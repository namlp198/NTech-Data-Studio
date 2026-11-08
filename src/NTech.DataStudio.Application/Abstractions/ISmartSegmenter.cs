using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>A point the user gave the segmenter: inside the object (<see cref="Positive"/>) or on what to leave out.</summary>
public sealed record SegmentPrompt(PixelPoint Position, bool Positive);

/// <summary>
/// Cuts an object out of a picture from a few points (decision 0016). Positions and results are in the pixels of
/// the master image. Implementations keep what they worked out for the last image, so the first call for an image is
/// slow (it analyses the whole picture) and the calls after it, one per mouse position, are quick.
/// </summary>
public interface ISmartSegmenter
{
    /// <summary>Null when ready; otherwise why not, in words for the user (for example the model files are missing).</summary>
    string? Unavailable { get; }

    /// <summary>Analyses the picture ahead of the first <see cref="SegmentAsync"/>. Calling it again for the same file is free.</summary>
    /// <exception cref="InvalidOperationException">The segmenter is unavailable or the picture cannot be read.</exception>
    Task PrepareAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <returns>The outline of the object the points describe; null when they select nothing usable.</returns>
    Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
        string imagePath, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken = default);
}
