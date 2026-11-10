using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.Shell;

namespace NTech.DataStudio.Desktop.ViewModels.Settings;

/// <summary>One action of the Hot-Key list: its name, its keys now, and the two buttons.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _keysText = string.Empty;

    /// <summary>Why the last key was refused. Shown under this row, because the list scrolls and a message at the top would be out of sight.</summary>
    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeButtonText))]
    private bool _isCapturing;

    public ShortcutRowViewModel(ShortcutInfo info, Action<ShortcutRowViewModel> change, Action<ShortcutRowViewModel> reset)
    {
        Info = info;
        ChangeCommand = new RelayCommand(() => change(this));
        ResetCommand = new RelayCommand(() => reset(this), () => IsCustom);
    }

    public ShortcutInfo Info { get; }
    public string Label => Info.Label;
    public IRelayCommand ChangeCommand { get; }
    public IRelayCommand ResetCommand { get; }

    public string ChangeButtonText => IsCapturing ? "Nhấn phím..." : "Đổi phím";
    public string ChangeAutomationName => $"Đổi phím {Label}";
    public string ResetAutomationName => $"Mặc định {Label}";

    partial void OnIsCustomChanged(bool value) => ResetCommand.NotifyCanExecuteChanged();
}

public sealed class ShortcutGroupViewModel
{
    public ShortcutGroupViewModel(string title, IReadOnlyList<ShortcutRowViewModel> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }
    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }
}

/// <summary>
/// The Settings screen. Its one section so far is Hot-Key: change the keys of the Annotate actions, saved at once
/// (decision 0015). A change takes effect without a restart because Annotate reads the same <see cref="SettingsService"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IActivatable
{
    private readonly SettingsService _settings;
    private readonly SamModelService _models;
    private readonly IFolderOpener _opener;
    private readonly IAutoLabeler _autoLabeler;
    private readonly IFilePicker _files;
    private readonly List<ShortcutRowViewModel> _rows = new();
    private ShortcutRowViewModel? _capturing;

    [ObservableProperty]
    private string? _statusMessage;

    public SettingsViewModel(SettingsService settings, SamModelService models, IFolderOpener opener, IAutoLabeler autoLabeler, IFilePicker files)
    {
        _settings = settings;
        _models = models;
        _opener = opener;
        _autoLabeler = autoLabeler;
        _files = files;
        _models.Changed += (_, _) => RefreshModel();

        var groups = new List<ShortcutGroupViewModel>();
        foreach (var group in ShortcutCatalog.All.GroupBy(info => info.Group))
        {
            var rows = group.Select(info => new ShortcutRowViewModel(info, ToggleCapture, Reset)).ToList();
            _rows.AddRange(rows);
            groups.Add(new ShortcutGroupViewModel(group.Key, rows));
        }

        Groups = groups;
        _settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; }

    // ---- Automatic labeling model (decision 0017) ----

    public string AutoLabelModelPath => _settings.AutoLabelModel ?? string.Empty;
    public bool HasAutoLabelModel => _settings.AutoLabelModel is not null;

    public string AutoLabelStatusText
    {
        get
        {
            if (_settings.AutoLabelModel is null)
                return "Chưa chọn model. Chọn file .onnx segment do Trainer xuất (cạnh nó phải có model.meta.json); Annotate sẽ có nút Gán tự động.";
            return _autoLabeler.Unavailable
                   ?? $"Model dùng được. Class: {string.Join(", ", _autoLabeler.ClassNames)}.";
        }
    }

    public bool AutoLabelProblem => _settings.AutoLabelModel is not null && _autoLabeler.Unavailable is not null;

    /// <summary>The score threshold in percent, 5 to 95.</summary>
    public double AutoLabelMinScorePercent
    {
        get => Math.Round(_settings.AutoLabelMinScore * 100);
        set
        {
            _settings.SetAutoLabelMinScore(value / 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoLabelMinScoreText));
        }
    }

    public string AutoLabelMinScoreText => $"Ngưỡng tin cậy {Math.Round(_settings.AutoLabelMinScore * 100)}%: model chỉ giữ vật thể có điểm từ mức này trở lên.";

    [RelayCommand]
    private void ChooseAutoLabelModel()
    {
        var current = _settings.AutoLabelModel;
        var initial = current is null ? null : Path.GetDirectoryName(current);
        if (_files.Pick("Chọn model segment (.onnx)", "ONNX (*.onnx)|*.onnx", initial) is { } path)
            _settings.SetAutoLabelModel(path);
    }

    [RelayCommand]
    private void ClearAutoLabelModel() => _settings.SetAutoLabelModel(null);

    // ---- Magic polygon model (decision 0016) ----

    public bool ModelReady => _models.IsReady;
    public bool ModelMissing => !_models.IsReady && !_models.IsDownloading;
    public bool IsDownloadingModel => _models.IsDownloading;
    public string ModelFolder => _models.Folder;
    public string? ModelError => _models.Error;

    public string ModelStatusText => _models.IsDownloading
        ? DownloadText()
        : _models.IsReady
            ? "Đã có đủ hai file model. Công cụ Magic polygon dùng được ở Annotate (project nhắm Segment)."
            : "Chưa có model. Cần tải hai file (khoảng 45 MB) một lần; sau đó công cụ chạy offline.";

    public double ModelProgressPercent => (_models.Progress?.Fraction ?? 0) * 100;

    private string DownloadText()
    {
        if (_models.Progress is not { } progress || progress.FileName.Length == 0) return "Đang tải model...";

        var total = progress.Total is { } size ? $" / {size / 1048576.0:0.0} MB" : string.Empty;
        return $"Đang tải {progress.FileName} ({progress.FileIndex + 1}/{progress.FileCount}): {progress.Received / 1048576.0:0.0} MB{total}";
    }

    private void RefreshModel()
    {
        foreach (var name in new[]
                 {
                     nameof(ModelReady), nameof(ModelMissing), nameof(IsDownloadingModel), nameof(ModelError),
                     nameof(ModelStatusText), nameof(ModelProgressPercent)
                 })
            OnPropertyChanged(name);
        DownloadModelCommand.NotifyCanExecuteChanged();
    }

    private bool CanDownloadModel() => !_models.IsReady && !_models.IsDownloading;

    [RelayCommand(CanExecute = nameof(CanDownloadModel))]
    private Task DownloadModelAsync() => _models.DownloadAsync();

    [RelayCommand]
    private void CancelModelDownload() => _models.Cancel();

    [RelayCommand]
    private void OpenModelFolder()
    {
        Directory.CreateDirectory(_models.Folder);
        _opener.Open(_models.Folder);
    }

    public bool IsCapturing => _capturing is not null;

    /// <summary>The file could not be read or the last change could not be saved.</summary>
    public string? ProblemMessage => _settings.Problem;

    public bool HasCustom => _settings.Shortcuts.HasCustom;

    public Task ActivateAsync()
    {
        Refresh();
        RefreshModel();
        return Task.CompletedTask;
    }

    public void Deactivate() => StopCapture();

    private void Refresh()
    {
        foreach (var row in _rows)
        {
            row.KeysText = string.Join("   ", _settings.Shortcuts.For(row.Info.Action).Select(key => key.Display));
            row.IsCustom = _settings.Shortcuts.IsCustom(row.Info.Action);
        }

        OnPropertyChanged(nameof(ProblemMessage));
        OnPropertyChanged(nameof(HasCustom));
        foreach (var name in new[]
                 {
                     nameof(AutoLabelModelPath), nameof(HasAutoLabelModel), nameof(AutoLabelStatusText), nameof(AutoLabelProblem),
                     nameof(AutoLabelMinScorePercent), nameof(AutoLabelMinScoreText)
                 })
            OnPropertyChanged(name);
        ResetAllCommand.NotifyCanExecuteChanged();
    }

    private void ToggleCapture(ShortcutRowViewModel row)
    {
        var wasThis = ReferenceEquals(_capturing, row);
        StopCapture();
        if (wasThis) return;

        _capturing = row;
        row.IsCapturing = true;
        row.ErrorText = null;
        StatusMessage = $"Nhấn phím mới cho \"{row.Label}\". Esc để hủy.";
        OnPropertyChanged(nameof(IsCapturing));
    }

    private void StopCapture()
    {
        if (_capturing is { } row)
        {
            row.IsCapturing = false;
            row.ErrorText = null;
        }

        _capturing = null;
        OnPropertyChanged(nameof(IsCapturing));
    }

    private void Reset(ShortcutRowViewModel row)
    {
        StopCapture();
        row.ErrorText = null;
        _settings.Reset(row.Info.Action);
        StatusMessage = $"\"{row.Label}\" về phím mặc định: {row.KeysText}.";
    }

    private bool CanResetAll() => _settings.Shortcuts.HasCustom;

    [RelayCommand(CanExecute = nameof(CanResetAll))]
    private void ResetAll()
    {
        StopCapture();
        _settings.ResetAll();
        StatusMessage = "Mọi phím tắt đã về mặc định.";
    }

    /// <summary>
    /// The view sends every key here while a row is waiting for one. True when it was used up, so the view marks it handled:
    /// Esc cancels, a lone Ctrl or Shift is ignored, anything else becomes the row's key or says why it cannot.
    /// </summary>
    public bool HandleCapture(Key key, ModifierKeys modifiers)
    {
        if (_capturing is not { } row) return false;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopCapture();
            StatusMessage = null;
            return true;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.System or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return true;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            row.ErrorText = "Không dùng được tổ hợp có Alt. Hãy thử Ctrl hoặc Shift.";
            return true;
        }

        var shortcut = new Shortcut(
            KeyNames.Of(key),
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift));
        if (_settings.Assign(row.Info.Action, shortcut) is { } problem)
        {
            row.ErrorText = problem;
            return true;
        }

        StopCapture();
        StatusMessage = $"\"{row.Label}\" dùng {shortcut.Display}.";
        return true;
    }
}
