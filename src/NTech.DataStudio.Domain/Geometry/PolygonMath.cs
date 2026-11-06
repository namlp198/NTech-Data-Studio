namespace NTech.DataStudio.Domain.Geometry;

/// <summary>Planar polygon helpers in pixel coordinates.</summary>
public static class PolygonMath
{
    /// <summary>Shoelace area; orientation does not matter.</summary>
    public static double Area(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count < 3) return 0;

        var doubled = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Count];
            doubled += current.X * next.Y - next.X * current.Y;
        }

        return Math.Abs(doubled) / 2;
    }

    public static PixelRect BoundingBox(IReadOnlyList<PixelPoint> points)
    {
        if (points.Count == 0) return default;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// True when two non-adjacent segments touch or cross. Adjacent segments
    /// share a vertex by construction, so they are skipped.
    /// </summary>
    public static bool HasSelfIntersection(IReadOnlyList<PixelPoint> points, bool closed)
    {
        if (points.Count < (closed ? 4 : 3)) return false;

        var segmentCount = closed ? points.Count : points.Count - 1;
        for (var first = 0; first < segmentCount; first++)
        {
            var firstNext = (first + 1) % points.Count;
            for (var second = first + 1; second < segmentCount; second++)
            {
                var secondNext = (second + 1) % points.Count;
                if (firstNext == second || secondNext == first) continue;
                if (SegmentsIntersect(points[first], points[firstNext], points[second], points[secondNext]))
                    return true;
            }
        }

        return false;
    }

    public static bool SegmentsIntersect(PixelPoint a1, PixelPoint a2, PixelPoint b1, PixelPoint b2)
    {
        var d1 = Cross(b1, b2, a1);
        var d2 = Cross(b1, b2, a2);
        var d3 = Cross(a1, a2, b1);
        var d4 = Cross(a1, a2, b2);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return (d1 == 0 && OnSegment(b1, b2, a1))
            || (d2 == 0 && OnSegment(b1, b2, a2))
            || (d3 == 0 && OnSegment(a1, a2, b1))
            || (d4 == 0 && OnSegment(a1, a2, b2));
    }

    private static double Cross(PixelPoint origin, PixelPoint end, PixelPoint point) =>
        (end.X - origin.X) * (point.Y - origin.Y) - (end.Y - origin.Y) * (point.X - origin.X);

    private static bool OnSegment(PixelPoint start, PixelPoint end, PixelPoint point) =>
        point.X >= Math.Min(start.X, end.X) && point.X <= Math.Max(start.X, end.X)
        && point.Y >= Math.Min(start.Y, end.Y) && point.Y <= Math.Max(start.Y, end.Y);
}
