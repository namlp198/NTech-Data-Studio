namespace NTech.DataStudio.Domain.Images;

public enum ImageStatus
{
    New,
    Labeled,
    Reviewed,
    Rejected,
    Null
}

/// <summary>
/// One master image: the thing that gets labeled. <see cref="MasterPath"/> is
/// relative to the project folder so a copied project still opens. A source
/// pixel maps to master as (x - OriginX) * Scale.
/// </summary>
public sealed record ImageItem(
    long Id,
    long SourceImageId,
    string MasterPath,
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    double Scale,
    ImageStatus Status);
