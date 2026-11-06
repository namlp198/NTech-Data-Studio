using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// How an image's status follows its labels: New → Labeled → Reviewed, and
/// Null for an image that has nothing to find.
/// </summary>
public static class StatusRules
{
    /// <summary>After labels or tags change. Editing a reviewed image asks for a new review.</summary>
    public static ImageStatus AfterContentEdit(ImageStatus current, bool hasLabels)
    {
        if (hasLabels) return ImageStatus.Labeled;
        return current == ImageStatus.Null ? ImageStatus.Null : ImageStatus.New;
    }

    /// <summary>The Null flag only makes sense on an image without labels. Null → New and back.</summary>
    public static ImageStatus? ToggleNull(ImageStatus current, bool hasLabels)
    {
        if (hasLabels) return null;
        return current is ImageStatus.Null or ImageStatus.Reviewed ? ImageStatus.New : ImageStatus.Null;
    }

    /// <summary>Approves a labeled or Null image; approving again withdraws it.</summary>
    public static ImageStatus? ToggleReviewed(ImageStatus current, bool hasLabels) => current switch
    {
        ImageStatus.Labeled or ImageStatus.Null => ImageStatus.Reviewed,
        ImageStatus.Reviewed => hasLabels ? ImageStatus.Labeled : ImageStatus.Null,
        _ => null
    };
}
