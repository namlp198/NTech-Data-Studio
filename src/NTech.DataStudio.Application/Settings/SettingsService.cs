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
            MagicBoxMode = stored.MagicBoxMode;
            AutoLabelModel = stored.AutoLabelModel;
            AutoLabelMinScore = stored.AutoLabelMinScore;
            AutoLabelEnabled = stored.AutoLabelEnabled;
            AutoLabelMode = stored.AutoLabelMode;
            AutoLabelDetectModel = stored.AutoLabelDetectModel;
            AutoLabelDetectMinScore = stored.AutoLabelDetectMinScore;
        }
        catch (SettingsException exception)
        {
            Shortcuts = ShortcutMap.Defaults;
            Problem = $"{exception.Message} Đang dùng phím tắt mặc định.";
        }
    }

    public ShortcutMap Shortcuts { get; private set; }

    /// <summary>Magic polygon works from a dragged box (true) or from the pointer (false).</summary>
    public bool MagicBoxMode { get; private set; }

    public void SetMagicBoxMode(bool on)
    {
        if (MagicBoxMode == on) return;

        MagicBoxMode = on;
        Save();
    }

    /// <summary>Advanced option (decision 0018): automatic labeling is available at all. Off until the user turns it on.</summary>
    public bool AutoLabelEnabled { get; private set; }

    public void SetAutoLabelEnabled(bool on)
    {
        if (AutoLabelEnabled == on) return;

        AutoLabelEnabled = on;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Which kind of model labels the pictures.</summary>
    public AutoLabelMode AutoLabelMode { get; private set; }

    public void SetAutoLabelMode(AutoLabelMode mode)
    {
        if (AutoLabelMode == mode) return;

        AutoLabelMode = mode;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The detect .onnx used with SAM (decision 0018), or null. Kept apart from <see cref="AutoLabelModel"/> so switching modes loses neither.</summary>
    public string? AutoLabelDetectModel { get; private set; }

    public void SetAutoLabelDetectModel(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(path, AutoLabelDetectModel, StringComparison.Ordinal)) return;

        AutoLabelDetectModel = path;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The segment .onnx used for automatic labeling (decision 0017), or null.</summary>
    public string? AutoLabelModel { get; private set; }

    /// <summary>Lowest score an automatic label may have, 0.05 to 0.95.</summary>
    public double AutoLabelMinScore { get; private set; } = 0.4;

    public void SetAutoLabelModel(string? path)
    {
        path = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(path, AutoLabelModel, StringComparison.Ordinal)) return;

        AutoLabelModel = path;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lowest score of an automatic Detect label, 0.05 to 0.95 (decision 0019).</summary>
    public double AutoLabelDetectMinScore { get; private set; } = 0.4;

    public void SetAutoLabelDetectMinScore(double score)
    {
        score = Math.Round(Math.Clamp(score, 0.05, 0.95), 2);
        if (Math.Abs(score - AutoLabelDetectMinScore) < 1e-9) return;

        AutoLabelDetectMinScore = score;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The score threshold of the automatic labeling of a task.</summary>
    public double MinScoreFor(NTech.DataStudio.Domain.Profiles.TaskKind task) =>
        task == NTech.DataStudio.Domain.Profiles.TaskKind.Detect ? AutoLabelDetectMinScore : AutoLabelMinScore;

    public void SetAutoLabelMinScore(double score)
    {
        score = Math.Round(Math.Clamp(score, 0.05, 0.95), 2);
        if (Math.Abs(score - AutoLabelMinScore) < 1e-9) return;

        AutoLabelMinScore = score;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

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
            _store.Save(new AppSettings(Shortcuts.ToStored(), LastProject, MagicBoxMode, AutoLabelModel, AutoLabelMinScore,
                AutoLabelEnabled, AutoLabelMode, AutoLabelDetectModel, AutoLabelDetectMinScore));
            Problem = null;
        }
        catch (SettingsException exception)
        {
            Problem = $"{exception.Message} Thay đổi chỉ có hiệu lực trong lần chạy này.";
        }
    }
}
