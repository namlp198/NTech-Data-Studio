using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>Boxes that SAM analyses together: one region of the picture (null is the whole picture) and what is cut out of it.</summary>
public sealed record SamRegionGroup(PixelRect? Region, IReadOnlyList<DetectedBox> Boxes);

/// <summary>
/// Decides which part of the picture SAM analyses for which boxes (decision 0018). SAM looks at the picture at a fixed
/// working size, so a small object in a big picture is a few pixels to it and its outline comes out coarse; analysing
/// only the part around it magnifies it, but that only pays for tiny boxes (see <see cref="TinyBoxSamPixels"/>). Analysing costs one encoder run per region, so small boxes that lie close
/// together share one region. Every other box shares the one analysis of the whole picture.
/// </summary>
public static class SamRegionPlanner
{
    /// <summary>The long side SAM's encoder gives the picture: whatever the picture's size, SAM sees it scaled to this.</summary>
    public const int SamInputSide = 1024;

    /// <summary>
    /// A box whose long side is under this many pixels in SAM's view is tiny and gets a region of its own. Bigger boxes use
    /// the whole picture, one analysis for all of them: measured on 1024x576 and 2048x1294 pictures of wire harnesses,
    /// regions around objects of 20 to 140 px (SAM's view) did not cut them out better than the whole picture, and cost four
    /// to five times as long.
    /// </summary>
    public const int TinyBoxSamPixels = 24;

    /// <summary>A region that grows past this share of the short side of the picture is dropped for the whole picture.</summary>
    public const double MaxRegionShare = 0.5;

    /// <summary>Context around a small box: this many times its long side on every side, so the box is a third of the region.</summary>
    public const double ContextFactor = 1.0;

    /// <summary>Context never shrinks below this many pixels, so a tiny box still has surroundings to look at.</summary>
    public const double MinContext = 16;

    public static IReadOnlyList<SamRegionGroup> Plan(int imageWidth, int imageHeight, IReadOnlyList<DetectedBox> boxes)
    {
        if (boxes.Count == 0) return Array.Empty<SamRegionGroup>();

        var picture = new PixelRect(0, 0, imageWidth, imageHeight);
        var maxRegion = Math.Min(imageWidth, imageHeight) * MaxRegionShare;
        var samScale = (double)SamInputSide / Math.Max(imageWidth, imageHeight);

        var whole = new List<DetectedBox>();
        var groups = new List<(PixelRect Region, List<DetectedBox> Boxes)>();
        // Reading order keeps neighbours next to each other, so greedy merging finds them.
        foreach (var box in boxes.OrderBy(item => item.Box.Y).ThenBy(item => item.Box.X))
        {
            var longSide = Math.Max(box.Box.Width, box.Box.Height);
            if (longSide * samScale >= TinyBoxSamPixels)
            {
                whole.Add(box);
                continue;
            }

            var around = Around(box.Box, picture);
            if (Math.Max(around.Width, around.Height) > maxRegion)
            {
                whole.Add(box);
                continue;
            }

            var index = groups.FindIndex(group => Fits(Union(group.Region, around), maxRegion));
            if (index < 0)
            {
                groups.Add((around, new List<DetectedBox> { box }));
                continue;
            }

            groups[index].Boxes.Add(box);
            groups[index] = (Union(groups[index].Region, around), groups[index].Boxes);
        }

        var result = new List<SamRegionGroup>();
        if (whole.Count > 0) result.Add(new SamRegionGroup(null, whole));
        result.AddRange(groups.Select(group => new SamRegionGroup(group.Region, group.Boxes)));
        return result;
    }

    private static bool Fits(PixelRect region, double maxRegion) => Math.Max(region.Width, region.Height) <= maxRegion;

    private static PixelRect Around(PixelRect box, PixelRect picture)
    {
        var context = Math.Max(MinContext, Math.Max(box.Width, box.Height) * ContextFactor);
        var left = Math.Max(picture.X, box.X - context);
        var top = Math.Max(picture.Y, box.Y - context);
        var right = Math.Min(picture.Right, box.Right + context);
        var bottom = Math.Min(picture.Bottom, box.Bottom + context);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static PixelRect Union(PixelRect first, PixelRect second)
    {
        var left = Math.Min(first.X, second.X);
        var top = Math.Min(first.Y, second.Y);
        return new PixelRect(left, top, Math.Max(first.Right, second.Right) - left, Math.Max(first.Bottom, second.Bottom) - top);
    }
}
