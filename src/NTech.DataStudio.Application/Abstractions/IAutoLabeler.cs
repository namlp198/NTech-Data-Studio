using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>One object a trained model found: its class by name, how sure it is, and the outline in master pixels.</summary>
/// <param name="Shape">What the label is: a polygon (Segment) or a box (Detect, decision 0019); a box holds its four corners.</param>
/// <param name="IsBoxOutline">The outline is only the detected box (SAM could not cut the object out), left for the user to fix by hand (decision 0018).</param>
public sealed record AutoLabel(string ClassName, double Score, IReadOnlyList<PixelPoint> Outline, bool IsBoxOutline = false, ShapeKind Shape = ShapeKind.Polygon);

/// <summary>
/// Finds every object of the classes a trained model knows (decisions 0017 and 0018). The model is a file the Trainer
/// exported (<c>model.onnx</c> next to its <c>model.meta.json</c>); nothing of the Trainer is called. Which model, and
/// where its file is, is the business of the implementation (the composition root sets it from the settings).
/// </summary>
public interface IAutoLabeler
{
    /// <summary>Null when ready; otherwise why not, in words for the user (no model chosen, file or meta missing, not a segment model).</summary>
    string? Unavailable { get; }

    /// <summary>Class names of the model in the order it knows them; empty while <see cref="Unavailable"/>.</summary>
    IReadOnlyList<string> ClassNames { get; }

    /// <param name="minScore">Objects scoring below this are left out.</param>
    /// <exception cref="InvalidOperationException">The labeler is unavailable or the picture cannot be read.</exception>
    Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default);
}
