using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Annotations;

/// <summary>
/// Hit-testing and edits of finished shapes, in master pixels. Every result
/// stays inside the image. An edit that would break a shape returns null, so
/// the caller keeps the last valid one.
/// </summary>
public static class ShapeEditing
{
    private const double MinArea = 1;
    private const double Epsilon = 1e-6;
    private const int BisectionSteps = 30;

    /// <summary>A point annotation has no area, so it is hit within this many tolerances.</summary>
    private const double PointHitFactor = 2;

    public static bool Contains(IReadOnlyList<PixelPoint> polygon, PixelPoint point)
    {
        var inside = false;
        for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
        {
            var a = polygon[index];
            var b = polygon[previous];
            if (a.Y > point.Y != b.Y > point.Y
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    public static double DistanceToSegment(PixelPoint point, PixelPoint start, PixelPoint end, out PixelPoint nearest)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared < 1e-12
            ? 0
            : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        nearest = new PixelPoint(start.X + t * dx, start.Y + t * dy);
        return point.DistanceTo(nearest);
    }

    /// <summary>Index of the closest vertex within <paramref name="tolerance"/>, or -1.</summary>
    public static int HitVertex(IReadOnlyList<PixelPoint> points, PixelPoint point, double tolerance)
    {
        var best = -1;
        var bestDistance = tolerance;
        for (var index = 0; index < points.Count; index++)
        {
            var distance = points[index].DistanceTo(point);
            if (distance <= bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The closest edge of a closed shape within the tolerance. Edge <c>i</c> joins vertex i and i + 1.</summary>
    public static (int Edge, PixelPoint Point)? HitEdge(IReadOnlyList<PixelPoint> points, PixelPoint point, double tolerance)
    {
        if (points.Count < 2) return null;

        (int Edge, PixelPoint Point)? best = null;
        var bestDistance = tolerance;
        for (var index = 0; index < points.Count; index++)
        {
            var distance = DistanceToSegment(point, points[index], points[(index + 1) % points.Count], out var nearest);
            if (distance <= bestDistance)
            {
                best = (index, nearest);
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Inside the shape, or on its outline, so a thin box can still be picked.</summary>
    public static bool HitShape(ShapeKind kind, IReadOnlyList<PixelPoint> points, PixelPoint point, double tolerance)
    {
        if (kind == ShapeKind.Point)
            return points[0].DistanceTo(point) <= tolerance * PointHitFactor;

        return Contains(points, point) || HitEdge(points, point, tolerance) is not null;
    }

    /// <summary>Moves the whole shape, stopping at the image edge instead of squashing it.</summary>
    public static IReadOnlyList<PixelPoint> Translate(
        IReadOnlyList<PixelPoint> points, double dx, double dy, double width, double height)
    {
        var box = PolygonMath.BoundingBox(points);
        dx = Math.Max(-box.X, Math.Min(dx, width - box.Right));
        dy = Math.Max(-box.Y, Math.Min(dy, height - box.Bottom));
        return points.Select(point => new PixelPoint(point.X + dx, point.Y + dy)).ToList();
    }

    public static IReadOnlyList<PixelPoint> InsertVertex(IReadOnlyList<PixelPoint> points, int edge, PixelPoint point)
    {
        var result = points.ToList();
        result.Insert(edge + 1, point);
        return result;
    }

    /// <summary>
    /// Drags one vertex. A box keeps its sides axis-aligned and a rotated box
    /// keeps its angle, both pinned on the opposite corner; a polygon vertex
    /// moves freely unless that would make the polygon cross itself.
    /// </summary>
    public static IReadOnlyList<PixelPoint>? MoveVertex(
        ShapeKind kind, IReadOnlyList<PixelPoint> points, int index, PixelPoint target, double width, double height)
    {
        if (index < 0 || index >= points.Count || !target.IsFinite) return null;

        var clamped = target.ClampTo(width, height);
        switch (kind)
        {
            case ShapeKind.Point:
                return new[] { clamped };

            case ShapeKind.Polygon:
            {
                var moved = points.ToList();
                moved[index] = clamped;
                return PolygonMath.HasSelfIntersection(moved, closed: true) ? null : moved;
            }

            case ShapeKind.Box:
            {
                var rect = PixelRect.FromCorners(points[(index + 2) % 4], clamped);
                return rect.Area >= MinArea ? rect.Corners() : null;
            }

            case ShapeKind.RotatedBox:
                return MoveRotatedCorner(points, index, clamped, width, height);

            default:
                return null;
        }
    }

    private static IReadOnlyList<PixelPoint>? MoveRotatedCorner(
        IReadOnlyList<PixelPoint> points, int index, PixelPoint target, double width, double height)
    {
        var opposite = points[(index + 2) % 4];
        var (ux, uy) = Unit(points[0], points[1]);
        var (vx, vy) = Unit(points[1], points[2]);

        // Every corner as a share of the dragged diagonal along the two sides.
        var corner = points[index];
        var diagonalU = (corner.X - opposite.X) * ux + (corner.Y - opposite.Y) * uy;
        var diagonalV = (corner.X - opposite.X) * vx + (corner.Y - opposite.Y) * vy;
        if (Math.Abs(diagonalU) < 1e-9 || Math.Abs(diagonalV) < 1e-9) return null;

        var shareU = new double[4];
        var shareV = new double[4];
        for (var k = 0; k < 4; k++)
        {
            var dx = points[k].X - opposite.X;
            var dy = points[k].Y - opposite.Y;
            shareU[k] = (dx * ux + dy * uy) / diagonalU;
            shareV[k] = (dx * vx + dy * vy) / diagonalV;
        }

        var newU = (target.X - opposite.X) * ux + (target.Y - opposite.Y) * uy;
        var newV = (target.X - opposite.X) * vx + (target.Y - opposite.Y) * vy;

        PixelPoint[] Build(double factor)
        {
            var result = new PixelPoint[4];
            for (var k = 0; k < 4; k++)
            {
                var along = shareU[k] * newU * factor;
                var across = shareV[k] * newV * factor;
                result[k] = new PixelPoint(opposite.X + ux * along + vx * across, opposite.Y + uy * along + vy * across);
            }

            return result;
        }

        var full = Build(1);
        PixelPoint[] shape;
        if (Inside(full, width, height))
        {
            shape = full;
        }
        else
        {
            // Pulling the corner past the edge shrinks the box toward the pinned corner,
            // which keeps it a true rectangle; clamping corners one by one would not.
            double low = 0, high = 1;
            for (var step = 0; step < BisectionSteps; step++)
            {
                var middle = (low + high) / 2;
                if (Inside(Build(middle), width, height)) low = middle; else high = middle;
            }

            shape = Build(low);
        }

        return PolygonMath.Area(shape) >= MinArea ? shape : null;
    }

    private static bool Inside(IReadOnlyList<PixelPoint> points, double width, double height) =>
        points.All(point => point.X >= -Epsilon && point.X <= width + Epsilon
                         && point.Y >= -Epsilon && point.Y <= height + Epsilon);

    private static (double X, double Y) Unit(PixelPoint from, PixelPoint to)
    {
        var length = from.DistanceTo(to);
        return length < 1e-9 ? (0, 0) : ((to.X - from.X) / length, (to.Y - from.Y) / length);
    }
}
