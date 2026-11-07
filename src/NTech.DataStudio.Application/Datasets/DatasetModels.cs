using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Datasets;

/// <summary>One active master as the dataset screen sees it, with the labels it carries.</summary>
/// <param name="LabelsByClass">Annotation count per class.</param>
public sealed record DatasetImage(
    long Id,
    long SourceImageId,
    string? GroupKey,
    string MasterPath,
    int Width,
    int Height,
    ImageStatus Status,
    IReadOnlyDictionary<long, int> LabelsByClass,
    IReadOnlySet<long> TagClassIds);

/// <summary>An image to place in a split. Images sharing a <see cref="UnitKey"/> always land in the same split.</summary>
public sealed record SplitItem(long ImageId, string UnitKey, IReadOnlySet<long> ClassIds);

public sealed record SplitResult(
    IReadOnlyDictionary<long, SplitKind> Assignments,
    IReadOnlyList<(long ClassId, SplitKind Missing)> UnplacedClasses);

/// <summary>Counts of one class in each split, indexed by <c>(int)SplitKind</c>.</summary>
public sealed record ClassSplitCount(long ClassId, string Name, int[] Images, int[] Labels)
{
    public int TotalLabels => Labels.Sum();
}

/// <summary>What a config would produce, worked out without writing anything.</summary>
public sealed record VersionPlan(
    IReadOnlyList<SplitAssignment> Assignments,
    IReadOnlyList<ClassSplitCount> Classes,
    int[] ImagesPerSplit,
    int[] LabelsPerSplit,
    int ExcludedUnlabeled,
    int DroppedNull,
    int EmptyImages,
    int UnitCount,
    int ExpectedTrainOutput,
    IReadOnlyList<string> Warnings)
{
    public int TotalImages => ImagesPerSplit.Sum();

    /// <summary>Images in the exported dataset: train after the multiplier, plus val and test untouched.</summary>
    public int ExpectedTotalOutput => ExpectedTrainOutput + ImagesPerSplit[(int)SplitKind.Val] + ImagesPerSplit[(int)SplitKind.Test];
}

/// <summary>Everything the dataset screen loads at once.</summary>
public sealed record DatasetWorkspace(
    string ProjectRoot,
    IReadOnlyList<DatasetImage> Images,
    IReadOnlyList<Domain.Classes.LabelClass> Classes,
    IReadOnlyList<DatasetVersion> Versions);
