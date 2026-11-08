using NTech.DataStudio.Application.Settings;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>Where the user's settings live between runs (decision 0015): per user, not per project.</summary>
public interface ISettingsStore
{
    /// <summary>The saved settings; defaults when nothing is saved yet.</summary>
    /// <exception cref="SettingsException">The file exists but cannot be read.</exception>
    AppSettings Load();

    /// <exception cref="SettingsException">The file cannot be written.</exception>
    void Save(AppSettings settings);
}
