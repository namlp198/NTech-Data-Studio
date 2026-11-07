using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Augment;

/// <summary>A label as it appears on an output picture, in output pixels.</summary>
public sealed record RenderedLabel(Guid SourceId, long ClassId, ShapeKind Kind, IReadOnlyList<PixelPoint> Points);

public sealed record LabelTransformOptions
{
    /// <summary>Class after remapping; null drops the label. Null keeps every class as it is.</summary>
    public Func<long, long?>? ClassMap { get; init; }

    /// <summary>A label with less than this share of its area left in the picture is dropped.</summary>
    public int MinVisiblePercent { get; init; } = 30;

    /// <summary>A label covered by cutouts at least this much is dropped; 0 keeps every label.</summary>
    public int CutoutDropPercent { get; init; }
}

/// <summary>
/// Carries labels through the same matrix the pixels went through (docs/architecture/PIPELINE.md).
/// A box that no longer lines up with the axes grows to the box around it; polygons are
/// clipped at the border; a rotated box is refitted with the smallest enclosing rectangle,
/// which may poke slightly outside the picture where the border cut it.
/// </summary>
public static class LabelTransformer
{
    private const double MinAreaPx = 1;

    public static IReadOnlyList<RenderedLabel> Transform(
        IReadOnlyList<Annotation> annotations,
        RenderPlan plan,
        LabelTransformOptions options)
    {
        var labels = new List<RenderedLabel>(annotations.Count);
        foreach (var annotation in annotations)
        {
            var classId = annotation.ClassId;
            if (options.ClassMap is { } map)
            {
                if (map(classId) is not { } mapped) continue;
                classId = mapped;
            }

            var points = TransformShape(annotation, plan, options.MinVisiblePercent / 100.0);
            if (points is null) continue;
            if (options.CutoutDropPercent > 0 && CutoutCoverage(points, plan) * 100 >= options.CutoutDropPercent)
                continue;

            labels.Add(new RenderedLabel(annotation.Id, classId, annotation.Kind, points));
        }

        return labels;
    }

    private static IReadOnlyList<PixelPoint>? TransformShape(Annotation annotation, RenderPlan plan, double minVisible)
    {
        var transform = plan.Transform;
        double width = plan.OutputWidth;
        double height = plan.OutputHeight;
        var moved = annotation.Points.Select(transform.Apply).ToList();

        switch (annotation.Kind)
        {
            case ShapeKind.Point:
                var point = moved[0];
                return point.X >= 0 && point.X <= width && point.Y >= 0 && point.Y <= height
                    ? new[] { point }
                    : null;

            case ShapeKind.Box:
                return FitBox(moved, width, height, minVisible);

            case ShapeKind.RotatedBox:
                return FitRotatedBox(moved, transform.IsSimilarity, width, height, minVisible);

            case ShapeKind.Polygon:
                return ClipPolygon(moved, width, height, minVisible);

            default:
                return null;
        }
    }

    private static IReadOnlyList<PixelPoint>? FitBox(List<PixelPoint> moved, double width, double height, double minVisible)
    {
        // When the matrix keeps axes this is exact; otherwise it is the box around the turned corners.
        var around = PolygonMath.BoundingBox(moved);
        if (around.Area < 1e-9) return null;

        var left = Math.Max(0, around.X);
        var top = Math.Max(0, around.Y);
        var right = Math.Min(width, around.Right);
        var bottom = Math.Min(height, around.Bottom);
        if (right <= left || bottom <= top) return null;

        var clipped = new PixelRect(left, top, right - left, bottom - top);
        if (clipped.Area < MinAreaPx || clipped.Area / around.Area < minVisible) return null;
        return clipped.Corners();
    }

    private static IReadOnlyList<PixelPoint>? FitRotatedBox(
        List<PixelPoint> moved,
        bool keepsRectangle,
        double width,
        double height,
        double minVisible)
    {
        var area = PolygonMath.Area(moved);
        if (area < 1e-9) return null;

        var clipped = ConvexGeometry.ClipToRectangle(moved, width, height);
        var visible = PolygonMath.Area(clipped);
        if (visible < MinAreaPx || visible / area < minVisible) return null;

        // Untouched by the border and still a rectangle: keep the exact corners.
        if (keepsRectangle && visible / area > 1 - 1e-9)
            return moved;

        var fitted = ConvexGeometry.MinAreaRectangle(clipped);
        // The fit keeps the rectangle a true rectangle, so a corner can sit a few pixels past the border.
        // Clamping each corner would bend it; whoever writes the label decides how to handle that.
        return fitted.Count == 4 ? fitted : null;
    }

    private static IReadOnlyList<PixelPoint>? ClipPolygon(List<PixelPoint> moved, double width, double height, double minVisible)
    {
        var area = PolygonMath.Area(moved);
        if (area < 1e-9) return null;

        var clipped = ConvexGeometry.ClipToRectangle(moved, width, height);
        if (clipped.Count < 3) return null;

        var visible = PolygonMath.Area(clipped);
        return visible < MinAreaPx || visible / area < minVisible ? null : clipped;
    }

    /// <summary>Share of the label's bounding box under cutouts, summed over boxes and capped at 1.</summary>
    private static double CutoutCoverage(IReadOnlyList<PixelPoint> points, RenderPlan plan)
    {
        if (plan.Augment.Cutouts.Count == 0) return 0;

        var box = PolygonMath.BoundingBox(points);
        if (box.Area < 1e-9)
        {
            // A point is either under a cutout or not.
            var point = points[0];
            return plan.Augment.Cutouts.Any(cutout => Contains(cutout, plan, point)) ? 1 : 0;
        }

        var covered = 0.0;
        foreach (var cutout in plan.Augment.Cutouts)
        {
            var left = Math.Max(box.X, cutout.X * plan.OutputWidth);
            var top = Math.Max(box.Y, cutout.Y * plan.OutputHeight);
            var right = Math.Min(box.Right, (cutout.X + cutout.Width) * plan.OutputWidth);
            var bottom = Math.Min(box.Bottom, (cutout.Y + cutout.Height) * plan.OutputHeight);
            if (right > left && bottom > top)
                covered += (right - left) * (bottom - top);
        }

        return Math.Min(1, covered / box.Area);
    }

    private static bool Contains(NormalizedRect cutout, RenderPlan plan, PixelPoint point) =>
        point.X >= cutout.X * plan.OutputWidth && point.X <= (cutout.X + cutout.Width) * plan.OutputWidth
        && point.Y >= cutout.Y * plan.OutputHeight && point.Y <= (cutout.Y + cutout.Height) * plan.OutputHeight;
}

