using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>The labels of the image on screen. Changed only through <see cref="AnnotateService"/>, after the database has accepted the change.</summary>
public sealed class ImageDocument
{
    private readonly List<Annotation> _annotations;
    private readonly HashSet<long> _tags;

    public ImageDocument(AnnotateImage image, ImageContent content)
    {
        ImageId = image.Id;
        Width = image.Width;
        Height = image.Height;
        Status = image.Status;
        _annotations = content.Annotations.ToList();
        _tags = content.Tags.ToHashSet();
    }

    public long ImageId { get; }
    public int Width { get; }
    public int Height { get; }
    public ImageStatus Status { get; internal set; }
    public IReadOnlyList<Annotation> Annotations => _annotations;
    public IReadOnlySet<long> Tags => _tags;
    public bool HasLabels => _annotations.Count > 0 || _tags.Count > 0;
    public int LabelCount => _annotations.Count + _tags.Count;

    public Annotation? Find(Guid id) => _annotations.FirstOrDefault(annotation => annotation.Id == id);

    internal List<Annotation> MutableAnnotations => _annotations;
    internal HashSet<long> MutableTags => _tags;
}
