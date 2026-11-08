using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>A point the user gave the segmenter: inside the object (<see cref="Positive"/>) or on what to leave out.</summary>
public sealed record SegmentPrompt(PixelPoint Position, bool Positive);

/// <summary>What to cut out: points, a box drawn around the object, or both.</summary>
public sealed record SegmentRequest(IReadOnlyList<SegmentPrompt> Points, PixelRect? Box = null)
{
    public bool IsEmpty => Points.Count == 0 && Box is null;
}

/// <summary>
/// Cuts an object out of a picture from a few points or a box (decision 0016). Positions and results are in the pixels
/// of the master image. Implementations keep what they worked out for the last pictures, so the first call for a picture
/// is slow (it analyses the whole picture) and the calls after it, one per mouse position, are quick.
/// <para>
/// <c>region</c> is the part of the picture to analyse. Null is the whole picture. A small object has few pixels in the
/// whole picture, so zooming in means analysing just the part on screen: the object then fills more of what the model
/// sees. Everything given to <see cref="SegmentAsync"/> must lie inside the region it names.
/// </para>
/// </summary>
public interface ISmartSegmenter
{
    /// <summary>Null when ready; otherwise why not, in words for the user (for example the model files are missing).</summary>
    string? Unavailable { get; }

    /// <summary>Analyses the picture (or the region) ahead of the first <see cref="SegmentAsync"/>. Calling it again for the same one is free.</summary>
    /// <exception cref="InvalidOperationException">The segmenter is unavailable or the picture cannot be read.</exception>
    Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default);

    /// <returns>The outline of the object the request describes; null when it selects nothing usable.</returns>
    Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
        string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default);
}
