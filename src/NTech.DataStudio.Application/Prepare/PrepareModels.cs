using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Prepare;

/// <summary>Facts available without decoding the pixels.</summary>
public sealed record ImageHeader(string Format, int Width, int Height, int BitDepth, int Channels);

public sealed record ImageAnalysis(
    string Sha256,
    ulong PHash,
    ulong DHash,
    double BlurScore,
    double DarkFraction,
    double BrightFraction);

public enum GeometryMethod
{
    Downscale,
    Tile,
    RoiCrop
}

public enum MasterFormat
{
    Jpeg95,
    Png
}

public enum MonoMode
{
    KeepOneChannel,
    TripleChannel
}

/// <param name="MaxLongEdge">Downscale only; never upscales.</param>
/// <param name="TileSize">Tile only.</param>
/// <param name="TileOverlap">Tile only.</param>
/// <param name="Roi">RoiCrop only, in source pixels after EXIF rotation.</param>
public sealed record GeometryPlan(
    GeometryMethod Method,
    int MaxLongEdge = 0,
    int TileSize = 0,
    int TileOverlap = 0,
    PixelRect? Roi = null);

public sealed record NormalizeOptions(GeometryPlan Geometry, MasterFormat Format, MonoMode Mono);

/// <summary>One written master. Names are relative to the master and thumbs folders.</summary>
public sealed record MasterFile(
    string FileName,
    string ThumbnailFileName,
    int Width,
    int Height,
    int OriginX,
    int OriginY,
    double Scale,
    long FileSize);

/// <param name="MasterBytes">Real encoded size of everything this one image would produce.</param>
public sealed record NormalizePreview(
    byte[] BeforeJpeg,
    byte[] AfterJpeg,
    int SourceWidth,
    int SourceHeight,
    int MasterWidth,
    int MasterHeight,
    int MasterCount,
    long MasterBytes);

public sealed record PrepareProgress(int Done, int Total, string? Current = null);

/// <summary>A file could not be read as an image; the message is shown to the user.</summary>
public sealed class ImageReadException : Exception
{
    public ImageReadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed record ImageFailure(string FileName, string Message);
