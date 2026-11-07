using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Export;

/// <summary>
/// Turns a label into the shape a layout can hold: a box, a polygon or an oriented box.
/// Every format reads labels through here, so the same label never exports two ways.
/// </summary>
public static class LabelShapes
{
    private const double MinSide = 1e-6;

    /// <summary>The axis-aligned box around the label, kept inside the picture; null for a point or a flat label.</summary>
    public static PixelRect? Box(RenderedLabel label, int width, int height)
    {
        if (label.Kind == ShapeKind.Point) return null;

        var around = PolygonMath.BoundingBox(label.Points);
        var left = Math.Clamp(around.X, 0, width);
        var top = Math.Clamp(around.Y, 0, height);
        var right = Math.Clamp(around.Right, 0, width);
        var bottom = Math.Clamp(around.Bottom, 0, height);
        return right - left > MinSide && bottom - top > MinSide
            ? new PixelRect(left, top, right - left, bottom - top)
            : null;
    }

    /// <summary>The outline: a polygon as drawn, a box or rotated box as its four corners. Null for a point.</summary>
    public static IReadOnlyList<PixelPoint>? Polygon(RenderedLabel label, int width, int height)
    {
        if (label.Kind == ShapeKind.Point) return null;

        var points = label.Points.Select(point => point.ClampTo(width, height)).ToList();
        return PolygonMath.Area(points) > MinSide ? points : null;
    }

    /// <summary>
    /// Four corners of an oriented box lying inside the picture. A box keeps its corners, a polygon
    /// gets the smallest rectangle around it, a rotated box that the border pushed out is shrunk.
    /// </summary>
    public static IReadOnlyList<PixelPoint>? Oriented(RenderedLabel label, int width, int height)
    {
        IReadOnlyList<PixelPoint> corners;
        switch (label.Kind)
        {
            case ShapeKind.Point:
                return null;
            case ShapeKind.Polygon:
                corners = ConvexGeometry.MinAreaRectangle(label.Points);
                break;
            default:
                corners = label.Points;
                break;
        }

        return corners.Count == 4 ? FitInside(corners, width, height) : null;
    }

    /// <summary>
    /// A rectangle that pokes past the border is scaled down about its centre until it fits. Clamping
    /// the corners instead would bend it into a shape that is not a rectangle, and Ultralytics rejects
    /// an oriented label with a coordinate outside 0..1. Null when the centre itself is outside.
    /// </summary>
    public static IReadOnlyList<PixelPoint>? FitInside(IReadOnlyList<PixelPoint> corners, double width, double height)
    {
        var centre = new PixelPoint(corners.Average(point => point.X), corners.Average(point => point.Y));
        var factor = 1.0;
        foreach (var corner in corners)
        {
            factor = Math.Min(factor, Reach(centre.X, corner.X - centre.X, width));
            factor = Math.Min(factor, Reach(centre.Y, corner.Y - centre.Y, height));
        }

        if (factor <= 1e-3) return null;
        if (factor >= 1 - 1e-12)
            return corners.Select(point => point.ClampTo(width, height)).ToList();

        return corners
            .Select(corner => new PixelPoint(
                centre.X + (corner.X - centre.X) * factor,
                centre.Y + (corner.Y - centre.Y) * factor).ClampTo(width, height))
            .ToList();
    }

    /// <summary>How far, as a share of <paramref name="delta"/>, one can go from <paramref name="from"/> before leaving 0..limit.</summary>
    private static double Reach(double from, double delta, double limit)
    {
        if (Math.Abs(delta) < 1e-12) return from >= 0 && from <= limit ? 1 : -1;
        var edge = delta > 0 ? limit : 0;
        return (edge - from) / delta;
    }
}
