namespace NTech.DataStudio.Domain.Geometry;

/// <summary>An axis-aligned rectangle in master-image pixels.</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double Area => Width * Height;

    public static PixelRect FromCorners(PixelPoint first, PixelPoint opposite) => new(
        Math.Min(first.X, opposite.X),
        Math.Min(first.Y, opposite.Y),
        Math.Abs(opposite.X - first.X),
        Math.Abs(opposite.Y - first.Y));

    public IReadOnlyList<PixelPoint> Corners() => new[]
    {
        new PixelPoint(X, Y),
        new PixelPoint(Right, Y),
        new PixelPoint(Right, Bottom),
        new PixelPoint(X, Bottom)
    };
}
