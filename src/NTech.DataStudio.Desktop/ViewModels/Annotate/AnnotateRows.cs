using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Desktop.ViewModels.Annotate;

public sealed partial class ImageRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAutoDot))]
    private ImageStatus _status;

    /// <summary>Some of the labels were put there by the model (decision 0017).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAutoDot))]
    private bool _hasModelLabels;

    [ObservableProperty]
    private int _labelCount;

    [ObservableProperty]
    private bool _inDataset;

    private readonly Action<ImageRowViewModel>? _inDatasetChanged;
    private bool _silent;

    public ImageRowViewModel(AnnotateImage image, string projectRoot, Action<ImageRowViewModel>? inDatasetChanged = null)
    {
        _inDatasetChanged = inDatasetChanged;
        Image = image;
        _silent = true;
        InDataset = image.InDataset;
        _silent = false;
        _status = image.Status;
        _hasModelLabels = image.HasModelLabels;
        _labelCount = image.LabelCount;
        ThumbnailPath = Path.Combine(projectRoot, image.ThumbPath);
        MasterFullPath = Path.Combine(projectRoot, image.MasterPath);
    }

    /// <summary>The dot turns purple while model labels wait for a review; a reviewed image is green again.</summary>
    public bool ShowAutoDot => HasModelLabels && Status != ImageStatus.Reviewed;

    public AnnotateImage Image { get; private set; }
    public long Id => Image.Id;
    public string FileName => Image.FileName;
    public string ThumbnailPath { get; }
    public string MasterFullPath { get; }

    partial void OnInDatasetChanged(bool value)
    {
        Image = Image with { InDataset = value };
        if (!_silent) _inDatasetChanged?.Invoke(this);
    }

    /// <summary>Sets the flag after a failed save without saving again.</summary>
    public void SetInDatasetWithoutNotifying(bool value)
    {
        _silent = true;
        InDataset = value;
        _silent = false;
    }

    /// <summary>Takes the status and label counts from the document just edited.</summary>
    public void Update(ImageDocument document)
    {
        var classes = document.Annotations.Select(annotation => annotation.ClassId).Concat(document.Tags).ToHashSet();
        var fromModel = document.Annotations.Any(annotation => annotation.Source == AnnotationSource.Model);
        Image = Image with { Status = document.Status, LabelCount = document.LabelCount, ClassIds = classes, HasModelLabels = fromModel };
        HasModelLabels = fromModel;
        Status = document.Status;
        LabelCount = document.LabelCount;
    }
}

public sealed partial class ClassItemViewModel : ObservableObject
{
    private readonly Action<ClassItemViewModel, bool> _tagChanged;
    private bool _silent;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _colorHex;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isTagged;

    public ClassItemViewModel(LabelClass source, int count, Action<ClassItemViewModel, bool> tagChanged)
    {
        Id = source.Id;
        _name = source.Name;
        _colorHex = source.Color;
        Hotkey = source.Hotkey;
        _count = count;
        _tagChanged = tagChanged;
    }

    public long Id { get; }
    public int? Hotkey { get; }
    public string HotkeyText => Hotkey?.ToString() ?? string.Empty;

    public void SetTaggedWithoutNotifying(bool value)
    {
        _silent = true;
        IsTagged = value;
        _silent = false;
    }

    partial void OnIsTaggedChanged(bool value)
    {
        if (!_silent) _tagChanged(this, value);
    }
}

public sealed partial class LabelRowViewModel : ObservableObject
{
    private readonly Action<LabelRowViewModel> _hiddenChanged;
    private readonly Action<LabelRowViewModel> _lockedChanged;
    private readonly Action<LabelRowViewModel> _selectedChanged;
    private bool _silent;

    /// <summary>Bound to the list item, so Ctrl and Shift selection in the list selects several labels.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isHidden;

    [ObservableProperty]
    private bool _isLocked;

    public LabelRowViewModel(
        Annotation annotation,
        string className,
        string colorHex,
        bool hidden,
        bool locked,
        Action<LabelRowViewModel> hiddenChanged,
        Action<LabelRowViewModel> lockedChanged,
        Action<LabelRowViewModel> selectedChanged)
    {
        _selectedChanged = selectedChanged;
        Annotation = annotation;
        ClassName = className;
        ColorHex = colorHex;
        _silent = true;
        IsHidden = hidden;
        IsLocked = locked;
        _silent = false;
        _hiddenChanged = hiddenChanged;
        _lockedChanged = lockedChanged;
    }

    public Annotation Annotation { get; }
    public Guid Id => Annotation.Id;
    public string ClassName { get; }
    public string ColorHex { get; }

    public string KindText => Annotation.Kind switch
    {
        ShapeKind.Box => "Box",
        ShapeKind.Polygon => "Polygon",
        ShapeKind.RotatedBox => "OBB",
        _ => "Point"
    };

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_silent) _selectedChanged(this);
    }

    public void SetSelectedWithoutNotifying(bool value)
    {
        if (IsSelected == value) return;
        _silent = true;
        IsSelected = value;
        _silent = false;
    }

    partial void OnIsHiddenChanged(bool value)
    {
        if (!_silent) _hiddenChanged(this);
    }

    partial void OnIsLockedChanged(bool value)
    {
        if (!_silent) _lockedChanged(this);
    }
}
