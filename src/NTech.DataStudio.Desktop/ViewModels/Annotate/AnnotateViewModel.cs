using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Desktop.Controls;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.Shell;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Desktop.ViewModels.Annotate;

public enum StatusFilterKind
{
    All,
    New,
    Labeled,
    NeedsReview,
    Reviewed,
    Null
}

/// <summary>
/// The annotation screen. It holds a database connection only while it is on
/// screen (<see cref="IActivatable"/>), so Prepare and the project folder are
/// never blocked by it. Every change is saved as it is made.
/// </summary>
public sealed partial class AnnotateViewModel : ObservableObject, IActivatable
{
    private readonly ProjectService _projects;
    private readonly SettingsService _settings;
    private readonly IUserPrompt _prompt;
    private readonly ImageLoader _loader;
    private readonly AnnotationClipboard _clipboard = new();

    private IAnnotationStore? _store;
    private AnnotateService? _service;
    private ClassService? _classService;
    private List<ImageRowViewModel> _allImages = new();
    private ImageRowViewModel? _current;
    private long? _lastImageId;
    private int _generation;
    private int _loadVersion;
    private bool _syncing;
    private bool _active;

    /// <summary>The class and label panel on the right is open. Closed, only its tab stays on the edge.</summary>
    [ObservableProperty]
    private bool _isPanelOpen = true;

    /// <summary>The thumbnail strip at the bottom is open.</summary>
    [ObservableProperty]
    private bool _isStripOpen = true;

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [RelayCommand]
    private void ToggleStrip() => IsStripOpen = !IsStripOpen;

    [ObservableProperty]
    private AnnotationEditor? _editor;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _imageError;

    [ObservableProperty]
    private bool _isImageLoading;

    [ObservableProperty]
    private BitmapSource? _currentBitmap;

    [ObservableProperty]
    private ImageRowViewModel? _selectedImage;

    [ObservableProperty]
    private ClassItemViewModel? _activeClass;


    [ObservableProperty]
    private string _classNameInput = string.Empty;

    [ObservableProperty]
    private ClassItemViewModel? _mergeTarget;

    [ObservableProperty]
    private string? _classError;

    [ObservableProperty]
    private double _labelOpacity = 1;

    [ObservableProperty]
    private Option<StatusFilterKind> _statusFilter;

    [ObservableProperty]
    private Option<long?> _classFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoLabelAllCommand))]
    private bool _hasImages;

    /// <summary>On: a dataset takes only the images ticked in the strip. Off: every labeled image.</summary>
    [ObservableProperty]
    private bool _selectionOnly;

    [ObservableProperty]
    private string _selectionText = string.Empty;

    [ObservableProperty]
    private IReadOnlyDictionary<long, ClassStyle>? _classStyles;

    [ObservableProperty]
    private string _imageInfoText = string.Empty;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private string _imageIndexText = string.Empty;

    [ObservableProperty]
    private string _nullButtonText = "Đánh dấu Null";

    [ObservableProperty]
    private string _reviewButtonText = "Duyệt ảnh";

    public AnnotateViewModel(ProjectService projects, IUserPrompt prompt, ImageLoader loader, SettingsService settings, ISmartSegmenter segmenter, SamModelService models, AutoLabelService autoLabel)
    {
        _autoLabel = autoLabel;
        models.Changed += (_, _) => RaiseTargetTaskChanged();
        _settings = settings;
        Magic = new MagicPolygonController(segmenter);
        Magic.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(MagicStatus));
            OnPropertyChanged(nameof(HasMagicStatus));
        };
        Magic.SetBoxMode(settings.MagicBoxMode);
        Hints = new ShortcutHints(settings);
        _settings.Changed += (_, _) => RaiseTargetTaskChanged();
        _projects = projects;
        _prompt = prompt;
        _loader = loader;
        _statusFilter = StatusFilters[0];
        _classFilter = AllClassesOption;
        ClassFilters.Add(AllClassesOption);
        _projects.CurrentChanged += OnProjectChanged;
    }

    /// <summary>The canvas listens to these; the view model only says what to show.</summary>
    public event Action? FitRequested;
    public event Action? ZoomInRequested;
    public event Action? ZoomOutRequested;
    public event Action<PixelRect>? ZoomToRequested;

    private static readonly Option<long?> AllClassesOption = new("Mọi class", null);

    public static IReadOnlyList<Option<StatusFilterKind>> StatusFilters { get; } = new[]
    {
        new Option<StatusFilterKind>("Mọi trạng thái", StatusFilterKind.All),
        new Option<StatusFilterKind>("Chưa gán", StatusFilterKind.New),
        new Option<StatusFilterKind>("Đã gán", StatusFilterKind.Labeled),
        new Option<StatusFilterKind>("Cần review", StatusFilterKind.NeedsReview),
        new Option<StatusFilterKind>("Đã duyệt", StatusFilterKind.Reviewed),
        new Option<StatusFilterKind>("Null", StatusFilterKind.Null)
    };

    public static IReadOnlyList<string> PaletteColors => ClassPalette.Colors;

    public ObservableCollection<ImageRowViewModel> Images { get; } = new();
    public ObservableCollection<ClassItemViewModel> Classes { get; } = new();
    public ObservableCollection<ClassItemViewModel> MergeTargets { get; } = new();
    public ObservableCollection<Option<long?>> ClassFilters { get; } = new();
    public ObservableCollection<LabelRowViewModel> Labels { get; } = new();

    public bool HasProject => _projects.Current is not null;
    public bool ShowEditor => HasProject && HasImages;
    public bool ShowNoImages => HasProject && !HasImages && !IsLoading;
    public bool HasClasses => Classes.Count > 0;
    public bool IsDrawing => Editor?.Drawing is not null;
    public string? EditorMessage => Editor?.Message;

    // ---- tools (the tool rail binds to these) ----

    public bool IsSelectTool { get => Tool == AnnotateTool.Select; set => ChooseTool(value, AnnotateTool.Select); }
    public bool IsBoxTool { get => Tool == AnnotateTool.Box; set => ChooseTool(value, AnnotateTool.Box); }
    /// <summary>The Magic polygon tool's state: the outline under the pointer, the points added, what it is doing.</summary>
    public MagicPolygonController Magic { get; }

    /// <summary>"Đang phân tích ảnh...", an error, or the reason the model is missing; shown over the picture while the tool is on.</summary>
    public string? MagicStatus => IsMagicTool ? Magic.Status : null;

    public bool HasMagicStatus => MagicStatus is not null;

    /// <summary>Magic polygon waits for a dragged box instead of following the pointer; kept between runs.</summary>
    public bool MagicBoxMode
    {
        get => Magic.BoxMode;
        set
        {
            if (Magic.BoxMode == value) return;

            Magic.SetBoxMode(value);
            _settings.SetMagicBoxMode(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MagicBoxModeTip));
        }
    }

    public string MagicBoxModeTip => MagicBoxMode
        ? "Bật: kéo một khung quanh vật thể rồi mới tách; phóng to thì phân tích riêng phần đang xem."
        : "Tắt: rê chuột để xem trước, bấm để lấy vật thể (phân tích cả ảnh).";

    public bool IsMagicTool { get => Tool == AnnotateTool.MagicPolygon; set => ChooseTool(value, AnnotateTool.MagicPolygon); }
    public bool IsMagicToolEnabled => TaskTools.Allows(TargetTask, AnnotateTool.MagicPolygon) && Magic.Unavailable is null;
    public string MagicToolTip =>
        TaskTools.WhyOff(TargetTask, AnnotateTool.MagicPolygon) is { } why ? $"{Hints.Text(ShortcutAction.MagicPolygonTool)} - {why}"
        : Magic.Unavailable is { } missing ? $"Magic polygon - {missing} Mở Cài đặt để tải model."
        : Hints.Text(ShortcutAction.MagicPolygonTool);

    public bool IsPolygonTool { get => Tool == AnnotateTool.Polygon; set => ChooseTool(value, AnnotateTool.Polygon); }
    public bool IsObbTool { get => Tool == AnnotateTool.RotatedBox; set => ChooseTool(value, AnnotateTool.RotatedBox); }
    public bool IsTagTool { get => Tool == AnnotateTool.Tag; set => ChooseTool(value, AnnotateTool.Tag); }
    public bool IsPanTool { get => Tool == AnnotateTool.Pan; set => ChooseTool(value, AnnotateTool.Pan); }

    private TaskKind? TargetTask => _projects.Current?.TargetTask;

    public bool IsBoxToolEnabled => TaskTools.Allows(TargetTask, AnnotateTool.Box);
    public bool IsPolygonToolEnabled => TaskTools.Allows(TargetTask, AnnotateTool.Polygon);
    public bool IsObbToolEnabled => TaskTools.Allows(TargetTask, AnnotateTool.RotatedBox);
    public bool IsTagToolEnabled => TaskTools.Allows(TargetTask, AnnotateTool.Tag);

    /// <summary>Tooltips that name the keys an action has now (they change when the user rebinds).</summary>
    public ShortcutHints Hints { get; }

    public string BoxToolTip => ToolTip(ShortcutAction.BoxTool, AnnotateTool.Box);
    public string PolygonToolTip => ToolTip(ShortcutAction.PolygonTool, AnnotateTool.Polygon);
    public string ObbToolTip => ToolTip(ShortcutAction.RotatedBoxTool, AnnotateTool.RotatedBox);
    public string TagToolTip => ToolTip(ShortcutAction.TagTool, AnnotateTool.Tag);

    private string ToolTip(ShortcutAction action, AnnotateTool tool) =>
        TaskTools.WhyOff(TargetTask, tool) is { } why ? $"{Hints.Text(action)} - {why}" : Hints.Text(action);

    private void RaiseTargetTaskChanged()
    {
        if (Editor is { } editor) editor.TargetTask = TargetTask;
        foreach (var name in new[]
                 {
                     nameof(IsBoxToolEnabled), nameof(IsMagicToolEnabled), nameof(MagicToolTip), nameof(IsPolygonToolEnabled), nameof(IsObbToolEnabled), nameof(IsTagToolEnabled),
                     nameof(BoxToolTip), nameof(PolygonToolTip), nameof(ObbToolTip), nameof(TagToolTip)
                 })
            OnPropertyChanged(name);
    }

    private AnnotateTool Tool => Editor?.Tool ?? AnnotateTool.Select;

    private void ChooseTool(bool selected, AnnotateTool tool)
    {
        // A radio group unchecks the old button by itself; only the newly checked one matters.
        if (selected && Editor is { } editor && editor.Tool != tool)
            editor.SetTool(tool);
    }

    private void RaiseToolChanged()
    {
        OnPropertyChanged(nameof(IsSelectTool));
        OnPropertyChanged(nameof(IsBoxTool));
        OnPropertyChanged(nameof(IsPolygonTool));
        OnPropertyChanged(nameof(IsMagicTool));
        OnPropertyChanged(nameof(MagicStatus));
        OnPropertyChanged(nameof(HasMagicStatus));
        OnPropertyChanged(nameof(IsObbTool));
        OnPropertyChanged(nameof(IsTagTool));
        OnPropertyChanged(nameof(IsPanTool));
    }

    // ---- activation ----

    public async Task ActivateAsync()
    {
        OnPropertyChanged(nameof(HasProject));
        if (_active || !HasProject) return;

        var generation = ++_generation;
        _active = true;
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var (store, images) = await Task.Run(() =>
            {
                var opened = _projects.OpenAnnotationStore();
                try
                {
                    var service = new AnnotateService(opened);
                    return (opened, service.LoadImages());
                }
                catch
                {
                    opened.Dispose();
                    throw;
                }
            });

            if (generation != _generation)
            {
                store.Dispose();
                return;
            }

            _store = store;
            _service = new AnnotateService(store);
            _classService = new ClassService(store);
            _allImages = images.Select(MakeRow).ToList();
            HasImages = _allImages.Count > 0;
            LoadSelectionOnly();

            var editor = new AnnotationEditor(_service, _clipboard) { TargetTask = TargetTask, Magic = Magic };
            editor.Changed += OnEditorChanged;
            editor.Edited += OnEdited;
            Editor = editor;

            ReloadClasses();
            ShowImageList();
            var start = _allImages.FirstOrDefault(row => row.Id == _lastImageId) ?? _allImages.FirstOrDefault();
            if (start is not null)
                await OpenImageAsync(start);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (generation == _generation)
            {
                ErrorMessage = $"Không mở được dữ liệu gán nhãn: {exception.Message}";
                _active = false;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                NotifyVisibility();
            }
        }
    }

    public void Deactivate()
    {
        if (!_active) return;

        UpdateProjectProgress();
        _generation++;
        _loadVersion++;
        _active = false;
        _batchCts?.Cancel();

        if (Editor is { } editor)
        {
            editor.Cancel();
            editor.Changed -= OnEditorChanged;
            editor.Edited -= OnEdited;
        }

        Editor = null;
        _service = null;
        _classService = null;
        _store?.Dispose();
        _store = null;
        _current = null;
        _loader.Clear();
        CurrentBitmap = null;
        IsImageLoading = false;
        IsLoading = false;
        NotifyVisibility();
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasProject));
        RaiseTargetTaskChanged();
        NotifyVisibility();
        if (!HasProject) Deactivate();
    }

    private void NotifyVisibility()
    {
        OnPropertyChanged(nameof(ShowEditor));
        OnPropertyChanged(nameof(ShowNoImages));
    }

    // ---- images ----

    partial void OnSelectedImageChanged(ImageRowViewModel? value)
    {
        if (_syncing || value is null || ReferenceEquals(value, _current)) return;
        _ = OpenImageAsync(value);
    }

    private async Task OpenImageAsync(ImageRowViewModel row)
    {
        var service = _service;
        var editor = Editor;
        if (service is null || editor is null) return;

        var version = ++_loadVersion;
        var generation = _generation;
        editor.Cancel();
        _current = row;
        AutoLabelMessage = null;
        AutoLabelCommand.NotifyCanExecuteChanged();
        _lastImageId = row.Id;
        ShowImageList();
        ImageError = null;
        IsImageLoading = true;

        try
        {
            var bitmap = await _loader.LoadAsync(row.MasterFullPath);
            if (version != _loadVersion || generation != _generation) return;

            var document = service.Open(row.Image);
            CurrentBitmap = bitmap;
            editor.Open(document);
            Magic.SetImage(row.MasterFullPath);
            RebuildLabels();
            RefreshTags();
            UpdateImageInfo();
            RaiseHistoryChanged();
            PreloadNeighbor();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (version != _loadVersion || generation != _generation) return;

            CurrentBitmap = null;
            editor.Open(null);
            Magic.SetImage(null);
            Labels.Clear();
            ImageError = $"Không mở được ảnh {row.FileName}: {exception.Message}";
            UpdateImageInfo();
        }
        finally
        {
            if (version == _loadVersion) IsImageLoading = false;
        }
    }

    private IEnumerable<ImageRowViewModel> Matching() =>
        _allImages.Where(row => Matches(row, StatusFilter.Value, ClassFilter.Value));

    private static bool Matches(ImageRowViewModel row, StatusFilterKind status, long? classId) =>
        (status switch
        {
            StatusFilterKind.New => row.Status == ImageStatus.New,
            StatusFilterKind.Labeled => row.Status is ImageStatus.Labeled or ImageStatus.Reviewed,
            StatusFilterKind.NeedsReview => row.Status == ImageStatus.Labeled,
            StatusFilterKind.Reviewed => row.Status == ImageStatus.Reviewed,
            StatusFilterKind.Null => row.Status == ImageStatus.Null,
            _ => true
        })
        && (classId is not { } id || row.Image.ClassIds.Contains(id));

    /// <summary>The strip shows the images that match the filters, plus the one being edited so it never vanishes under the user.</summary>
    private void ShowImageList()
    {
        var wanted = _allImages.Where(row => ReferenceEquals(row, _current) || Matches(row, StatusFilter.Value, ClassFilter.Value)).ToList();

        // Replacing the list resets the strip's scroll position, so leave it alone when nothing changed.
        if (!wanted.SequenceEqual(Images))
        {
            _syncing = true;
            Images.Clear();
            foreach (var row in wanted) Images.Add(row);
            _syncing = false;
        }

        _syncing = true;
        SelectedImage = _current;
        _syncing = false;

        var index = _current is null ? -1 : Images.IndexOf(_current);
        ImageIndexText = index >= 0 ? $"{index + 1} / {Images.Count}" : $"- / {Images.Count}";
    }

    partial void OnStatusFilterChanged(Option<StatusFilterKind> value) => ShowImageList();

    partial void OnClassFilterChanged(Option<long?> value) => ShowImageList();

    private async Task MoveAsync(int direction)
    {
        if (_current is null) return;

        var candidates = Matching().ToList();
        var target = direction > 0
            ? candidates.FirstOrDefault(row => row.Id > _current.Id)
            : candidates.LastOrDefault(row => row.Id < _current.Id);
        if (target is not null)
            await OpenImageAsync(target);
    }

    private void PreloadNeighbor()
    {
        if (_current is null) return;

        var next = Matching().FirstOrDefault(row => row.Id > _current.Id);
        if (next is not null) _loader.Preload(next.MasterFullPath);
    }

    private void UpdateImageInfo()
    {
        if (_current is null || Editor?.Document is not { } document)
        {
            ImageInfoText = _current?.FileName ?? string.Empty;
            return;
        }

        var status = document.Status switch
        {
            ImageStatus.Labeled => "Đã gán",
            ImageStatus.Reviewed => "Đã duyệt",
            ImageStatus.Null => "Null",
            _ => "Chưa gán"
        };
        ImageInfoText = $"{_current.FileName} · {document.Width} × {document.Height} · {status} · {document.LabelCount} nhãn";
        NullButtonText = document.Status == ImageStatus.Null ? "Bỏ Null" : "Đánh dấu Null";
        ReviewButtonText = document.Status == ImageStatus.Reviewed ? "Bỏ duyệt" : "Duyệt ảnh";
    }

    // ---- editor events ----

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDrawing));
        OnPropertyChanged(nameof(EditorMessage));
        RaiseToolChanged();
        RefreshTags();
        SyncSelectionFromEditor();
    }

    private void OnEdited(object? sender, AnnotationEdit edit)
    {
        if (_current is null || Editor?.Document is not { } document) return;

        _current.Update(document);
        RebuildLabels();
        RefreshTags();
        RefreshCounts();
        UpdateImageInfo();
        UpdateProjectProgress();
        RaiseHistoryChanged();
    }

    private void RaiseHistoryChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void UpdateProjectProgress() =>
        _projects.UpdateCounts(
            _allImages.Count,
            _allImages.Count(row => row.Status is ImageStatus.Labeled or ImageStatus.Reviewed or ImageStatus.Null));

    // ---- labels ----

    private void RebuildLabels()
    {
        _syncing = true;
        Labels.Clear();
        if (Editor is { Document: { } document } editor)
        {
            foreach (var annotation in document.Annotations)
            {
                var cls = Classes.FirstOrDefault(item => item.Id == annotation.ClassId);
                Labels.Add(new LabelRowViewModel(
                    annotation,
                    cls?.Name ?? "?",
                    cls?.ColorHex ?? "#808080",
                    editor.Hidden.Contains(annotation.Id),
                    editor.Locked.Contains(annotation.Id),
                    row => Editor?.ToggleHidden(row.Id),
                    row => Editor?.ToggleLocked(row.Id),
                    OnLabelRowSelected));
            }
        }

        _syncing = false;
        SyncSelectionFromEditor();
    }

    private void SyncSelectionFromEditor()
    {
        if (Editor is not { } editor) return;

        _syncing = true;
        foreach (var row in Labels) row.SetSelectedWithoutNotifying(editor.IsSelected(row.Id));
        _syncing = false;
        OnPropertyChanged(nameof(HasLabelSelection));
        OnPropertyChanged(nameof(DeleteLabelsText));
    }

    /// <summary>The list changed what is selected (a click, Ctrl or Shift): the editor follows.</summary>
    private void OnLabelRowSelected(LabelRowViewModel row)
    {
        if (_syncing || Editor is null) return;

        var chosen = Labels.Where(item => item.IsSelected).ToList();
        Editor.SelectMany(chosen.Select(item => item.Id));
        if (chosen.Count == 1)
            ZoomToRequested?.Invoke(PolygonMath.BoundingBox(chosen[0].Annotation.Points));
    }

    public bool HasLabelSelection => Editor is { } editor && editor.SelectionCount > 0;

    public string DeleteLabelsText => Editor is { SelectionCount: > 1 } editor
        ? $"Xóa {editor.SelectionCount} nhãn đã chọn"
        : "Xóa nhãn đã chọn";

    [RelayCommand]
    private void SelectAllLabels() => Editor?.SelectAll();

    // ---- classes ----

    partial void OnActiveClassChanged(ClassItemViewModel? value)
    {
        if (Editor is { } editor) editor.ActiveClassId = value?.Id;
        ClassNameInput = value?.Name ?? string.Empty;
        ClassError = null;
        MergeTargets.Clear();
        foreach (var other in Classes.Where(item => item.Id != value?.Id))
            MergeTargets.Add(other);
        MergeTarget = MergeTargets.FirstOrDefault();
    }

    private void ReloadClasses()
    {
        if (_classService is null) return;

        var classes = _classService.Load();
        var counts = _classService.LoadCounts();
        var previousActive = ActiveClass?.Id;
        var previousFilter = ClassFilter.Value;

        Classes.Clear();
        foreach (var cls in classes)
            Classes.Add(new ClassItemViewModel(cls, counts.GetValueOrDefault(cls.Id), OnTagToggled));

        ClassStyles = classes.ToDictionary(
            cls => cls.Id,
            cls => new ClassStyle((Color)ColorConverter.ConvertFromString(cls.Color), cls.Name));

        ClassFilters.Clear();
        ClassFilters.Add(AllClassesOption);
        foreach (var item in Classes) ClassFilters.Add(new Option<long?>(item.Name, item.Id));
        ClassFilter = ClassFilters.FirstOrDefault(option => option.Value == previousFilter) ?? AllClassesOption;

        ActiveClass = Classes.FirstOrDefault(item => item.Id == previousActive) ?? Classes.FirstOrDefault();
        OnPropertyChanged(nameof(HasClasses));
        RefreshTags();
    }

    private void RefreshCounts()
    {
        if (_classService is null) return;

        var counts = _classService.LoadCounts();
        foreach (var item in Classes)
            item.Count = counts.GetValueOrDefault(item.Id);
    }

    private void RefreshTags()
    {
        var tags = Editor?.Document?.Tags;
        foreach (var item in Classes)
            item.SetTaggedWithoutNotifying(tags?.Contains(item.Id) == true);

        TagsText = tags is { Count: > 0 }
            ? "Tag: " + string.Join(", ", Classes.Where(item => tags.Contains(item.Id)).Select(item => item.Name))
            : string.Empty;
    }

    private void OnTagToggled(ClassItemViewModel item, bool _) => Editor?.ToggleTag(item.Id);

    /// <summary>A class operation either works or leaves a message in the panel; it never throws at the user.</summary>
    private bool Guard(Action action)
    {
        ClassError = null;
        try
        {
            action();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ClassError = exception.Message;
            return false;
        }
    }

    [RelayCommand]
    private void AddClass()
    {
        var name = ClassNameInput;
        Guard(() => ActiveClassAfter(_classService!.Add(name).Id));
    }

    private void ActiveClassAfter(long classId)
    {
        ReloadClasses();
        ActiveClass = Classes.FirstOrDefault(item => item.Id == classId);
        _ = RefreshAfterClassChangeAsync();
    }

    [RelayCommand]
    private void RenameClass()
    {
        if (ActiveClass is not { } active) return;

        var id = active.Id;
        var name = ClassNameInput;
        Guard(() =>
        {
            _classService!.Rename(id, name);
            ActiveClassAfter(id);
        });
    }

    [RelayCommand]
    private void RecolorClass(string? color)
    {
        if (ActiveClass is not { } active) return;

        var id = active.Id;
        Guard(() =>
        {
            _classService!.Recolor(id, color);
            ActiveClassAfter(id);
        });
    }

    [RelayCommand]
    private void MergeClass()
    {
        if (ActiveClass is not { } from || MergeTarget is not { } into) return;

        var usage = _classService!.Usage(from.Id);
        if (usage.AnnotationCount > 0 && !_prompt.Confirm(
                "Gộp class",
                $"Gộp \"{from.Name}\" vào \"{into.Name}\"? {usage.AnnotationCount} nhãn trên {usage.ImageCount} ảnh sẽ chuyển thành \"{into.Name}\". Việc này không hoàn tác được.",
                "Gộp class", danger: true))
            return;

        var fromId = from.Id;
        var intoId = into.Id;
        if (Guard(() => _classService!.Merge(fromId, intoId)))
        {
            ActiveClassAfter(intoId);
            AfterClassDataChanged();
        }
    }

    [RelayCommand]
    private void DeleteClass()
    {
        if (ActiveClass is not { } active) return;

        var usage = _classService!.Usage(active.Id);
        if (usage.AnnotationCount > 0 && !_prompt.Confirm(
                "Xóa class",
                $"Xóa class \"{active.Name}\"? {usage.AnnotationCount} nhãn trên {usage.ImageCount} ảnh cũng bị xóa. Việc này không hoàn tác được.",
                "Xóa class", danger: true))
            return;

        var id = active.Id;
        if (Guard(() => _classService!.Delete(id)))
        {
            ReloadClasses();
            AfterClassDataChanged();
        }
    }

    /// <summary>Labels moved or disappeared under every image, so cached rows, history and the clipboard are stale.</summary>
    private void AfterClassDataChanged()
    {
        _service?.ClearHistory();
        _clipboard.Clear();
        _ = RefreshAfterClassChangeAsync(reloadImages: true);
    }

    private async Task RefreshAfterClassChangeAsync(bool reloadImages = false)
    {
        RebuildLabels();
        RefreshCounts();
        if (!reloadImages || _service is null || _store is null) return;

        var images = _service.LoadImages();
        _allImages = images.Select(MakeRow).ToList();
        var same = _allImages.FirstOrDefault(row => row.Id == _lastImageId) ?? _allImages.FirstOrDefault();
        UpdateProjectProgress();
        if (same is not null)
            await OpenImageAsync(same);
    }

    // ---- automatic labeling (decision 0017) ----

    private readonly AutoLabelService _autoLabel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AutoLabelCommand))]
    [NotifyCanExecuteChangedFor(nameof(AutoLabelAllCommand))]
    private bool _isAutoLabeling;

    [ObservableProperty]
    private bool _isBatchRunning;

    private CancellationTokenSource? _batchCts;

    [ObservableProperty]
    private string? _autoLabelMessage;

    private bool CanAutoLabel() => !IsAutoLabeling && _current is not null;

    private bool CanAutoLabelAll() => !IsAutoLabeling && HasImages;

    [RelayCommand]
    private void CancelAutoLabel() => _batchCts?.Cancel();

    private static readonly PromptButton[] ExistingLabelChoices =
    {
        new("cancel", "Hủy lệnh", IsCancel: true),
        new("add", "Ghi đè", PromptButtonRole.Primary, IsDefault: true),
        new("replace", "Xóa hết, gán mới", PromptButtonRole.Danger)
    };

    /// <summary>
    /// Labels every image that has no labels yet. Images that already have labels but are not ticked for the dataset
    /// raise the same three-way question as a single image (asked once for all of them); ticked images are left alone.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAutoLabelAll))]
    private async Task AutoLabelAllAsync()
    {
        if (_store is null || _service is null) return;

        var classes = _store.LoadClasses();
        if (_autoLabel.Check(classes) is { } problem)
        {
            AutoLabelMessage = problem;
            return;
        }

        var fresh = _allImages.Where(row => row.Status == ImageStatus.New).ToList();
        var labeled = _allImages.Where(row => row.Status is ImageStatus.Labeled or ImageStatus.Reviewed && !row.InDataset).ToList();
        var replaceExisting = false;
        var targets = new List<ImageRowViewModel>(fresh);
        if (labeled.Count > 0)
        {
            var choice = _prompt.Choose(
                "Gán tự động hàng loạt",
                $"{fresh.Count} ảnh chưa gán nhãn sẽ được gán. Ngoài ra có {labeled.Count} ảnh đã có nhãn nhưng chưa được tích chọn vào dataset (ảnh đã tích chọn được giữ nguyên).\n\n"
                + "Ghi đè: giữ nhãn hiện tại của các ảnh đó và thêm nhãn của model lên trên.\n"
                + "Xóa hết, gán mới: xóa nhãn hiện tại của các ảnh đó rồi gán lại bằng model.\n"
                + "Hủy lệnh: không gán gì cả.",
                ExistingLabelChoices,
                PromptSeverity.Question);
            if (choice is not ("add" or "replace")) return;
            replaceExisting = choice == "replace";
            targets.AddRange(labeled);
        }

        if (targets.Count == 0)
        {
            AutoLabelMessage = "Không có ảnh nào cần gán tự động.";
            return;
        }

        var minScore = _settings.AutoLabelMinScore;
        IsAutoLabeling = true;
        IsBatchRunning = true;
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        var generation = _loadVersion;
        int processed = 0, labelCount = 0, imagesLabeled = 0, nothing = 0, failed = 0;
        string? firstError = null;
        var skipped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var cancelled = false;
        try
        {
            foreach (var row in targets)
            {
                token.ThrowIfCancellationRequested();
                AutoLabelMessage = $"Gán tự động {processed + 1} / {targets.Count}: {row.FileName}";
                try
                {
                    var result = await _autoLabel.LabelAsync(row.MasterFullPath, row.Id, classes, minScore, token);
                    if (generation != _loadVersion || _service is null) return;

                    foreach (var name in result.SkippedClasses) skipped.Add(name);
                    if (result.Annotations.Count == 0)
                    {
                        nothing++;
                    }
                    else
                    {
                        ApplyBatchLabels(row, result.Annotations, replaceExisting && row.Status != ImageStatus.New);
                        labelCount += result.Annotations.Count;
                        imagesLabeled++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failed++;
                    firstError ??= $"{row.FileName}: {exception.Message}";
                }

                processed++;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            IsBatchRunning = false;
            IsAutoLabeling = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }

        if (generation != _loadVersion) return;

        RefreshCounts();
        UpdateProjectProgress();
        ShowImageList();
        var parts = new List<string>
        {
            $"{(cancelled ? "Đã dừng sau" : "Xong:")} {imagesLabeled} ảnh được gán ({labelCount} nhãn, từ {minScore:P0}). Hãy duyệt từng ảnh."
        };
        if (nothing > 0) parts.Add($"{nothing} ảnh model không thấy vật thể nào.");
        if (failed > 0) parts.Add($"{failed} ảnh lỗi (đầu tiên: {firstError}).");
        if (skipped.Count > 0) parts.Add($"Bỏ qua class không có trong project: {string.Join(", ", skipped)}.");
        AutoLabelMessage = string.Join(" ", parts);
    }

    /// <summary>The image on screen goes through its editor; any other through its own document, as one edit either way.</summary>
    private void ApplyBatchLabels(ImageRowViewModel row, IReadOnlyList<NTech.DataStudio.Domain.Annotations.Annotation> labels, bool replace)
    {
        if (ReferenceEquals(row, _current) && Editor is { Document: { } shown } editor && shown.ImageId == row.Id)
        {
            editor.AddAutoLabels(labels, replace);
            return;
        }

        var document = _service!.Open(row.Image);
        var before = replace ? document.Annotations.ToList() : new List<NTech.DataStudio.Domain.Annotations.Annotation>();
        _service.Execute(document, new EditRequest(before, labels, Array.Empty<long>(), Array.Empty<long>()));
        row.Update(document);
    }

    [RelayCommand(CanExecute = nameof(CanAutoLabel))]
    private async Task AutoLabelAsync()
    {
        if (_current is not { } row || _store is null || Editor is not { Document: { } document } editor) return;

        var classes = _store.LoadClasses();
        if (_autoLabel.Check(classes) is { } problem)
        {
            AutoLabelMessage = problem;
            return;
        }

        var replaceExisting = false;
        if (document.LabelCount > 0)
        {
            var choice = _prompt.Choose(
                "Gán nhãn tự động",
                $"Ảnh này đã có {document.LabelCount} nhãn.\n\n"
                + "Xóa hết, gán mới: xóa các nhãn hiện tại (trừ nhãn đang khóa) rồi gán lại bằng model.\n"
                + "Ghi đè: giữ nhãn hiện tại và thêm nhãn của model lên trên.",
                new[]
                {
                    new PromptButton("cancel", "Hủy lệnh", IsCancel: true),
                    new PromptButton("add", "Ghi đè", PromptButtonRole.Primary, IsDefault: true),
                    new PromptButton("replace", "Xóa hết, gán mới", PromptButtonRole.Danger)
                },
                PromptSeverity.Question);
            if (choice is not ("add" or "replace")) return;
            replaceExisting = choice == "replace";
        }

        IsAutoLabeling = true;
        AutoLabelMessage = "Đang gán nhãn tự động...";
        try
        {
            var minScore = _settings.AutoLabelMinScore;
            var result = await _autoLabel.LabelAsync(row.MasterFullPath, row.Id, classes, minScore);
            if (!ReferenceEquals(row, _current) || !ReferenceEquals(editor, Editor) || editor.Document?.ImageId != row.Id)
            {
                AutoLabelMessage = null;
                return;
            }

            var skipped = result.SkippedClasses.Count > 0
                ? $" Bỏ qua class không có trong project: {string.Join(", ", result.SkippedClasses)}."
                : string.Empty;
            if (result.Annotations.Count == 0)
            {
                AutoLabelMessage = $"Model không thấy vật thể nào từ {minScore:P0} trở lên.{skipped}";
                return;
            }

            AutoLabelMessage = editor.AddAutoLabels(result.Annotations, replaceExisting)
                ? $"Đã {(replaceExisting ? "gán mới" : "thêm")} {result.Annotations.Count} nhãn tự động (từ {minScore:P0}). Hãy sửa rồi bấm Duyệt ảnh.{skipped}"
                : "Không lưu được nhãn tự động.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            AutoLabelMessage = $"Gán tự động lỗi: {exception.Message}";
        }
        finally
        {
            IsAutoLabeling = false;
        }
    }

    // ---- dataset selection ----

    private ImageRowViewModel MakeRow(AnnotateImage image) => new(image, _store!.ProjectRoot, OnRowInDatasetChanged);

    private void LoadSelectionOnly()
    {
        _loadingSelection = true;
        try { SelectionOnly = _service?.LoadSelectionOnly() ?? false; }
        finally { _loadingSelection = false; }
        UpdateSelectionText();
    }

    private bool _loadingSelection;

    partial void OnSelectionOnlyChanged(bool value)
    {
        UpdateSelectionText();
        if (_loadingSelection || _service is null) return;
        try
        {
            _service.SetSelectionOnly(value);
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            ErrorMessage = $"Không lưu được lựa chọn: {exception.Message}";
            _loadingSelection = true;
            SelectionOnly = !value;
            _loadingSelection = false;
            UpdateSelectionText();
        }
    }

    private void OnRowInDatasetChanged(ImageRowViewModel row)
    {
        if (_service is null) return;
        try
        {
            _service.SetInDataset(row.Id, row.InDataset);
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            ErrorMessage = $"Không lưu được lựa chọn ảnh: {exception.Message}";
            row.SetInDatasetWithoutNotifying(!row.InDataset);
        }

        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        SelectionText = SelectionOnly
            ? $"{_allImages.Count(row => row.InDataset)} / {_allImages.Count} ảnh được chọn"
            : string.Empty;
    }

    // ---- commands ----

    [RelayCommand]
    private void Finish() => Editor?.Finish();

    [RelayCommand]
    private void UndoPoint() => Editor?.UndoPoint();

    [RelayCommand]
    private void CancelDrawing() => Editor?.Cancel();

    private bool CanUndo() => Editor?.Document is { } document && _service?.CanUndo(document.ImageId) == true;

    private bool CanRedo() => Editor?.Document is { } document && _service?.CanRedo(document.ImageId) == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Editor?.Undo();
        RaiseHistoryChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Editor?.Redo();
        RaiseHistoryChanged();
    }

    [RelayCommand]
    private void DeleteSelected() => Editor?.DeleteSelected();

    [RelayCommand]
    private Task Next() => MoveAsync(1);

    [RelayCommand]
    private Task Previous() => MoveAsync(-1);

    /// <summary>For a camera that did not move, the labels of the image before are a good start.</summary>
    [RelayCommand]
    private void CopyFromPrevious()
    {
        if (Editor is not { Document: { } document } editor || _service is null) return;

        var previous = _allImages.LastOrDefault(row => row.Id < document.ImageId);
        if (previous is null) return;

        var content = _service.LoadContent(previous.Id);
        editor.CopyFrom(content, previous.Image.Width, previous.Image.Height);
    }

    [RelayCommand]
    private void ToggleNull() => Editor?.ToggleNull();

    [RelayCommand]
    private void ToggleReviewed() => Editor?.ToggleReviewed();

    [RelayCommand]
    private void ToggleHideAll() => Editor?.ToggleShapesVisible();

    [RelayCommand]
    private void Fit() => FitRequested?.Invoke();

    [RelayCommand]
    private void ZoomIn() => ZoomInRequested?.Invoke();

    [RelayCommand]
    private void ZoomOut() => ZoomOutRequested?.Invoke();

    // ---- keyboard ----

    /// <summary>
    /// Runs the action the pressed key is bound to (Settings, decision 0015). The view calls this unless a text
    /// box has the focus. Keys 1 to 9 pick classes and are not rebindable.
    /// </summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (Editor is null) return false;
        if (modifiers.HasFlag(ModifierKeys.Alt)) return false;

        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl)
        {
            if (key is >= Key.D1 and <= Key.D9) return UseHotkey(key - Key.D1 + 1);
            if (key is >= Key.NumPad1 and <= Key.NumPad9) return UseHotkey(key - Key.NumPad1 + 1);
        }

        return _settings.Shortcuts.Find(new Shortcut(KeyNames.Of(key), ctrl, shift)) is { } action && Run(action, Editor);
    }

    /// <summary>Whether this key press is bound to an action, so the view knows to take it (Space waits for its key-up).</summary>
    public bool IsBound(Key key, ModifierKeys modifiers) =>
        Editor is not null && !modifiers.HasFlag(ModifierKeys.Alt)
        && _settings.Shortcuts.Find(new Shortcut(KeyNames.Of(key), modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift))) is not null;

    private bool Run(ShortcutAction action, AnnotationEditor editor)
    {
        switch (action)
        {
            case ShortcutAction.SelectTool: editor.SetTool(AnnotateTool.Select); return true;
            case ShortcutAction.BoxTool: editor.SetTool(AnnotateTool.Box); return true;
            case ShortcutAction.PolygonTool: editor.SetTool(AnnotateTool.Polygon); return true;
            case ShortcutAction.MagicPolygonTool: editor.SetTool(AnnotateTool.MagicPolygon); return true;
            case ShortcutAction.RotatedBoxTool: editor.SetTool(AnnotateTool.RotatedBox); return true;
            case ShortcutAction.TagTool: editor.SetTool(AnnotateTool.Tag); return true;
            case ShortcutAction.PanTool: editor.SetTool(AnnotateTool.Pan); return true;
            // Q steps to the next tool in the rail and Shift+Q back, so one hand can flip between tools. Not E:
            // a Vietnamese Telex IME turns a repeated "e" into "ê" (a Backspace plus a letter) and eats the press.
            case ShortcutAction.NextTool: editor.CycleTool(1); return true;
            case ShortcutAction.PreviousTool: editor.CycleTool(-1); return true;
            case ShortcutAction.ZoomIn: ZoomInRequested?.Invoke(); return true;
            case ShortcutAction.ZoomOut: ZoomOutRequested?.Invoke(); return true;
            case ShortcutAction.FitView: FitRequested?.Invoke(); return true;
            case ShortcutAction.ToggleShapes: editor.ToggleShapesVisible(); return true;
            case ShortcutAction.PreviousImage: _ = MoveAsync(-1); return true;
            case ShortcutAction.NextImage: _ = MoveAsync(1); return true;
            case ShortcutAction.Finish: editor.Finish(); return true;
            case ShortcutAction.UndoPoint: editor.UndoPoint(); return true;
            case ShortcutAction.Escape: editor.Escape(); return true;
            case ShortcutAction.ToggleNull: editor.ToggleNull(); return true;
            case ShortcutAction.Delete: editor.DeleteSelected(); return true;
            case ShortcutAction.Undo: Undo(); return true;
            case ShortcutAction.Redo: Redo(); return true;
            case ShortcutAction.Copy: editor.Copy(); return true;
            case ShortcutAction.Paste: editor.Paste(); return true;
            default: return false;
        }
    }

/// <summary>Keys 1-9 pick the class. With the tag tool they tag the image; with a shape selected they reclassify it.</summary>
    private bool UseHotkey(int number)
    {
        var target = Classes.FirstOrDefault(item => item.Hotkey == number);
        if (target is null || Editor is null) return false;

        ActiveClass = target;
        if (Editor.Tool == AnnotateTool.Tag)
            Editor.ToggleTag(target.Id);
        else if (Editor.Tool == AnnotateTool.Select)
            Editor.ChangeSelectedClass(target.Id);
        return true;
    }
}
