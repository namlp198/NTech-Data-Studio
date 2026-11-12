using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Annotations;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// Automatic labeling for the Detect task (decision 0019): the boxes a detect model found are the labels, as boxes.
/// No SAM is involved; that is the Segment task's detect-and-cut-out way (<see cref="DetectThenSegmentLabeler"/>).
/// </summary>
public sealed class BoxAutoLabeler : IAutoLabeler
{
    private readonly IBoxDetector _detector;

    public BoxAutoLabeler(IBoxDetector detector)
    {
        _detector = detector;
    }

    public string? Unavailable => _detector.Unavailable;

    public IReadOnlyList<string> ClassNames => _detector.ClassNames;

    public async Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default)
    {
        var detection = await _detector.DetectAsync(imagePath, minScore, cancellationToken);
        return detection.Boxes
            .OrderByDescending(found => found.Score)
            .Select(found => new AutoLabel(found.ClassName, found.Score, found.Box.Corners(), Shape: ShapeKind.Box))
            .ToList();
    }
}
