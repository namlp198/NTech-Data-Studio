namespace NTech.DataStudio.Domain.Geometry;

/// <summary>
/// A rectangle described by its main axis (the center line along its length)
/// and its full width across that axis.
/// </summary>
public readonly record struct RotatedRect(PixelPoint AxisStart, PixelPoint AxisEnd, double Width)
{
    public double Length => AxisStart.DistanceTo(AxisEnd);

    /// <summary>
    /// Corners in drawing order; empty when the axis has no length, because
    /// such an axis has no direction to be wide across.
    /// </summary>
    public IReadOnlyList<PixelPoint> Corners()
    {
        if (!TryGetUnitNormal(AxisStart, AxisEnd, out var normalX, out var normalY))
            return Array.Empty<PixelPoint>();

        var halfWidth = Width / 2;
        var offsetX = normalX * halfWidth;
        var offsetY = normalY * halfWidth;
        return new[]
        {
            new PixelPoint(AxisStart.X + offsetX, AxisStart.Y + offsetY),
            new PixelPoint(AxisEnd.X + offsetX, AxisEnd.Y + offsetY),
            new PixelPoint(AxisEnd.X - offsetX, AxisEnd.Y - offsetY),
            new PixelPoint(AxisStart.X - offsetX, AxisStart.Y - offsetY)
        };
    }

    /// <summary>Full width implied by a pointer at <paramref name="point"/>: twice its distance from the axis line.</summary>
    public static double WidthThrough(PixelPoint axisStart, PixelPoint axisEnd, PixelPoint point)
    {
        if (!TryGetUnitNormal(axisStart, axisEnd, out var normalX, out var normalY))
            return 0;

        var distance = (point.X - axisStart.X) * normalX + (point.Y - axisStart.Y) * normalY;
        return 2 * Math.Abs(distance);
    }

    /// <summary>
    /// The widest rectangle on this axis whose corners all stay inside the
    /// image. Shrinking the width keeps it a true rectangle, which clamping
    /// each corner separately would not.
    /// </summary>
    public static double MaxWidthInside(PixelPoint axisStart, PixelPoint axisEnd, double imageWidth, double imageHeight)
    {
        if (!TryGetUnitNormal(axisStart, axisEnd, out var normalX, out var normalY))
            return 0;

        var halfWidth = double.PositiveInfinity;
        foreach (var point in new[] { axisStart, axisEnd })
        {
            halfWidth = Math.Min(halfWidth, ReachAlong(point.X, normalX, imageWidth));
            halfWidth = Math.Min(halfWidth, ReachAlong(point.Y, normalY, imageHeight));
        }

        return 2 * halfWidth;
    }

    // The rectangle extends both ways along the normal, so the nearer image edge limits it.
    private static double ReachAlong(double coordinate, double normalComponent, double size)
    {
        var magnitude = Math.Abs(normalComponent);
        return magnitude < 1e-12
            ? double.PositiveInfinity
            : Math.Min(coordinate, size - coordinate) / magnitude;
    }

    private static bool TryGetUnitNormal(PixelPoint start, PixelPoint end, out double normalX, out double normalY)
    {
        var length = start.DistanceTo(end);
        if (length < 1e-9)
        {
            normalX = normalY = 0;
            return false;
        }

        normalX = -(end.Y - start.Y) / length;
        normalY = (end.X - start.X) / length;
        return true;
    }
}
