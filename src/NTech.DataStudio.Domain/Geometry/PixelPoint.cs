namespace NTech.DataStudio.Domain.Geometry;

/// <summary>A point in master-image pixels: origin top-left, X right, Y down.</summary>
public readonly record struct PixelPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public double DistanceTo(PixelPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public PixelPoint ClampTo(double imageWidth, double imageHeight) =>
        new(Math.Clamp(X, 0, imageWidth), Math.Clamp(Y, 0, imageHeight));
}
