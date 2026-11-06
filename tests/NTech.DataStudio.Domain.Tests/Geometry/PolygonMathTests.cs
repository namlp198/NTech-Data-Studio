using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Geometry;

public class PolygonMathTests
{
    private static readonly PixelPoint[] Square =
    {
        new(0, 0), new(10, 0), new(10, 10), new(0, 10)
    };

    [Fact]
    public void Area_IsIndependentOfOrientation()
    {
        Assert.Equal(100, PolygonMath.Area(Square));
        Assert.Equal(100, PolygonMath.Area(Square.Reverse().ToArray()));
    }

    [Fact]
    public void Area_OfFewerThanThreePointsIsZero()
    {
        Assert.Equal(0, PolygonMath.Area(new[] { new PixelPoint(0, 0), new PixelPoint(5, 5) }));
    }

    [Fact]
    public void BoundingBox_EnclosesAllPoints()
    {
        var box = PolygonMath.BoundingBox(new[] { new PixelPoint(3, 7), new PixelPoint(-1, 2), new PixelPoint(5, 4) });

        Assert.Equal(new PixelRect(-1, 2, 6, 5), box);
    }

    [Fact]
    public void HasSelfIntersection_FalseForConvexPolygon()
    {
        Assert.False(PolygonMath.HasSelfIntersection(Square, closed: true));
    }

    [Fact]
    public void HasSelfIntersection_TrueForBowTie()
    {
        var bowTie = new[] { new PixelPoint(0, 0), new PixelPoint(10, 10), new PixelPoint(10, 0), new PixelPoint(0, 10) };

        Assert.True(PolygonMath.HasSelfIntersection(bowTie, closed: true));
    }

    [Fact]
    public void HasSelfIntersection_ClosingSegmentCountsOnlyWhenClosed()
    {
        // The crossing is between the middle diagonal and the segment that closes the ring.
        var zigzag = new[] { new PixelPoint(0, 0), new PixelPoint(10, 0), new PixelPoint(0, 10), new PixelPoint(10, 10) };

        Assert.False(PolygonMath.HasSelfIntersection(zigzag, closed: false));
        Assert.True(PolygonMath.HasSelfIntersection(zigzag, closed: true));
    }

    [Fact]
    public void HasSelfIntersection_TrueWhenVertexTouchesAnotherSegment()
    {
        var touching = new[]
        {
            new PixelPoint(0, 0), new PixelPoint(10, 0), new PixelPoint(10, 10),
            new PixelPoint(5, 0), new PixelPoint(0, 10)
        };

        Assert.True(PolygonMath.HasSelfIntersection(touching, closed: true));
    }

    [Fact]
    public void HasSelfIntersection_FalseForTriangleBecauseAllSegmentsAreAdjacent()
    {
        var triangle = new[] { new PixelPoint(0, 0), new PixelPoint(10, 0), new PixelPoint(5, 10) };

        Assert.False(PolygonMath.HasSelfIntersection(triangle, closed: true));
    }
}
