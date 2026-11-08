namespace NTech.DataStudio.Application.Settings;

/// <summary>What is saved in settings.json: only what the user changed.</summary>
public sealed record AppSettings(IReadOnlyDictionary<string, IReadOnlyList<string>> Shortcuts)
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
