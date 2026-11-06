using NTech.DataStudio.Domain.Annotations;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>Shared by every image of the session, so labels can be copied from one image to another.</summary>
public sealed class AnnotationClipboard
{
    public IReadOnlyList<Annotation> Items { get; private set; } = Array.Empty<Annotation>();
    public long SourceImageId { get; private set; }
    public int SourceWidth { get; private set; } = 1;
    public int SourceHeight { get; private set; } = 1;

    /// <summary>Pastes onto the same image shift a little more each time, so copies do not hide each other.</summary>
    public int PasteCount { get; internal set; }

    public bool HasItems => Items.Count > 0;

    public void Set(IReadOnlyList<Annotation> items, long imageId, int width, int height)
    {
        Items = items;
        SourceImageId = imageId;
        SourceWidth = Math.Max(1, width);
        SourceHeight = Math.Max(1, height);
        PasteCount = 0;
    }

    public void Clear()
    {
        Items = Array.Empty<Annotation>();
        PasteCount = 0;
    }
}
