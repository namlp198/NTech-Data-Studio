using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Desktop.ViewModels.Settings;

/// <summary>
/// One task's card in the automatic labeling section (decision 0019): its model, whether it works, and the score threshold.
/// What it describes depends on the settings, so <see cref="AutoLabelSettingsViewModel"/> asks it to <see cref="Refresh"/>.
/// </summary>
public sealed partial class AutoLabelTaskPanel : ObservableObject
{
    private readonly Func<string?> _path;
    private readonly Action<string?> _setPath;
    private readonly Func<bool> _showsModel;
    private readonly Func<string?> _missingText;
    private readonly Func<IAutoLabeler> _labeler;
    private readonly Func<double> _score;
    private readonly Action<double> _setScore;
    private readonly IFilePicker _files;
    private readonly string _pickTitle;

    public AutoLabelTaskPanel(
        string title, string description, string modelLabel, string pickTitle,
        Func<string?> path, Action<string?> setPath, Func<bool> showsModel, Func<string?> missingText,
        Func<IAutoLabeler> labeler, Func<double> score, Action<double> setScore, IFilePicker files)
    {
        Title = title;
        Description = description;
        ModelLabel = modelLabel;
        _pickTitle = pickTitle;
        _path = path;
        _setPath = setPath;
        _showsModel = showsModel;
        _missingText = missingText;
        _labeler = labeler;
        _score = score;
        _setScore = setScore;
        _files = files;
    }

    public string Title { get; }
    public string Description { get; }
    public string ModelLabel { get; }

    /// <summary>Whether this card has a model file of its own to choose (not when it borrows the Detect model).</summary>
    public bool ShowsModelRow => _showsModel();

    public string ModelPath => _path() ?? string.Empty;
    public bool HasModel => _path() is not null;

    /// <summary>The words of the state badge and its kind (ok, warn, error) for the colour.</summary>
    public string BadgeText => Problem is null ? "Sẵn sàng" : NeedsModel ? "Chưa có model" : "Có lỗi";
    public string BadgeKind => Problem is null ? "ok" : NeedsModel ? "warn" : "error";

    /// <summary>Why the task cannot be labeled yet, in words for the user; null when it can.</summary>
    public string? Problem => _missingText() ?? _labeler().Unavailable;

    public bool HasProblem => Problem is not null;

    private bool NeedsModel => _missingText() is not null;

    public string StatusText => Problem ?? $"Model dùng được. Class: {string.Join(", ", _labeler().ClassNames)}.";

    /// <summary>The score threshold in percent, 5 to 95.</summary>
    public double MinScorePercent
    {
        get => Math.Round(_score() * 100);
        set
        {
            _setScore(value / 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MinScoreText));
        }
    }

    public string MinScoreText => $"{Math.Round(_score() * 100)}%";

    public void Refresh()
    {
        foreach (var name in new[]
                 {
                     nameof(ShowsModelRow), nameof(ModelPath), nameof(HasModel), nameof(BadgeText), nameof(BadgeKind),
                     nameof(Problem), nameof(HasProblem), nameof(StatusText), nameof(MinScorePercent), nameof(MinScoreText)
                 })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void Choose()
    {
        var initial = _path() is { } current ? Path.GetDirectoryName(current) : null;
        if (_files.Pick(_pickTitle, "ONNX (*.onnx)|*.onnx", initial) is { } path) _setPath(path);
    }

    [RelayCommand]
    private void Clear() => _setPath(null);
}

/// <summary>
/// The automatic labeling section of Settings (decisions 0017, 0018 and 0019): the advanced switch, and one card per task,
/// Detect (boxes from a detect model) and Segment (polygons from a segment model, or from a detect model and SAM).
/// </summary>
public sealed partial class AutoLabelSettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    public AutoLabelSettingsViewModel(SettingsService settings, ConfiguredAutoLabeler labeler, SamModelService sam, IFilePicker files)
    {
        _settings = settings;
        Detect = new AutoLabelTaskPanel(
            "Detect (box)",
            "Model detect do Trainer xuất tìm các vật thể; mỗi vật thể thành một box.",
            "Model detect", "Chọn model detect (.onnx)",
            () => settings.AutoLabelDetectModel, settings.SetAutoLabelDetectModel,
            () => true,
            () => settings.AutoLabelDetectModel is null ? "Chưa chọn model detect. Chọn file .onnx do Trainer xuất (cạnh nó phải có model.meta.json)." : null,
            () => labeler.Detect,
            () => settings.AutoLabelDetectMinScore, settings.SetAutoLabelDetectMinScore, files);
        Segment = new AutoLabelTaskPanel(
            "Segment (polygon)",
            "Gán polygon: bằng model segment tự vẽ, hoặc bằng model detect rồi SAM cắt từng khung.",
            "Model segment", "Chọn model segment (.onnx)",
            () => settings.AutoLabelModel, settings.SetAutoLabelModel,
            () => settings.AutoLabelMode == AutoLabelMode.Segment,
            SegmentMissing,
            () => labeler.Active(TaskKind.Segment),
            () => settings.AutoLabelMinScore, settings.SetAutoLabelMinScore, files);

        _settings.Changed += (_, _) => Refresh();
        sam.Changed += (_, _) => Refresh();
    }

    public AutoLabelTaskPanel Detect { get; }
    public AutoLabelTaskPanel Segment { get; }

    public static IReadOnlyList<Option<AutoLabelMode>> Modes { get; } = new[]
    {
        new Option<AutoLabelMode>("Model segment (tự vẽ polygon)", AutoLabelMode.Segment),
        new Option<AutoLabelMode>("Model detect + SAM (tìm vật thể rồi cắt bằng SAM)", AutoLabelMode.DetectSam)
    };

    /// <summary>The advanced option itself: off, Annotate has no automatic labeling buttons.</summary>
    public bool Enabled
    {
        get => _settings.AutoLabelEnabled;
        set => _settings.SetAutoLabelEnabled(value);
    }

    public Option<AutoLabelMode> ModeOption
    {
        get => Modes.First(option => option.Value == _settings.AutoLabelMode);
        set
        {
            if (value is not null) _settings.SetAutoLabelMode(value.Value);
        }
    }

    /// <summary>What the chosen way to label polygons needs, in one line under the way.</summary>
    public string ModeHelp => _settings.AutoLabelMode == AutoLabelMode.DetectSam
        ? "Dùng model detect đã chọn ở mục Detect để tìm khung, rồi model SAM của Magic polygon cắt từng khung; vật thể nhỏ xíu được phân tích riêng quanh nó. "
          + "Khung nào SAM không cắt được thì giữ nguyên khung (polygon 4 đỉnh) để bạn sửa tay."
        : "Model segment do Trainer xuất tự vẽ polygon. Mask của nó thô hơn polygon của SAM, nhất là với vật thể nhỏ hoặc mảnh.";

    private string? SegmentMissing() => _settings.AutoLabelMode switch
    {
        AutoLabelMode.DetectSam => _settings.AutoLabelDetectModel is null ? "Chưa chọn model detect ở mục Detect (cách này dùng nó để tìm khung)." : null,
        _ => _settings.AutoLabelModel is null ? "Chưa chọn model segment. Chọn file .onnx do Trainer xuất (cạnh nó phải có model.meta.json)." : null
    };

    private void Refresh()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(ModeOption));
        OnPropertyChanged(nameof(ModeHelp));
        Detect.Refresh();
        Segment.Refresh();
    }
}
