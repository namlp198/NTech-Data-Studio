using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>One object a trained model found: its class by name, how sure it is, and the outline in master pixels.</summary>
public sealed record AutoLabel(string ClassName, double Score, IReadOnlyList<PixelPoint> Outline);

/// <summary>
/// Finds every object of the classes a trained segment model knows (decision 0017). The model is a file the Trainer
/// exported (<c>model.onnx</c> next to its <c>model.meta.json</c>); nothing of the Trainer is called.
/// </summary>
public interface IAutoLabeler
{
    /// <summary>Path of the .onnx file in use, or null when none was chosen.</summary>
    string? ModelPath { get; }

    /// <summary>Null when ready; otherwise why not, in words for the user (no model chosen, file or meta missing, not a segment model).</summary>
    string? Unavailable { get; }

    /// <summary>Class names of the model in the order it knows them; empty while <see cref="Unavailable"/>.</summary>
    IReadOnlyList<string> ClassNames { get; }

    /// <summary>Chooses the model; null forgets it. Nothing is loaded until the first <see cref="DetectAsync"/>.</summary>
    void SetModel(string? onnxPath);

    /// <param name="minScore">Objects scoring below this are left out.</param>
    /// <exception cref="InvalidOperationException">The labeler is unavailable or the picture cannot be read.</exception>
    Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default);
}
