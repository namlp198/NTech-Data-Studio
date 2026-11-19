using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>Cuts the object inside a box out with SAM. Shared by detect + SAM labeling (0018) and box to polygon conversion (0022).</summary>
internal static class BoxCutter
{
    /// <summary>Overlap (intersection over union) between the bounds of a cut-out and its box below which the cut-out is judged to be something else.</summary>
    public const double MinAgreement = 0.3;

    /// <summary>The outline SAM gives for the box, or null when it gave none or one that does not look like the object of the box.</summary>
    public static async Task<IReadOnlyList<PixelPoint>?> CutOutAsync(
        ISmartSegmenter segmenter, string imagePath, PixelRect? region, PixelRect box, CancellationToken cancellationToken)
    {
        var request = new SegmentRequest(Array.Empty<SegmentPrompt>(), box);
        var outline = await segmenter.SegmentAsync(imagePath, region, request, cancellationToken);
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
