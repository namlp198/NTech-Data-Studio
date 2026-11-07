using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Geometry;

public class ConvexGeometryTests
{
    private static readonly PixelPoint[] Square =
    {
        new(0, 0), new(100, 0), new(100, 100), new(0, 100)
    };

    private static IReadOnlyList<PixelPoint> RotatedRectangle(double angleDegrees, double width, double height)
    {
        var rotation = Affine2D.About(Affine2D.Rotation(angleDegrees * Math.PI / 180), width / 2, height / 2)
            .Then(Affine2D.Translation(200, 200));
        return new PixelRect(0, 0, width, height).Corners().Select(rotation.Apply).ToList();
    }

    [Fact]
    public void Hull_DropsInteriorAndCollinearPoints()
    {
        var points = Square.Concat(new[] { new PixelPoint(50, 50), new PixelPoint(50, 0), new PixelPoint(100, 100) });

        Assert.Equal(4, ConvexGeometry.Hull(points).Count);
    }

    [Fact]
    public void MinAreaRectangle_RecoversARotatedRectangleExactly()
    {
        var corners = RotatedRectangle(30, 100, 40);

        var fitted = ConvexGeometry.MinAreaRectangle(corners);

        Assert.Equal(4, fitted.Count);
        Assert.Equal(4000, PolygonMath.Area(fitted), 3);
        Assert.All(corners, corner => Assert.Contains(fitted, other => other.DistanceTo(corner) < 1e-6));
    }

    [Fact]
    public void MinAreaRectangle_EnclosesEveryInputPoint()
    {
        var points = new[] { new PixelPoint(0, 0), new PixelPoint(80, 10), new PixelPoint(60, 70), new PixelPoint(5, 50) };

        var fitted = ConvexGeometry.MinAreaRectangle(points);

        Assert.True(PolygonMath.Area(fitted) >= PolygonMath.Area(ConvexGeometry.Hull(points)));
        // Every point lies inside or on the rectangle: the clip of the rectangle by itself keeps its area.
        var edgeAlignedArea = PolygonMath.Area(fitted);
        Assert.Equal(edgeAlignedArea, PolygonMath.Area(ConvexGeometry.Hull(fitted.Concat(points))), 6);
    }

    [Fact]
    public void MinAreaRectangle_IsEmptyWhenThePointsHaveNoArea()
    {
        Assert.Empty(ConvexGeometry.MinAreaRectangle(new[] { new PixelPoint(0, 0), new PixelPoint(5, 5), new PixelPoint(10, 10) }));
    }

    [Fact]
    public void ClipToRectangle_KeepsAPolygonThatIsInside()
    {
        var clipped = ConvexGeometry.ClipToRectangle(Square, 200, 200);

        Assert.Equal(10000, PolygonMath.Area(clipped), 6);
    }

    [Fact]
    public void ClipToRectangle_CutsOffThePartOutside()
    {
        var shifted = Square.Select(point => new PixelPoint(point.X + 50, point.Y)).ToList();

        var clipped = ConvexGeometry.ClipToRectangle(shifted, 100, 100);

        Assert.Equal(5000, PolygonMath.Area(clipped), 6);
        Assert.All(clipped, point => Assert.InRange(point.X, 0, 100));
    }

    [Fact]
    public void ClipToRectangle_ReturnsNothingForAPolygonOutside()
    {
        var far = Square.Select(point => new PixelPoint(point.X + 500, point.Y)).ToList();

        Assert.Empty(ConvexGeometry.ClipToRectangle(far, 100, 100));
    }

    [Fact]
    public void ClipToRectangle_CutsACornerOfATriangle()
    {
        var triangle = new[] { new PixelPoint(-50, -50), new PixelPoint(150, -50), new PixelPoint(-50, 150) };

        var clipped = ConvexGeometry.ClipToRectangle(triangle, 100, 100);

        // The hypotenuse runs x + y = 100: the whole lower-left half of the 100 x 100 box is inside.
        Assert.Equal(5000, PolygonMath.Area(clipped), 6);
    }
}
