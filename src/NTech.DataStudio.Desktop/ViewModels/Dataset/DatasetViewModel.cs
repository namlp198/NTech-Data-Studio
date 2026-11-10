using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.Shell;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Desktop.ViewModels.Dataset;

/// <summary>
/// Module 3: split, preprocessing and augmentation settings, the numbers they produce,
/// a preview of real output pictures, and the versions already saved. Every edit
/// recomputes the plan after a short pause; nothing is written until "Tạo version".
/// </summary>
public sealed partial class DatasetViewModel : ObservableObject, IActivatable
{
    private const int DebounceMs = 250;
    private const int PreviewCount = 9;

    private static readonly HashSet<string> ConfigProperties = new()
    {
        nameof(TrainText), nameof(ValText), nameof(TestText), nameof(SeedText),
        nameof(Stratify), nameof(GroupByFolder), nameof(IncludeUnlabeled),
        nameof(SelectedResize), nameof(WidthText), nameof(HeightText), nameof(Grayscale),
        nameof(DropNullText), nameof(MultiplierText)
    };

    private readonly ProjectService _projects;
    private readonly DatasetService _service;
    private readonly IUserPrompt _prompt;

    private DatasetWorkspace? _workspace;
    private DatasetConfig? _config;
    private VersionPlan? _plan;
    private IReadOnlyDictionary<long, Color> _classColors = new Dictionary<long, Color>();
    private CancellationTokenSource? _work;
    private string? _loadedRoot;
    private int _previewShuffle;
    private bool _suspend;
    private bool _active;

    [ObservableProperty] private string _trainText = "70";
    [ObservableProperty] private string _valText = "20";
    [ObservableProperty] private string _testText = "10";
    [ObservableProperty] private string _seedText = "42";
    [ObservableProperty] private bool _stratify = true;
    [ObservableProperty] private bool _groupByFolder = true;
    [ObservableProperty] private bool _includeUnlabeled;

    [ObservableProperty] private Option<ResizeMode> _selectedResize;
    [ObservableProperty] private string _widthText = "640";
    [ObservableProperty] private string _heightText = "640";
    [ObservableProperty] private bool _grayscale;
    [ObservableProperty] private string _dropNullText = "0";

    [ObservableProperty] private string _multiplierText = "1";

    [ObservableProperty] private string _versionName = "v001";

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private bool _hasImages;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _configError;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _resultMessage;
    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty] private string? _previewError;
    [ObservableProperty] private string _previewNote = string.Empty;
    [ObservableProperty] private bool _canCreate;

    public DatasetViewModel(ProjectService projects, DatasetService service, IUserPrompt prompt)
    {
        _projects = projects;
        _service = service;
        _prompt = prompt;

        ResizeOptions = new[]
        {
            new Option<ResizeMode>("Giữ nguyên master (khuyến nghị cho YOLO26)", ResizeMode.None),
            new Option<ResizeMode>("Stretch: kéo giãn về kích thước đích", ResizeMode.Stretch),
            new Option<ResizeMode>("Fit: vừa khung, thêm viền xám (letterbox)", ResizeMode.Fit),
            new Option<ResizeMode>("Fill: phủ kín khung, cắt phần thừa", ResizeMode.Fill)
        };
        _selectedResize = ResizeOptions[0];
        AugmentRows = BuildAugmentRows();

        _projects.CurrentChanged += OnProjectChanged;
    }

    public IReadOnlyList<Option<ResizeMode>> ResizeOptions { get; }
    public IReadOnlyList<AugmentOptionRow> AugmentRows { get; }

    public ObservableCollection<ClassMapRow> ClassMapRows { get; } = new();
    public ObservableCollection<MetricRow> Metrics { get; } = new();
    public ObservableCollection<ClassCountRow> ClassRows { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();
    public ObservableCollection<VersionRow> Versions { get; } = new();
    public ObservableCollection<PreviewTileViewModel> Tiles { get; } = new();

    public bool HasProject => _projects.Current is not null;
    public bool ShowForm => HasProject && HasImages && !IsLoading;
    public bool ShowNoImages => HasProject && !HasImages && !IsLoading && LoadError is null;
    public bool IsResizeEnabled => SelectedResize.Value != ResizeMode.None;
    public bool HasClasses => ClassMapRows.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
    public bool HasVersions => Versions.Count > 0;
    public bool HasTiles => Tiles.Count > 0;
    public bool HasPlan => _plan is { TotalImages: > 0 };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(SelectedResize):
                OnPropertyChanged(nameof(IsResizeEnabled));
                break;
            case nameof(HasImages) or nameof(IsLoading) or nameof(LoadError):
                OnPropertyChanged(nameof(ShowForm));
                OnPropertyChanged(nameof(ShowNoImages));
                break;
        }

        if (e.PropertyName is { } name && ConfigProperties.Contains(name))
            OnConfigEdited();
    }

    // ---- activation and loading ----

    public async Task ActivateAsync()
    {
        _active = true;
        OnPropertyChanged(nameof(HasProject));
        if (HasProject)
            await LoadAsync();
    }

    public void Deactivate()
    {
        _active = false;
        _work?.Cancel();
        IsPreviewLoading = false;
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ShowForm));
        OnPropertyChanged(nameof(ShowNoImages));

        var root = _projects.Current?.RootPath;
        if (root is null)
        {
            _work?.Cancel();
            _workspace = null;
            _loadedRoot = null;
            HasImages = false;
            ClearResults();
        }
        else if (_active && root != _loadedRoot)
        {
            // A different project: everything shown belongs to the old one.
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => HasProject ? LoadAsync() : Task.CompletedTask;

    private async Task LoadAsync()
    {
        _work?.Cancel();
        IsLoading = true;
        LoadError = null;
        try
        {
            var (workspace, versions) = await Task.Run(() =>
            {
                var loaded = _service.Load();
                return (loaded, loaded.Versions.Select(DescribeVersion).ToList());
            });

            _workspace = workspace;
            _loadedRoot = workspace.ProjectRoot;
            _classColors = workspace.Classes.ToDictionary(labelClass => labelClass.Id, labelClass => ParseColor(labelClass.Color));
            HasImages = workspace.Images.Count > 0;

            Versions.Clear();
            foreach (var row in versions)
                Versions.Add(row);
            OnPropertyChanged(nameof(HasVersions));
            VersionName = NextVersionName(workspace.Versions);

            RebuildClassMap(workspace);
            if (HasImages)
                await RecomputeAsync(debounce: false, withPreview: true);
            else
                ClearResults();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LoadError = exception.Message;
            HasImages = false;
            ClearResults();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildClassMap(DatasetWorkspace workspace)
    {
        var previous = ClassMapRows.ToDictionary(row => row.ClassId, row => row.ToEntry());
        _suspend = true;
        try
        {
            ClassMapRows.Clear();
            foreach (var labelClass in workspace.Classes)
            {
                var row = new ClassMapRow(labelClass, workspace.Classes, OnConfigEdited);
                if (previous.TryGetValue(labelClass.Id, out var entry) && entry is not null
                    && (entry.TargetClassId is null || workspace.Classes.Any(other => other.Id == entry.TargetClassId)))
                    row.Select(entry);
                ClassMapRows.Add(row);
            }
        }
        finally
        {
            _suspend = false;
        }

        OnPropertyChanged(nameof(HasClasses));
    }

    private VersionRow DescribeVersion(DatasetVersion version)
    {
        var items = _service.LoadAssignments(version.Id);
        var counts = new int[3];
        foreach (var item in items)
            counts[(int)item.Split]++;

        string summary;
        try
        {
            var config = DatasetService.ConfigOf(version);
            var multiplier = config.Augment.IsEnabled ? $" ×{config.Augment.Multiplier}" : string.Empty;
            summary = $"{items.Count} ảnh · train {counts[0]}{multiplier} · val {counts[1]} · test {counts[2]} · seed {version.Seed} · {version.CreatedAt.LocalDateTime:dd/MM/yyyy HH:mm}";
        }
        catch (JsonException)
        {
            summary = $"{items.Count} ảnh · không đọc được cấu hình (version tạo bởi phiên bản khác của ứng dụng)";
        }

        return new VersionRow(version.Id, version.Name, summary);
    }

    private static string NextVersionName(IReadOnlyList<DatasetVersion> versions)
    {
        var number = versions
            .Select(version => version.Name.StartsWith('v') && int.TryParse(version.Name.AsSpan(1), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var name = $"v{number:000}";
        while (versions.Any(version => version.Name == name))
            name = $"v{++number:000}";
        return name;
    }

    private static Color ParseColor(string text)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    // ---- recomputing ----

    private void OnConfigEdited()
    {
        if (_suspend || _workspace is null || !HasImages) return;
        _ = RecomputeAsync(debounce: true, withPreview: true);
    }

    /// <summary>Builds the config from the form, plans the version, then refreshes the preview. A newer edit cancels an older run.</summary>
    private async Task RecomputeAsync(bool debounce, bool withPreview)
    {
        _work?.Cancel();
        var cancellation = _work = new CancellationTokenSource();
        var token = cancellation.Token;

        try
        {
            if (debounce)
                await Task.Delay(DebounceMs, token);

            var workspace = _workspace;
            if (workspace is null) return;

            if (!TryBuildConfig(out var config, out var error))
            {
                ConfigError = error;
                ClearResults();
                return;
            }

            var plan = await Task.Run(() => VersionBuilder.Build(workspace.Images, workspace.Classes, config), token);
            token.ThrowIfCancellationRequested();

            ConfigError = null;
            ApplyPlan(config, plan);
            if (withPreview)
                await RefreshPreviewAsync(workspace, config, plan, token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit: that run reports.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ConfigError = exception.Message;
        }
    }

    private void ApplyPlan(DatasetConfig config, VersionPlan plan)
    {
        _config = config;
        _plan = plan;
        CanCreate = plan.TotalImages > 0 && !IsBusy;
        OnPropertyChanged(nameof(HasPlan));

        var total = Math.Max(1, plan.TotalImages);
        string Share(int index) => $"{100.0 * plan.ImagesPerSplit[index] / total:0}% · {plan.LabelsPerSplit[index]} nhãn";

        Metrics.Clear();
        Metrics.Add(new MetricRow("Train", plan.ImagesPerSplit[0].ToString(), Share(0)));
        Metrics.Add(new MetricRow("Val", plan.ImagesPerSplit[1].ToString(), Share(1)));
        Metrics.Add(new MetricRow("Test", plan.ImagesPerSplit[2].ToString(), Share(2)));
        Metrics.Add(new MetricRow(
            "Xuất ra",
            plan.ExpectedTotalOutput.ToString(),
            config.Augment.IsEnabled
                ? $"train {plan.ImagesPerSplit[0]} × {config.Augment.Multiplier} + val + test"
                : "chưa tăng cường"));

        ClassRows.Clear();
        foreach (var row in plan.Classes)
        {
            var isLow = row.TotalLabels == 0
                        || row.Labels[(int)SplitKind.Train] < VersionBuilder.MinTrainLabels
                        || (config.Split.ValPercent > 0 && row.Labels[(int)SplitKind.Val] == 0);
            ClassRows.Add(new ClassCountRow(
                row.Name,
                $"{row.Images[0]} / {row.Labels[0]}",
                $"{row.Images[1]} / {row.Labels[1]}",
                $"{row.Images[2]} / {row.Labels[2]}",
                row.TotalLabels.ToString(),
                isLow));
        }

        Warnings.Clear();
        foreach (var warning in plan.Warnings)
            Warnings.Add(warning);
        OnPropertyChanged(nameof(HasWarnings));

        Notes.Clear();
        if (plan.TotalImages > 0)
            Notes.Add($"{plan.UnitCount} nhóm ảnh được chia (ảnh cùng nhóm luôn nằm chung một tập).");
        if (plan.ExcludedUnlabeled > 0)
            Notes.Add($"{plan.ExcludedUnlabeled} ảnh còn ở trạng thái New nên chưa đưa vào. Bật \"Gồm ảnh chưa gán nhãn\" nếu muốn thêm.");
        if (plan.ExcludedUnselected > 0)
            Notes.Add($"{plan.ExcludedUnselected} ảnh chưa được chọn vào dataset (chế độ \"Chỉ đưa ảnh được chọn\" ở màn Annotate đang bật).");
        if (plan.DroppedNull > 0)
            Notes.Add($"{plan.DroppedNull} ảnh không nhãn bị bỏ theo tỉ lệ đã chọn.");
        if (plan.EmptyImages > 0)
            Notes.Add($"{plan.EmptyImages} ảnh không có nhãn được giữ làm ảnh nền.");

        PreviewNote = config.Augment.IsEnabled
            ? "Mỗi ô là ảnh sẽ được xuất ra khi tạo version, kèm nhãn sau biến đổi. Chỉ lấy từ tập train."
            : "Chưa bật tăng cường (hệ số nhân 1): các ô hiện ảnh gốc sau preprocessing. Chọn hệ số nhân từ 2 trở lên để xem ảnh tăng cường.";
    }

    private void ClearResults()
    {
        _config = null;
        _plan = null;
        CanCreate = false;
        IsPreviewLoading = false;
        Metrics.Clear();
        ClassRows.Clear();
        Warnings.Clear();
        Notes.Clear();
        Tiles.Clear();
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(HasTiles));
        OnPropertyChanged(nameof(HasPlan));
    }

    private async Task RefreshPreviewAsync(DatasetWorkspace workspace, DatasetConfig config, VersionPlan plan, CancellationToken token)
    {
        if (plan.TotalImages == 0)
        {
            Tiles.Clear();
            OnPropertyChanged(nameof(HasTiles));
            return;
        }

        IsPreviewLoading = true;
        PreviewError = null;
        var shuffle = _previewShuffle;
        var colors = _classColors;
        try
        {
            var tiles = await Task.Run(
                () => _service.RenderPreview(workspace, config, plan, PreviewCount, shuffle, token)
                    .Select(tile => PreviewTileViewModel.From(tile, colors))
                    .ToList(),
                token);
            token.ThrowIfCancellationRequested();

            Tiles.Clear();
            foreach (var tile in tiles)
                Tiles.Add(tile);
            OnPropertyChanged(nameof(HasTiles));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            PreviewError = exception.Message;
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private async Task ShufflePreviewAsync()
    {
        if (_workspace is not { } workspace || _config is not { } config || _plan is not { } plan) return;

        _previewShuffle++;
        _work?.Cancel();
        var cancellation = _work = new CancellationTokenSource();
        try
        {
            await RefreshPreviewAsync(workspace, config, plan, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer edit took over.
        }
    }

    // ---- the form as a config ----

    private bool TryBuildConfig(out DatasetConfig config, out string? error)
    {
        config = new DatasetConfig();

        if (!NumberText.TryParseInt(TrainText, out var train)
            || !NumberText.TryParseInt(ValText, out var val)
            || !NumberText.TryParseInt(TestText, out var test))
        {
            error = "Tỉ lệ train, val, test phải là số nguyên.";
            return false;
        }

        if (!NumberText.TryParseInt(SeedText, out var seed))
        {
            error = "Seed phải là số nguyên.";
            return false;
        }

        var width = 640;
        var height = 640;
        if (IsResizeEnabled
            && (!NumberText.TryParseInt(WidthText, out width) || !NumberText.TryParseInt(HeightText, out height)))
        {
            error = "Kích thước đích phải là số nguyên.";
            return false;
        }

        if (!NumberText.TryParseInt(DropNullText, out var dropNull))
        {
            error = "Tỉ lệ bỏ ảnh Null phải là số nguyên.";
            return false;
        }

        if (!NumberText.TryParseInt(MultiplierText, out var multiplier))
        {
            error = "Hệ số nhân phải là số nguyên.";
            return false;
        }

        var augment = new AugmentConfig { Multiplier = multiplier };
        foreach (var row in AugmentRows)
        {
            if (!row.TryApply(augment, out augment, out error))
                return false;
        }

        config = new DatasetConfig
        {
            Seed = seed,
            Split = new SplitConfig
            {
                TrainPercent = train,
                ValPercent = val,
                TestPercent = test,
                Stratify = Stratify,
                GroupByFolder = GroupByFolder,
                IncludeUnlabeled = IncludeUnlabeled
            },
            Preprocess = new PreprocessConfig
            {
                Resize = SelectedResize.Value,
                Width = width,
                Height = height,
                Grayscale = Grayscale,
                DropNullPercent = dropNull,
                ClassMap = ClassMapRows.Select(row => row.ToEntry()).OfType<ClassMapEntry>().ToList()
            },
            Augment = augment
        };
        error = config.Validate();
        return error is null;
    }

    private AugmentOptionRow[] BuildAugmentRows()
    {
        AugmentOptionRow Switch(string key, string label, Func<AugmentConfig, bool, AugmentConfig> apply) =>
            new(key, label, null, hasToggle: true, hasValue: false, 1, 0, (config, value) => apply(config, value > 0), OnConfigEdited);

        AugmentOptionRow Range(string key, string label, string unit, double initial, Func<AugmentConfig, double, AugmentConfig> apply, double off = 0) =>
            new(key, label, unit, hasToggle: true, hasValue: true, initial, off, apply, OnConfigEdited);

        AugmentOptionRow Plain(string key, string label, string unit, double initial, Func<AugmentConfig, double, AugmentConfig> apply) =>
            new(key, label, unit, hasToggle: false, hasValue: true, initial, initial, apply, OnConfigEdited);

        static int Whole(double value) => (int)Math.Round(value);

        return new[]
        {
            Switch("flipH", "Lật ngang", (c, on) => c with { FlipHorizontal = on }),
            Switch("flipV", "Lật dọc", (c, on) => c with { FlipVertical = on }),
            Switch("rot90cw", "Xoay 90° theo chiều kim đồng hồ", (c, on) => c with { Rotate90Clockwise = on }),
            Switch("rot90ccw", "Xoay 90° ngược chiều kim đồng hồ", (c, on) => c with { Rotate90CounterClockwise = on }),
            Switch("rot180", "Xoay 180°", (c, on) => c with { Rotate180 = on }),
            Range("rotate", "Xoay nhỏ", "± độ", 10, (c, v) => c with { RotateDegrees = v }),
            Range("scale", "Zoom / scale", "± %", 10, (c, v) => c with { ScalePercent = v }),
            Range("translate", "Dịch chuyển", "± %", 5, (c, v) => c with { TranslatePercent = v }),
            Range("shear", "Shear", "± độ", 5, (c, v) => c with { ShearDegrees = v }),
            Range("brightness", "Độ sáng", "± %", 20, (c, v) => c with { BrightnessPercent = v }),
            Range("exposure", "Phơi sáng", "± %", 20, (c, v) => c with { ExposurePercent = v }),
            Range("contrast", "Tương phản", "± %", 20, (c, v) => c with { ContrastPercent = v }),
            Range("gamma", "Gamma", "± %", 15, (c, v) => c with { GammaPercent = v }),
            Range("hue", "Hue", "± độ", 15, (c, v) => c with { HueDegrees = v }),
            Range("saturation", "Bão hòa", "± %", 20, (c, v) => c with { SaturationPercent = v }),
            Range("gray", "Ảnh xám ngẫu nhiên", "% số ảnh", 10, (c, v) => c with { GrayscalePercent = Whole(v) }),
            Range("blur", "Gaussian blur", "tối đa px", 1.5, (c, v) => c with { BlurPx = v }),
            Range("motion", "Motion blur", "tối đa px", 7, (c, v) => c with { MotionBlurPx = v }),
            Range("noise", "Nhiễu", "tối đa %", 3, (c, v) => c with { NoisePercent = v }),
            Range("jpeg", "Nén JPEG gây vỡ ảnh", "chất lượng thấp nhất", 70, (c, v) => c with { JpegQualityMin = Whole(v) }, off: 100),
            Range("cutout", "Cutout", "số ô", 2, (c, v) => c with { CutoutCount = Whole(v) }),
            Plain("cutoutSize", "Kích thước ô cutout", "% cạnh ảnh", 10, (c, v) => c with { CutoutSizePercent = Whole(v) }),
            Range("cutoutDrop", "Bỏ nhãn bị cutout che từ", "% diện tích", 50, (c, v) => c with { CutoutDropLabelPercent = Whole(v) }),
            Plain("minVisible", "Bỏ nhãn còn dưới", "% diện tích trong ảnh", 30, (c, v) => c with { MinVisiblePercent = Whole(v) })
        };
    }

    // ---- presets ----

    [RelayCommand]
    private void ApplyPreset(string? name)
    {
        var values = name switch
        {
            "factory" => new Dictionary<string, double>
            {
                ["brightness"] = 20, ["exposure"] = 15, ["contrast"] = 15, ["gamma"] = 10,
                ["blur"] = 1.5, ["motion"] = 5, ["noise"] = 3, ["jpeg"] = 70, ["rotate"] = 3
            },
            "geometry" => new Dictionary<string, double>
            {
                ["flipH"] = 1, ["rotate"] = 10, ["scale"] = 10, ["translate"] = 5,
                ["brightness"] = 15, ["contrast"] = 15, ["hue"] = 10, ["saturation"] = 15,
                ["blur"] = 1.5, ["noise"] = 2
            },
            _ => new Dictionary<string, double>()
        };

        _suspend = true;
        try
        {
            MultiplierText = name switch { "factory" or "geometry" => "3", _ => "1" };
            foreach (var row in AugmentRows)
            {
                if (!row.HasToggle) continue;
                row.Set(values.ContainsKey(row.Key), values.TryGetValue(row.Key, out var value) ? value : null);
            }
        }
        finally
        {
            _suspend = false;
        }

        OnConfigEdited();
    }

    // ---- versions ----

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateVersionAsync()
    {
        if (_config is not { } config || _plan is not { } plan) return;

        ErrorMessage = null;
        ResultMessage = null;
        IsBusy = true;
        CanCreate = false;
        var name = VersionName;
        try
        {
            var created = await Task.Run(() => _service.CreateVersion(name, config, plan));
            await _projects.RefreshAsync();
            await LoadAsync();
            ResultMessage = $"Đã tạo version \"{created.Name}\": {plan.TotalImages} ảnh, xuất ra {plan.ExpectedTotalOutput} ảnh. Ảnh tăng cường được sinh khi xuất dataset, từ ảnh master và config này.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ProjectException)
        {
            ErrorMessage = exception.Message;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorMessage = $"Không tạo được version: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            CanCreate = _plan is { TotalImages: > 0 } && ConfigError is null;
        }
    }

    partial void OnCanCreateChanged(bool value) => CreateVersionCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task DeleteVersionAsync(VersionRow? row)
    {
        if (row is null) return;
        if (!_prompt.Confirm(
                "Xóa version",
                $"Xóa version \"{row.Name}\"?\nẢnh, nhãn và các version khác không bị ảnh hưởng.",
                "Xóa version", danger: true))
            return;

        ErrorMessage = null;
        ResultMessage = null;
        try
        {
            await Task.Run(() => _service.DeleteVersion(row.Id));
            await _projects.RefreshAsync();
            await LoadAsync();
            ResultMessage = $"Đã xóa version \"{row.Name}\".";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorMessage = $"Không xóa được version: {exception.Message}";
        }
    }
}


