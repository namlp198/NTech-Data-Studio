namespace NTech.DataStudio.Domain.Datasets;

public enum SplitKind
{
    Train,
    Val,
    Test
}

public sealed record SplitAssignment(long ImageId, SplitKind Split);

/// <summary>
/// A reproducible snapshot: the same config and <see cref="Seed"/> always
/// generate the same augmented images.
/// </summary>
public sealed record DatasetVersion(
    long Id,
    string Name,
    string PrepJson,
    string SplitJson,
    string AugmentJson,
    int Seed,
    DateTimeOffset CreatedAt);
