using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

public sealed partial class NormalizeStepViewModel : PrepareStepViewModel
{
    private const int PreviewEdge = 1024;
    private const int PreviewDelayMilliseconds = 250;

    private readonly NormalizeService _normalize;
    private readonly IImageCodec _codec;
    private readonly IModelProfileCatalog _catalog;
    private readonly IUserPrompt _prompt;
    private IReadOnlyList<SourceImage> _active = Array.Empty<SourceImage>();
    private string _projectRoot = string.Empty;
    private int _medianLongEdge;
    private int _previewIndex;
    private bool _applyingProposal;
    private CancellationTokenSource? _previewCancellation;

    [ObservableProperty]
    private ModelProfile? _selectedProfile;

    [ObservableProperty]
    private string _minObjectPxText = string.Empty;

    [ObservableProperty]
    private string? _recommendationText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownscale))]
    [NotifyPropertyChangedFor(nameof(IsTile))]
    [NotifyPropertyChangedFor(nameof(IsRoi))]
    private Option<GeometryMethod> _selectedMethod;

    [ObservableProperty]
    private int _selectedMaxEdge = 2048;

    [ObservableProperty]
    private int _tileSize = 1280;

    [ObservableProperty]
    private int _tileOverlap = 128;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoiText))]
    [NotifyCanExecuteChangedFor(nameof(ClearRoiCommand))]
    private PixelRect? _roi;

    [ObservableProperty]
    private Option<MasterFormat> _selectedFormat;

    [ObservableProperty]
    private Option<MonoMode> _selectedMono;

    [ObservableProperty]
    private byte[]? _previewBefore;

    [ObservableProperty]
    private byte[]? _previewAfter;

    [ObservableProperty]
    private string? _previewCaption;

    [ObservableProperty]
    private string? _previewNote;

    [ObservableProperty]
    private string? _previewError;

    [ObservableProperty]
    private bool _isPreviewLoading;

    [ObservableProperty]
    private int _previewSourceWidth;

    [ObservableProperty]
    private int _previewSourceHeight;

    [ObservableProperty]
    private bool _hasImages;

    public NormalizeStepViewModel(
        ProjectService projects,
        NormalizeService normalize,
        IImageCodec codec,
        IModelProfileCatalog catalog,
        IUserPrompt prompt)
        : base(projects)
    {
        _normalize = normalize;
        _codec = codec;
        _catalog = catalog;
        _prompt = prompt;
        _selectedMethod = Methods[0];
        _selectedFormat = Formats[0];
        _selectedMono = MonoModes[0];
    }

    public ObservableCollection<ModelProfile> Profiles { get; } = new();

    public ObservableCollection<string> Warnings { get; } = new();

    public IReadOnlyList<Option<GeometryMethod>> Methods { get; } = new[]
    {
        new Option<GeometryMethod>("Downscale: thu nhỏ cạnh dài", GeometryMethod.Downscale),
        new Option<GeometryMethod>("Tile: cắt thành nhiều ảnh", GeometryMethod.Tile),
        new Option<GeometryMethod>("Crop ROI: một vùng cố định", GeometryMethod.RoiCrop)
    };

    public IReadOnlyList<int> MaxEdges { get; } = new[] { 2048, 4096 };

    public IReadOnlyList<Option<MasterFormat>> Formats { get; } = new[]
    {
        new Option<MasterFormat>("JPEG q95 (nhẹ, mặc định)", MasterFormat.Jpeg95),
        new Option<MasterFormat>("PNG (lossless, cho lỗi bề mặt rất mảnh)", MasterFormat.Png)
    };

    public IReadOnlyList<Option<MonoMode>> MonoModes { get; } = new[]
    {
        new Option<MonoMode>("Ảnh mono giữ 1 kênh", MonoMode.KeepOneChannel),
        new Option<MonoMode>("Ảnh mono nhân thành 3 kênh", MonoMode.TripleChannel)
    };

    public bool IsDownscale => SelectedMethod.Value == GeometryMethod.Downscale;
    public bool IsTile => SelectedMethod.Value == GeometryMethod.Tile;
    public bool IsRoi => SelectedMethod.Value == GeometryMethod.RoiCrop;

    public string RoiText => Roi is { } roi
        ? $"{roi.Width:0} × {roi.Height:0} px tại ({roi.X:0}, {roi.Y:0})"
        : "Kéo chuột trên ảnh gốc để chọn vùng.";

public override Task ReloadAsync() => RunAsync("Đang đọc project", (store, _, _) =>
    {
        var sources = store.LoadSources();
        var profiles = Profiles.Count == 0 ? _catalog.LoadAll() : null;
        var root = store.ProjectRoot;
        return () =>
        {
            _projectRoot = root;
            _active = sources.Where(source => !source.IsRejected).ToList();
            HasImages = _active.Count > 0;
            _medianLongEdge = _active.Count == 0
                ? 0
                : _active.Select(source => source.LongEdge).Order().ElementAt(_active.Count / 2);
            _previewIndex = Math.Clamp(_previewIndex, 0, Math.Max(0, _active.Count - 1));

            if (profiles is not null)
            {
                foreach (var profile in profiles)
                    Profiles.Add(profile);
                SelectedProfile = profiles.FirstOrDefault(profile => profile.Id == "yolo26-detect") ?? profiles.FirstOrDefault();
            }

            ApplyProposal();
            RequestPreview();
        };
    });

    /// <summary>The ROI the user dragged on the preview, in source pixels.</summary>
    public void SetRoi(PixelRect roi) => Roi = roi;

    partial void OnSelectedProfileChanged(ModelProfile? value) => ApplyProposal();

    partial void OnMinObjectPxTextChanged(string value) => ApplyProposal();

    partial void OnRoiChanged(PixelRect? value)
    {
        ApplyProposal();
        RequestPreview();
    }

    partial void OnSelectedMethodChanged(Option<GeometryMethod> value) => RequestPreview();

    partial void OnSelectedMaxEdgeChanged(int value) => RequestPreview();

    partial void OnTileSizeChanged(int value) => RequestPreview();

    partial void OnTileOverlapChanged(int value) => RequestPreview();

    partial void OnSelectedFormatChanged(Option<MasterFormat> value) => RequestPreview();

    partial void OnSelectedMonoChanged(Option<MonoMode> value) => RequestPreview();

    private void ApplyProposal()
    {
        if (SelectedProfile is not { } profile || _medianLongEdge == 0) return;

        int? minObject = int.TryParse(MinObjectPxText, out var parsed) && parsed > 0 ? parsed : null;
        var proposal = GeometryPlanner.Propose(_medianLongEdge, minObject, profile, Roi);

        _applyingProposal = true;
        SelectedMethod = Methods.First(method => method.Value == proposal.Plan.Method);
        if (proposal.Plan.MaxLongEdge > 0) SelectedMaxEdge = proposal.Plan.MaxLongEdge;
        if (proposal.Plan.TileSize > 0)
        {
            TileSize = proposal.Plan.TileSize;
            TileOverlap = proposal.Plan.TileOverlap;
        }
        _applyingProposal = false;

        Warnings.Clear();
        foreach (var warning in proposal.Warnings)
            Warnings.Add(warning);
        RecommendationText = $"Đề xuất: {SelectedMethod.Label}. Train với imgsz {proposal.RecommendedImgsz}. Cạnh dài phổ biến của bộ ảnh: {_medianLongEdge} px.";
        RequestPreview();
    }

    private bool TryBuildOptions(out NormalizeOptions options, out string? problem)
    {
        problem = SelectedMethod.Value switch
        {
            GeometryMethod.Tile when TileSize <= 0 => "Kích thước tile phải lớn hơn 0.",
            GeometryMethod.Tile when TileOverlap < 0 || TileOverlap >= TileSize => "Độ chồng lấn phải từ 0 tới nhỏ hơn kích thước tile.",
            GeometryMethod.RoiCrop when Roi is null => "Chưa chọn vùng ROI.",
            _ => null
        };

        var plan = SelectedMethod.Value switch
        {
            GeometryMethod.Tile => new GeometryPlan(GeometryMethod.Tile, TileSize: TileSize, TileOverlap: TileOverlap),
            GeometryMethod.RoiCrop => new GeometryPlan(GeometryMethod.RoiCrop, Roi: Roi),
            _ => new GeometryPlan(GeometryMethod.Downscale, MaxLongEdge: SelectedMaxEdge)
        };
        options = new NormalizeOptions(plan, SelectedFormat.Value, SelectedMono.Value);
        return problem is null;
    }

    [RelayCommand]
    private void PreviousPreview() => MovePreview(-1);

    [RelayCommand]
    private void NextPreview() => MovePreview(1);

    private void MovePreview(int delta)
    {
        if (_active.Count == 0) return;
        _previewIndex = (_previewIndex + delta + _active.Count) % _active.Count;
        RequestPreview();
    }

    private bool HasRoi() => Roi is not null;

    [RelayCommand(CanExecute = nameof(HasRoi))]
    private void ClearRoi() => Roi = null;

    /// <summary>Settings change in bursts while typing, so only the last request renders.</summary>
    private async void RequestPreview()
    {
        if (_applyingProposal) return;

        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        PreviewError = null;

        if (_active.Count == 0)
        {
            PreviewBefore = PreviewAfter = null;
            PreviewCaption = PreviewNote = null;
            return;
        }

        var hasOptions = TryBuildOptions(out var options, out var problem);
        if (!hasOptions && SelectedMethod.Value != GeometryMethod.RoiCrop)
        {
            PreviewError = problem;
            return;
        }

        // While no ROI is drawn yet, still show the whole image to draw on.
        if (!hasOptions)
            options = options with { Geometry = new GeometryPlan(GeometryMethod.Downscale, MaxLongEdge: SelectedMaxEdge) };

        var source = _active[_previewIndex];
        var path = Path.Combine(_projectRoot, source.StoredPath);
        var total = _active.Count;
        var position = _previewIndex + 1;

        try
        {
            IsPreviewLoading = true;
            await Task.Delay(PreviewDelayMilliseconds, cancellation.Token);
            var preview = await Task.Run(() => _codec.RenderPreview(path, options, PreviewEdge), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;

            PreviewBefore = preview.BeforeJpeg;
            PreviewAfter = preview.AfterJpeg;
            PreviewSourceWidth = preview.SourceWidth;
            PreviewSourceHeight = preview.SourceHeight;
            PreviewCaption = $"Ảnh {position} / {total}: {source.FileName}";
            PreviewNote =
                $"Trước: {preview.SourceWidth} × {preview.SourceHeight}, {PrepareText.Bytes(source.FileSize)}.  "
                + $"Sau: {preview.MasterCount} ảnh {preview.MasterWidth} × {preview.MasterHeight}, {PrepareText.Bytes(preview.MasterBytes)}.  "
                + $"Cả bộ {total} ảnh ≈ {PrepareText.Bytes(preview.MasterBytes * total)}.";
        }
        catch (OperationCanceledException)
        {
            // A newer request took over.
        }
        catch (ImageReadException exception)
        {
            PreviewError = $"{source.FileName}: {exception.Message}";
        }
        finally
        {
            if (_previewCancellation == cancellation)
                IsPreviewLoading = false;
        }
    }

    [RelayCommand]
    private async Task RunNormalizeAsync()
    {
        if (!TryBuildOptions(out var options, out var problem))
        {
            ErrorMessage = problem;
            return;
        }

        var change = ConfigChange.None;
        await RunAsync("Đang kiểm tra cấu hình", (store, _, _) =>
        {
            change = NormalizeService.CheckConfigChange(store, options);
            return () => { };
        });

        if (change == ConfigChange.BlockedByAnnotations)
        {
            ErrorMessage = "Project đã có nhãn trên ảnh master hiện tại, nên không đổi được cấu hình chuẩn hóa. Hãy dùng lại cấu hình cũ.";
            return;
        }

        if (change == ConfigChange.MastersWillBeRebuilt
            && !_prompt.Confirm("Cấu hình khác lần trước. Toàn bộ ảnh master sẽ được tạo lại; ảnh gốc không bị ảnh hưởng. Tiếp tục?"))
            return;

        await RunAsync("Đang chuẩn hóa", (store, progress, token) =>
        {
            var result = _normalize.Normalize(store, options, progress, token);
            return () =>
            {
                var ratio = result.MasterBytes > 0 ? (double)result.SourceBytes / result.MasterBytes : 0;
                ResultMessage =
                    $"Đã xử lý {result.Processed} ảnh, bỏ qua {result.AlreadyDone} ảnh đã có. "
                    + $"Tổng {result.MasterCount} ảnh master, {PrepareText.Bytes(result.SourceBytes)} còn {PrepareText.Bytes(result.MasterBytes)} (giảm {ratio:0.#} lần).";
                if (result.Failures.Count > 0)
                    ErrorMessage = PrepareText.Failures(result.Failures);
            };
        });

        await Projects.RefreshAsync();
    }
}
