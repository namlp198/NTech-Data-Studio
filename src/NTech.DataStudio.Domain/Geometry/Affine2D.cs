namespace NTech.DataStudio.Domain.Geometry;

/// <summary>
/// A 2D affine map in edge coordinates (0..width, 0..height), the same system
/// labels use: x' = M11*x + M12*y + Dx, y' = M21*x + M22*y + Dy. Y points down,
/// so a positive rotation turns the picture clockwise on screen.
/// </summary>
public readonly record struct Affine2D(double M11, double M12, double M21, double M22, double Dx, double Dy)
{
    private const double Epsilon = 1e-9;

    public static Affine2D Identity => new(1, 0, 0, 1, 0, 0);

    public static Affine2D Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    public static Affine2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Affine2D Rotation(double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Affine2D(cos, -sin, sin, cos, 0, 0);
    }

    /// <summary>x' = x + tan(x angle) * y, y' = y + tan(y angle) * x.</summary>
    public static Affine2D Shear(double xRadians, double yRadians) =>
        new(1, Math.Tan(xRadians), Math.Tan(yRadians), 1, 0, 0);

    /// <summary>The flip that mirrors left and right inside a frame of <paramref name="width"/>.</summary>
    public static Affine2D FlipHorizontal(double width) => new(-1, 0, 0, 1, width, 0);

    public static Affine2D FlipVertical(double height) => new(1, 0, 0, -1, 0, height);

    /// <summary>
    /// A quarter turn clockwise of a frame <paramref name="height"/> tall; the
    /// result frame is as wide as that height and as tall as the old width.
    /// </summary>
    public static Affine2D QuarterTurnClockwise(double height) => new(0, -1, 1, 0, height, 0);

    /// <summary>Applies <paramref name="linear"/> around a center instead of the origin.</summary>
    public static Affine2D About(Affine2D linear, double centerX, double centerY) =>
        Translation(-centerX, -centerY).Then(linear).Then(Translation(centerX, centerY));

    public double Determinant => M11 * M22 - M12 * M21;

    /// <summary>The map that does this one first and then <paramref name="next"/>.</summary>
    public Affine2D Then(Affine2D next) => new(
        next.M11 * M11 + next.M12 * M21,
        next.M11 * M12 + next.M12 * M22,
        next.M21 * M11 + next.M22 * M21,
        next.M21 * M12 + next.M22 * M22,
        next.M11 * Dx + next.M12 * Dy + next.Dx,
        next.M21 * Dx + next.M22 * Dy + next.Dy);

    public PixelPoint Apply(PixelPoint point) => new(
        M11 * point.X + M12 * point.Y + Dx,
        M21 * point.X + M22 * point.Y + Dy);

    /// <exception cref="InvalidOperationException">The map collapses the plane and has no inverse.</exception>
    public Affine2D Invert()
    {
        var determinant = Determinant;
        if (Math.Abs(determinant) < Epsilon)
            throw new InvalidOperationException("Phép biến đổi suy biến, không có phép nghịch đảo.");

        var m11 = M22 / determinant;
        var m12 = -M12 / determinant;
        var m21 = -M21 / determinant;
        var m22 = M11 / determinant;
        return new Affine2D(m11, m12, m21, m22, -(m11 * Dx + m12 * Dy), -(m21 * Dx + m22 * Dy));
    }

    /// <summary>Linear scale of the map: the square root of the area factor.</summary>
    public double MeanScale => Math.Sqrt(Math.Abs(Determinant));

    /// <summary>True when horizontal and vertical lines stay horizontal and vertical (flips, quarter turns, scales, shifts).</summary>
    public bool KeepsAxes =>
        (Math.Abs(M12) < Epsilon && Math.Abs(M21) < Epsilon)
        || (Math.Abs(M11) < Epsilon && Math.Abs(M22) < Epsilon);

    /// <summary>True when angles survive: any mix of rotation, mirror, uniform scale and shift. A rectangle stays a rectangle.</summary>
    public bool IsSimilarity
    {
        get
        {
            var scaleSquaredFirst = M11 * M11 + M21 * M21;
            var scaleSquaredSecond = M12 * M12 + M22 * M22;
            var tolerance = 1e-9 * Math.Max(1, Math.Max(scaleSquaredFirst, scaleSquaredSecond));
            return Math.Abs(scaleSquaredFirst - scaleSquaredSecond) < tolerance
                && Math.Abs(M11 * M12 + M21 * M22) < tolerance;
        }
    }

    /// <summary>
    /// The same map for OpenCV, whose pixel (0, 0) is a centre at 0.5 in edge
    /// coordinates: shift in by half a pixel, apply, shift out again.
    /// </summary>
    public Affine2D ToPixelCenters() => Translation(0.5, 0.5).Then(this).Then(Translation(-0.5, -0.5));
}
