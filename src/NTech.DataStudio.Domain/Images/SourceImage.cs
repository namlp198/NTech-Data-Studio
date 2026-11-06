namespace NTech.DataStudio.Domain.Images;

public enum RejectReason
{
    Corrupt,
    ExactDuplicate,
    NearDuplicate,
    Blurry,
    TooDark,
    TooBright
}

/// <summary>
/// One imported original, never modified. Analysis fields are null until the
/// file has been decoded once. <see cref="StoredPath"/> is relative to the
/// project when the file was copied in, absolute when only referenced.
/// <see cref="KeepOverride"/> means the user restored it, so the cleaning rules
/// must not reject it again.
/// </summary>
public sealed record SourceImage(
    long Id,
    string OriginalPath,
    string StoredPath,
    bool IsCopied,
    long FileSize,
    long ModifiedTicks,
    string Format,
    int Width,
    int Height,
    int BitDepth,
    int Channels,
    string? GroupKey,
    string? Sha256 = null,
    ulong? PHash = null,
    ulong? DHash = null,
    double? BlurScore = null,
    double? DarkFraction = null,
    double? BrightFraction = null,
    RejectReason? Reject = null,
    string? RejectDetail = null,
    bool KeepOverride = false)
{
    public int LongEdge => Math.Max(Width, Height);
    public bool IsAnalyzed => Sha256 is not null;
    public bool IsRejected => Reject is not null;
    public string FileName => Path.GetFileName(OriginalPath);
}
