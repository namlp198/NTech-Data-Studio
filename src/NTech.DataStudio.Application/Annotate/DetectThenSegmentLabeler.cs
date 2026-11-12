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
    public const double MinAgreement = 0.3;

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

    /// <summary>The outline SAM gives for the box, or null when it gave none or one that does not look like the object of the box.</summary>
    private async Task<IReadOnlyList<PixelPoint>?> CutOutAsync(string imagePath, PixelRect? region, PixelRect box, CancellationToken cancellationToken)
    {
        var request = new SegmentRequest(Array.Empty<SegmentPrompt>(), box);
        var outline = await _segmenter.SegmentAsync(imagePath, region, request, cancellationToken);
        if (outline is null || outline.Count < 3) return null;

        var left = outline.Min(point => point.X);
        var top = outline.Min(point => point.Y);
        var bounds = new PixelRect(left, top, outline.Max(point => point.X) - left, outline.Max(point => point.Y) - top);
        return Agreement(bounds, box) >= MinAgreement ? outline : null;
    }

    internal static double Agreement(PixelRect first, PixelRect second)
    {
        var width = Math.Min(first.Right, second.Right) - Math.Max(first.X, second.X);
        var height = Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y);
        if (width <= 0 || height <= 0) return 0;

        var intersection = width * height;
        var union = first.Area + second.Area - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
