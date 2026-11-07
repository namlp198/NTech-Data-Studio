namespace NTech.DataStudio.Domain.Geometry;

/// <summary>Hulls, clipping and the smallest enclosing rectangle, for labels after a transform.</summary>
public static class ConvexGeometry
{
    /// <summary>Convex hull in counter-clockwise order (Andrew's monotone chain); collinear and repeated points are dropped.</summary>
    public static IReadOnlyList<PixelPoint> Hull(IEnumerable<PixelPoint> points)
    {
        var sorted = points
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();
        if (sorted.Count < 3) return sorted;

        var hull = new List<PixelPoint>(2 * sorted.Count);
        foreach (var point in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }

        var lowerCount = hull.Count + 1;
        for (var index = sorted.Count - 2; index >= 0; index--)
        {
            var point = sorted[index];
            while (hull.Count >= lowerCount && Cross(hull[^2], hull[^1], point) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// <summary>
    /// The enclosing rectangle of least area, as four corners in drawing order.
    /// Empty when the points do not enclose any area.
    /// </summary>
    public static IReadOnlyList<PixelPoint> MinAreaRectangle(IEnumerable<PixelPoint> points)
    {
        var hull = Hull(points);
        if (hull.Count < 3) return Array.Empty<PixelPoint>();

        var bestArea = double.PositiveInfinity;
        PixelPoint[] best = Array.Empty<PixelPoint>();
        for (var index = 0; index < hull.Count; index++)
        {
            var start = hull[index];
            var end = hull[(index + 1) % hull.Count];
            var length = start.DistanceTo(end);
            if (length < 1e-12) continue;

            // The best rectangle has a side along a hull edge, so trying each edge is enough.
            var ux = (end.X - start.X) / length;
            var uy = (end.Y - start.Y) / length;
            var vx = -uy;
            var vy = ux;

            var minU = double.PositiveInfinity;
            var maxU = double.NegativeInfinity;
            var minV = double.PositiveInfinity;
            var maxV = double.NegativeInfinity;
            foreach (var point in hull)
            {
                var u = point.X * ux + point.Y * uy;
                var v = point.X * vx + point.Y * vy;
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            var area = (maxU - minU) * (maxV - minV);
            if (area >= bestArea) continue;

            bestArea = area;
            best = new[]
            {
                new PixelPoint(ux * minU + vx * minV, uy * minU + vy * minV),
                new PixelPoint(ux * maxU + vx * minV, uy * maxU + vy * minV),
                new PixelPoint(ux * maxU + vx * maxV, uy * maxU + vy * maxV),
                new PixelPoint(ux * minU + vx * maxV, uy * minU + vy * maxV)
            };
        }

        return best;
    }

    /// <summary>
    /// Sutherland-Hodgman clip of a polygon to the rectangle (0, 0, width, height).
    /// A concave polygon can come out with overlapping edges along the border;
    /// its area is still right, which is all label export needs.
    /// </summary>
    public static IReadOnlyList<PixelPoint> ClipToRectangle(IReadOnlyList<PixelPoint> polygon, double width, double height)
    {
        var output = polygon;
        output = ClipEdge(output, alongX: true, 0, keepGreater: true);
        output = ClipEdge(output, alongX: true, width, keepGreater: false);
        output = ClipEdge(output, alongX: false, 0, keepGreater: true);
        output = ClipEdge(output, alongX: false, height, keepGreater: false);
        return output;
    }

    private static IReadOnlyList<PixelPoint> ClipEdge(
        IReadOnlyList<PixelPoint> input,
        bool alongX,
        double limit,
        bool keepGreater)
    {
        if (input.Count == 0) return input;

        double Axis(PixelPoint point) => alongX ? point.X : point.Y;
        bool Inside(PixelPoint point) => keepGreater ? Axis(point) >= limit : Axis(point) <= limit;

        var output = new List<PixelPoint>(input.Count + 4);
        var previous = input[^1];
        foreach (var current in input)
        {
            var currentInside = Inside(current);
            if (currentInside != Inside(previous))
            {
                var t = (limit - Axis(previous)) / (Axis(current) - Axis(previous));
                // Landing exactly on the limit avoids a hair of drift that would leave the point just outside.
                output.Add(alongX
                    ? new PixelPoint(limit, previous.Y + (current.Y - previous.Y) * t)
                    : new PixelPoint(previous.X + (current.X - previous.X) * t, limit));
            }

            if (currentInside)
                output.Add(current);
            previous = current;
        }

        return output;
    }

    private static double Cross(PixelPoint origin, PixelPoint first, PixelPoint second) =>
        (first.X - origin.X) * (second.Y - origin.Y) - (first.Y - origin.Y) * (second.X - origin.X);
}
