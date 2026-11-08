namespace NTech.DataStudio.Application.Settings;

/// <summary>
/// Which keys run which action. Immutable: a change makes a new map. Holds only what the user changed
/// on top of the defaults, so an action added in a later version gets its default keys.
/// A key belongs to at most one action.
/// </summary>
public sealed class ShortcutMap
{
    private readonly IReadOnlyDictionary<ShortcutAction, IReadOnlyList<Shortcut>> _overrides;
    private readonly Dictionary<ShortcutAction, IReadOnlyList<Shortcut>> _effective;
    private readonly Dictionary<Shortcut, ShortcutAction> _lookup;
    private readonly HashSet<ShortcutAction> _clashing = new();

    private ShortcutMap(IReadOnlyDictionary<ShortcutAction, IReadOnlyList<Shortcut>> overrides)
    {
        _overrides = overrides;
        _effective = new Dictionary<ShortcutAction, IReadOnlyList<Shortcut>>();
        _lookup = new Dictionary<Shortcut, ShortcutAction>();
        foreach (var info in ShortcutCatalog.All)
        {
            var keys = overrides.TryGetValue(info.Action, out var custom) ? custom : info.Defaults;
            _effective[info.Action] = keys;
            foreach (var key in keys)
            {
                if (_lookup.TryGetValue(key, out var owner) && owner != info.Action)
                {
                    _clashing.Add(owner);
                    _clashing.Add(info.Action);
                }

                _lookup[key] = info.Action;
            }
        }
    }

    public static ShortcutMap Defaults { get; } = new(new Dictionary<ShortcutAction, IReadOnlyList<Shortcut>>());

    /// <summary>Keys of an action, in the order they were set.</summary>
    public IReadOnlyList<Shortcut> For(ShortcutAction action) => _effective[action];

    public bool IsCustom(ShortcutAction action) => _overrides.ContainsKey(action);

    public bool HasCustom => _overrides.Count > 0;

    /// <summary>The action a key press runs, or null. Shift is ignored when only an unshifted key is bound, so "+" works on every layout.</summary>
    public ShortcutAction? Find(Shortcut pressed)
    {
        if (_lookup.TryGetValue(pressed, out var action)) return action;
        if (pressed.Shift && !pressed.Ctrl && _lookup.TryGetValue(pressed with { Shift = false }, out action)) return action;
        return null;
    }

    /// <summary>The other action that already uses this exact key, or null.</summary>
    public ShortcutAction? ConflictOf(Shortcut shortcut, ShortcutAction except) =>
        _lookup.TryGetValue(shortcut, out var action) && action != except ? action : null;

    /// <summary>Gives the action these keys instead of its own. Back to the defaults drops the override.</summary>
    /// <exception cref="ShortcutException">A key cannot be a shortcut, or another action holds it.</exception>
    public ShortcutMap With(ShortcutAction action, IReadOnlyList<Shortcut> keys)
    {
        if (keys.Count == 0) throw new ShortcutException("Cần ít nhất một phím.");
        foreach (var key in keys)
        {
            if (key.Problem() is { } problem) throw new ShortcutException(problem);
            if (ConflictOf(key, action) is { } other)
                throw new ShortcutException($"{key.Display} đang dùng cho \"{ShortcutCatalog.Of(other).Label}\".");
        }

        var copy = _overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (keys.SequenceEqual(ShortcutCatalog.Of(action).Defaults)) copy.Remove(action);
        else copy[action] = keys.ToList();
        return new ShortcutMap(copy);
    }

    public ShortcutMap Reset(ShortcutAction action)
    {
        if (!_overrides.ContainsKey(action)) return this;
        var copy = _overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        copy.Remove(action);
        return new ShortcutMap(copy);
    }

    /// <summary>What is saved: the actions whose keys differ from the defaults.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ToStored() =>
        _overrides.ToDictionary(pair => pair.Key.ToString(), pair => (IReadOnlyList<string>)pair.Value.Select(key => key.ToString()).ToList());

    /// <summary>
    /// Reads what was saved. Unknown actions and unreadable keys are ignored (that action keeps its defaults), and
    /// when two actions end up with one key the later one goes back to its defaults, so a hand-edited or
    /// older file can never leave a key doing two things.
    /// </summary>
    public static ShortcutMap FromStored(IReadOnlyDictionary<string, IReadOnlyList<string>>? stored)
    {
        var overrides = new Dictionary<ShortcutAction, IReadOnlyList<Shortcut>>();
        foreach (var (name, texts) in stored ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            if (!Enum.TryParse<ShortcutAction>(name, out var action) || !Enum.IsDefined(action)) continue;

            var keys = new List<Shortcut>();
            var valid = texts.Count > 0;
            foreach (var text in texts)
            {
                if (Shortcut.TryParse(text, out var key) && key.Problem() is null) keys.Add(key);
                else valid = false;
            }

            if (valid && !keys.SequenceEqual(ShortcutCatalog.Of(action).Defaults))
                overrides[action] = keys;
        }

        // Defaults never clash with each other, so a clash always involves an override; dropping one ends the loop.
        while (true)
        {
            var map = new ShortcutMap(overrides);
            var drop = overrides.Keys.Where(map._clashing.Contains).OrderByDescending(action => action).Cast<ShortcutAction?>().FirstOrDefault();
            if (drop is null) return map;
            overrides.Remove(drop.Value);
        }
    }
}
