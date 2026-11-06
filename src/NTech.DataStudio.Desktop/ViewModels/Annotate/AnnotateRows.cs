using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Desktop.ViewModels.Annotate;

public sealed partial class ImageRowViewModel : ObservableObject
{
    [ObservableProperty]
    private ImageStatus _status;

    [ObservableProperty]
    private int _labelCount;

    public ImageRowViewModel(AnnotateImage image, string projectRoot)
    {
        Image = image;
        _status = image.Status;
        _labelCount = image.LabelCount;
        ThumbnailPath = Path.Combine(projectRoot, image.ThumbPath);
        MasterFullPath = Path.Combine(projectRoot, image.MasterPath);
    }

    public AnnotateImage Image { get; private set; }
    public long Id => Image.Id;
    public string FileName => Image.FileName;
    public string ThumbnailPath { get; }
    public string MasterFullPath { get; }

    /// <summary>Takes the status and label counts from the document just edited.</summary>
    public void Update(ImageDocument document)
    {
        var classes = document.Annotations.Select(annotation => annotation.ClassId).Concat(document.Tags).ToHashSet();
        Image = Image with { Status = document.Status, LabelCount = document.LabelCount, ClassIds = classes };
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
    private bool _silent;

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
        Action<LabelRowViewModel> lockedChanged)
    {
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

    partial void OnIsHiddenChanged(bool value)
    {
        if (!_silent) _hiddenChanged(this);
    }

    partial void OnIsLockedChanged(bool value)
    {
        if (!_silent) _lockedChanged(this);
    }
}
