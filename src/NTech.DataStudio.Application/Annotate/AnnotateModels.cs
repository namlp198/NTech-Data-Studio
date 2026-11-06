using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Annotate;

public enum AnnotateTool
{
    Select,
    Box,
    Polygon,
    RotatedBox,
    Tag
}

/// <summary>One active master image as the annotation screen lists it.</summary>
public sealed record AnnotateImage(
    long Id,
    string MasterPath,
    string ThumbPath,
    int Width,
    int Height,
    ImageStatus Status,
    int LabelCount,
    IReadOnlySet<long> ClassIds)
{
    public string FileName => Path.GetFileName(MasterPath);
}

public sealed record ImageContent(IReadOnlyList<Annotation> Annotations, IReadOnlySet<long> Tags);

public sealed record ClassUsage(int AnnotationCount, int ImageCount);

/// <summary>What the caller wants to change. Statuses are worked out by the service.</summary>
/// <param name="Before">Annotations as they are now that this edit changes or removes.</param>
/// <param name="After">Annotations as they should be: changed and added ones.</param>
/// <param name="ForcedStatus">Only for Null and Review toggles, which change nothing else.</param>
public sealed record EditRequest(
    IReadOnlyList<Annotation> Before,
    IReadOnlyList<Annotation> After,
    IReadOnlyList<long> TagsAdded,
    IReadOnlyList<long> TagsRemoved,
    ImageStatus? ForcedStatus = null)
{
    public static EditRequest Add(IReadOnlyList<Annotation> added) =>
        new(Array.Empty<Annotation>(), added, Array.Empty<long>(), Array.Empty<long>());

    public static EditRequest Remove(IReadOnlyList<Annotation> removed) =>
        new(removed, Array.Empty<Annotation>(), Array.Empty<long>(), Array.Empty<long>());

    public static EditRequest Replace(Annotation before, Annotation after) =>
        new(new[] { before }, new[] { after }, Array.Empty<long>(), Array.Empty<long>());

    public static EditRequest Tags(IReadOnlyList<long> added, IReadOnlyList<long> removed) =>
        new(Array.Empty<Annotation>(), Array.Empty<Annotation>(), added, removed);

    public static EditRequest Status(ImageStatus status) =>
        new(Array.Empty<Annotation>(), Array.Empty<Annotation>(), Array.Empty<long>(), Array.Empty<long>(), status);
}

/// <summary>A finished edit: everything needed to apply it again or take it back.</summary>
public sealed record AnnotationEdit(
    long ImageId,
    IReadOnlyList<Annotation> Before,
    IReadOnlyList<Annotation> After,
    IReadOnlyList<long> TagsAdded,
    IReadOnlyList<long> TagsRemoved,
    ImageStatus StatusBefore,
    ImageStatus StatusAfter);

/// <summary>One atomic write to the project database.</summary>
public sealed record StoreChange(
    long ImageId,
    IReadOnlyList<Guid> Deletes,
    IReadOnlyList<Annotation> Upserts,
    IReadOnlyList<long> TagsAdded,
    IReadOnlyList<long> TagsRemoved,
    ImageStatus Status);
