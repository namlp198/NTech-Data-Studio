using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace NTech.DataStudio.Trainer.Desktop.Services;

/// <summary>File pickers and the file manager through Avalonia, so the same code works on Windows and Ubuntu.</summary>
public sealed class AvaloniaDialogs : IDialogs
{
    private static TopLevel? Window =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> PickDataYamlAsync()
    {
        if (Window is not { } window) return null;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn data.yaml của dataset YOLO detect",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("data.yaml") { Patterns = ["*.yaml", "*.yml"] }]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Window is not { } window) return null;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task OpenFolderAsync(string path)
    {
        if (Window is not { } window) return;
        if (!await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)))
            throw new IOException($"Không mở được thư mục {path} bằng trình quản lý file.");
    }
}

/// <summary>settings.json under the user's application data (~/.config on Ubuntu).</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private Settings _settings;

    public JsonSettingsStore(string? folder = null)
    {
        var root = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NTechTrainer");
        _path = Path.Combine(root, "settings.json");
        _settings = Load(_path);
    }

    public string? LastWorkspace
    {
        get => _settings.LastWorkspace;
        set
        {
            _settings = _settings with { LastWorkspace = value };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_settings));
            }
            catch (IOException)
            {
                // Losing the last workspace is harmless; the user opens it again.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Settings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings(null) : new Settings(null);
        }
        catch (Exception problem) when (problem is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Settings(null);
        }
    }

    private sealed record Settings(string? LastWorkspace);
}
