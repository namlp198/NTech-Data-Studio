using NTech.DataStudio.Application.Abstractions;

namespace NTech.DataStudio.Application.Settings;

/// <summary>
/// The settings of the running app. A change is saved at once and takes effect at once: screens read
/// <see cref="Shortcuts"/> on every key press and listen to <see cref="Changed"/> for what they show.
/// </summary>
public sealed class SettingsService
{
    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store)
    {
        _store = store;
        try
        {
            var stored = store.Load();
            Shortcuts = ShortcutMap.FromStored(stored.Shortcuts);
            LastProject = stored.LastProject;
        }
        catch (SettingsException exception)
        {
            Shortcuts = ShortcutMap.Defaults;
            Problem = $"{exception.Message} Đang dùng phím tắt mặc định.";
        }
    }

    public ShortcutMap Shortcuts { get; private set; }

    /// <summary>Folder of the project to open at start, or null.</summary>
    public string? LastProject { get; private set; }

    /// <summary>Remembers which project is open (null: none, so the next start opens none). Does not raise <see cref="Changed"/>.</summary>
    public void RememberProject(string? folder)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? null : folder;
        if (string.Equals(folder, LastProject, StringComparison.Ordinal)) return;

        LastProject = folder;
        Save();
    }

    /// <summary>Set when the file could not be read at start or the last change could not be saved; cleared by a successful save.</summary>
    public string? Problem { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Gives the action one key in place of its current ones.</summary>
    /// <returns>Null when done, otherwise why not (a key another action holds, Space, a digit).</returns>
    public string? Assign(ShortcutAction action, Shortcut key)
    {
        try
        {
            Apply(Shortcuts.With(action, new[] { key }));
            return null;
        }
        catch (ShortcutException exception)
        {
            return exception.Message;
        }
    }

    public void Reset(ShortcutAction action) => Apply(Shortcuts.Reset(action));

    public void ResetAll() => Apply(ShortcutMap.Defaults);

    private void Apply(ShortcutMap map)
    {
        if (ReferenceEquals(map, Shortcuts)) return;

        // The change holds for this run even when the disk refuses it; the problem says it will not survive a restart.
        Shortcuts = map;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        try
        {
            _store.Save(new AppSettings(Shortcuts.ToStored(), LastProject));
            Problem = null;
        }
        catch (SettingsException exception)
        {
            Problem = $"{exception.Message} Thay đổi chỉ có hiệu lực trong lần chạy này.";
        }
    }
}
