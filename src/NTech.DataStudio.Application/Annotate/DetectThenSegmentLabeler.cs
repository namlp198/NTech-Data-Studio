using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// Automatic labeling with a detect model and SAM (decision 0018): the detect model finds the boxes, SAM cuts each box
/// out into a polygon. A box SAM cannot cut out, or whose cut-out does not match it, stays as the box itself (four
/// corners) so the object is not lost; <see cref="AutoLabel.IsBoxOutline"/> says so.
/// </summary>
public sealed class DetectThenSegmentLabeler : IAutoLabeler
{
    /// <summary>Overlap (intersection over union) between the bounds of a cut-out and its box below which the cut-out is judged to be something else.</summary>
    public const double MinAgreement = BoxCutter.MinAgreement;

    private readonly IBoxDetector _detector;
    private readonly ISmartSegmenter _segmenter;

    public DetectThenSegmentLabeler(IBoxDetector detector, ISmartSegmenter segmenter)
    {
        _detector = detector;
        _segmenter = segmenter;
    }

    public string? Unavailable => _detector.Unavailable ?? _segmenter.Unavailable;

    public IReadOnlyList<string> ClassNames => _detector.ClassNames;

    public async Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default)
    {
        if (Unavailable is { } why) throw new InvalidOperationException(why);

        var detection = await _detector.DetectAsync(imagePath, minScore, cancellationToken);
        var labels = new List<AutoLabel>();
        foreach (var group in SamRegionPlanner.Plan(detection.ImageWidth, detection.ImageHeight, detection.Boxes))
        {
            foreach (var found in group.Boxes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outline = await CutOutAsync(imagePath, group.Region, found.Box, cancellationToken);
                labels.Add(outline is null
                    ? new AutoLabel(found.ClassName, found.Score, found.Box.Corners(), IsBoxOutline: true)
                    : new AutoLabel(found.ClassName, found.Score, outline));
            }
        }

        return labels.OrderByDescending(label => label.Score).ToList();
    }

    private Task<IReadOnlyList<PixelPoint>?> CutOutAsync(string imagePath, PixelRect? region, PixelRect box, CancellationToken cancellationToken) =>
        BoxCutter.CutOutAsync(_segmenter, imagePath, region, box, cancellationToken);
}
