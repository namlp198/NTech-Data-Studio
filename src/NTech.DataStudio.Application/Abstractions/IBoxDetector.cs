using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>One box a detect model found: class by name, score, and the box in master pixels.</summary>
public sealed record DetectedBox(string ClassName, double Score, PixelRect Box);

/// <param name="ImageWidth">Width of the master picture the boxes are in.</param>
/// <param name="ImageHeight">Height of the master picture the boxes are in.</param>
public sealed record BoxDetection(int ImageWidth, int ImageHeight, IReadOnlyList<DetectedBox> Boxes);

/// <summary>
/// Finds boxes with a detect model the Trainer exported (<c>model.onnx</c> next to its <c>model.meta.json</c>), decision 0018.
/// The boxes are then cut out by SAM (<see cref="ISmartSegmenter"/>) to become polygons.
/// </summary>
public interface IBoxDetector
{
    /// <summary>Null when ready; otherwise why not, in words for the user (no model chosen, file or meta missing, not a detect model).</summary>
    string? Unavailable { get; }

    /// <summary>Class names of the model in the order it knows them; empty while <see cref="Unavailable"/>.</summary>
    IReadOnlyList<string> ClassNames { get; }

    /// <summary>Chooses the model; null forgets it. Nothing is loaded until the first <see cref="DetectAsync"/>.</summary>
    void SetModel(string? onnxPath);

    /// <param name="minScore">Boxes scoring below this are left out.</param>
    /// <exception cref="InvalidOperationException">The detector is unavailable or the picture cannot be read.</exception>
    Task<BoxDetection> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default);
}
