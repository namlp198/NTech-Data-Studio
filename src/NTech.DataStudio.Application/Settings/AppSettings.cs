namespace NTech.DataStudio.Application.Settings;

/// <summary>What is saved in settings.json: only what the user changed.</summary>
/// <param name="Shortcuts">Rebound shortcuts.</param>
/// <param name="MagicBoxMode">Magic polygon waits for a box dragged around the object (and analyses the zoomed part) instead of following the pointer.</param>
/// <param name="AutoLabelModel">Path of the segment .onnx the Trainer exported, used to label pictures automatically (decision 0017); null when none.</param>
/// <param name="AutoLabelMinScore">Objects the model scores below this are left out, 0.05 to 0.95.</param>
/// <param name="LastProject">Folder of the project open when the app was last used; null when none (or it was closed on purpose).</param>
public sealed record AppSettings(IReadOnlyDictionary<string, IReadOnlyList<string>> Shortcuts, string? LastProject = null, bool MagicBoxMode = false, string? AutoLabelModel = null, double AutoLabelMinScore = 0.4)
{
    public static AppSettings Empty { get; } = new(new Dictionary<string, IReadOnlyList<string>>());
}

/// <summary>Settings could not be read or written; <see cref="Exception.Message"/> is shown to the user.</summary>
public sealed class SettingsException : Exception
{
    public SettingsException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
