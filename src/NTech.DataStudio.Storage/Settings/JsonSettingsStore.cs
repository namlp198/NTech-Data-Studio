using System.Text.Json;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Settings;

namespace NTech.DataStudio.Storage.Settings;

/// <summary>settings.json under the user's profile. Written beside its name and renamed, so a crash never leaves half a file.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public JsonSettingsStore(string path)
    {
        _path = path;
    }

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NTech", "DataStudio", "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return AppSettings.Empty;

        try
        {
            var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), Options);
            return new AppSettings(
                file?.Shortcuts?.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)(pair.Value ?? new List<string>()))
                ?? new Dictionary<string, IReadOnlyList<string>>(),
                string.IsNullOrWhiteSpace(file?.LastProject) ? null : file.LastProject,
                file?.MagicBoxMode ?? false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new SettingsException($"Không đọc được file cài đặt \"{_path}\": {exception.Message}", exception);
        }
    }

    public void Save(AppSettings settings)
    {
        var temp = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var file = new SettingsFile
            {
                Version = FormatVersion,
                Shortcuts = settings.Shortcuts.ToDictionary(pair => pair.Key, pair => (List<string>?)pair.Value.ToList()),
                LastProject = settings.LastProject,
                MagicBoxMode = settings.MagicBoxMode
            };
            File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SettingsException($"Không lưu được file cài đặt \"{_path}\": {exception.Message}", exception);
        }
    }

    private sealed class SettingsFile
    {
        public int Version { get; set; }
        public Dictionary<string, List<string>?>? Shortcuts { get; set; }
        public string? LastProject { get; set; }
        public bool MagicBoxMode { get; set; }
    }
}
