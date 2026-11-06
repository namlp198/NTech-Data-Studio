using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Geometry;

public class RotatedRectTests
{
    [Fact]
    public void Corners_OfHorizontalAxisAreOffsetAcrossIt()
    {
        var corners = new RotatedRect(new PixelPoint(10, 20), new PixelPoint(30, 20), 8).Corners();

        Assert.Equal(new[]
        {
            new PixelPoint(10, 24), new PixelPoint(30, 24), new PixelPoint(30, 16), new PixelPoint(10, 16)
        }, corners);
    }

    [Fact]
    public void Corners_OfDiagonalAxisFormARectangleOfLengthTimesWidth()
    {
        var rect = new RotatedRect(new PixelPoint(10, 10), new PixelPoint(40, 50), 20);

        Assert.Equal(rect.Length * 20, PolygonMath.Area(rect.Corners()), 6);
    }

    [Fact]
    public void Corners_EmptyWhenAxisHasNoLength()
    {
        Assert.Empty(new RotatedRect(new PixelPoint(5, 5), new PixelPoint(5, 5), 10).Corners());
    }

    [Fact]
    public void WidthThrough_IsTwiceTheDistanceFromTheAxis()
    {
        var width = RotatedRect.WidthThrough(new PixelPoint(0, 0), new PixelPoint(10, 0), new PixelPoint(3, -4));

        Assert.Equal(8, width);
    }

    [Fact]
    public void MaxWidthInside_IsLimitedByTheNearestImageEdge()
    {
        var width = RotatedRect.MaxWidthInside(new PixelPoint(10, 3), new PixelPoint(50, 3), 100, 100);

        Assert.Equal(6, width);
    }

    [Fact]
    public void MaxWidthInside_KeepsDiagonalCornersInsideTheImage()
    {
        var start = new PixelPoint(20, 20);
        var end = new PixelPoint(70, 60);

        var width = RotatedRect.MaxWidthInside(start, end, 100, 100);

        var corners = new RotatedRect(start, end, width).Corners();
        Assert.All(corners, corner =>
        {
            Assert.InRange(corner.X, -1e-9, 100 + 1e-9);
            Assert.InRange(corner.Y, -1e-9, 100 + 1e-9);
        });
        Assert.True(width > 0);
    }
}
