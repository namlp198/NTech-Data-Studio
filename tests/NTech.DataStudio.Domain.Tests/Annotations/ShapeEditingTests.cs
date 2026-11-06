using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Annotations;

public class ShapeEditingTests
{
    private const double W = 200;
    private const double H = 100;

    private static readonly PixelPoint[] Box =
    {
        new(20, 20), new(60, 20), new(60, 50), new(20, 50)
    };

    private static readonly PixelPoint[] Triangle = { new(10, 10), new(90, 10), new(50, 80) };

    [Fact]
    public void Contains_InsideAndOutside()
    {
        Assert.True(ShapeEditing.Contains(Box, new PixelPoint(40, 30)));
        Assert.False(ShapeEditing.Contains(Box, new PixelPoint(70, 30)));
        Assert.True(ShapeEditing.Contains(Triangle, new PixelPoint(50, 30)));
        Assert.False(ShapeEditing.Contains(Triangle, new PixelPoint(15, 70)));
    }

    [Fact]
    public void DistanceToSegment_UsesTheEndpointBeyondTheSegment()
    {
        var distance = ShapeEditing.DistanceToSegment(new PixelPoint(15, 4), new PixelPoint(0, 0), new PixelPoint(10, 0), out var nearest);

        Assert.Equal(new PixelPoint(10, 0), nearest);
        Assert.Equal(Math.Sqrt(25 + 16), distance, 9);
    }

    [Fact]
    public void HitVertex_PicksTheClosestWithinTolerance()
    {
        Assert.Equal(1, ShapeEditing.HitVertex(Box, new PixelPoint(58, 22), 5));
        Assert.Equal(-1, ShapeEditing.HitVertex(Box, new PixelPoint(40, 35), 5));
    }

    [Fact]
    public void HitEdge_ReturnsTheEdgeAndTheFootPoint()
    {
        var hit = ShapeEditing.HitEdge(Box, new PixelPoint(40, 23), 5);

        Assert.Equal(0, hit!.Value.Edge);
        Assert.Equal(new PixelPoint(40, 20), hit.Value.Point);
        Assert.Null(ShapeEditing.HitEdge(Box, new PixelPoint(40, 35), 5));
    }

    [Fact]
    public void HitEdge_ClosingEdgeCounts()
    {
        Assert.Equal(3, ShapeEditing.HitEdge(Box, new PixelPoint(21, 35), 3)!.Value.Edge);
    }

    [Fact]
    public void HitShape_PointKindUsesAWiderTolerance()
    {
        var point = new[] { new PixelPoint(50, 50) };

        Assert.True(ShapeEditing.HitShape(ShapeKind.Point, point, new PixelPoint(58, 50), 5));
        Assert.False(ShapeEditing.HitShape(ShapeKind.Point, point, new PixelPoint(61, 50), 5));
    }

    [Fact]
    public void HitShape_ThinBoxCanBePickedOnItsOutline()
    {
        var thin = new PixelPoint[] { new(10, 10), new(110, 10), new(110, 11), new(10, 11) };

        Assert.True(ShapeEditing.HitShape(ShapeKind.Box, thin, new PixelPoint(60, 13), 4));
    }

    [Fact]
    public void Translate_MovesEveryPoint()
    {
        var moved = ShapeEditing.Translate(Box, 10, 5, W, H);

        Assert.Equal(new PixelPoint(30, 25), moved[0]);
        Assert.Equal(new PixelPoint(70, 55), moved[2]);
    }

    [Fact]
    public void Translate_StopsAtTheImageEdgeWithoutChangingTheSize()
    {
        var moved = ShapeEditing.Translate(Box, 500, -500, W, H);
        var box = PolygonMath.BoundingBox(moved);

        Assert.Equal((200, 0), (box.Right, box.Y));
        Assert.Equal((40, 30), (box.Width, box.Height));
    }

    [Fact]
    public void MoveVertex_BoxCornerKeepsTheOppositeCornerAndStaysAxisAligned()
    {
        var result = ShapeEditing.MoveVertex(ShapeKind.Box, Box, 2, new PixelPoint(100, 70), W, H)!;

        Assert.Equal(new PixelRect(20, 20, 80, 50), PolygonMath.BoundingBox(result));
        Assert.Equal(4000, PolygonMath.Area(result));
    }

    [Fact]
    public void MoveVertex_BoxDraggedPastTheOppositeCornerFlips()
    {
        var result = ShapeEditing.MoveVertex(ShapeKind.Box, Box, 2, new PixelPoint(5, 5), W, H)!;

        Assert.Equal(new PixelRect(5, 5, 15, 15), PolygonMath.BoundingBox(result));
    }

    [Fact]
    public void MoveVertex_BoxIsClampedAndDegenerateBoxIsRefused()
    {
        var clamped = ShapeEditing.MoveVertex(ShapeKind.Box, Box, 2, new PixelPoint(900, 900), W, H)!;
        Assert.Equal(new PixelRect(20, 20, 180, 80), PolygonMath.BoundingBox(clamped));

        Assert.Null(ShapeEditing.MoveVertex(ShapeKind.Box, Box, 2, new PixelPoint(20, 90), W, H));
    }

    [Fact]
    public void MoveVertex_PolygonMovesOneVertex()
    {
        var result = ShapeEditing.MoveVertex(ShapeKind.Polygon, Triangle, 2, new PixelPoint(40, 90), W, H)!;

        Assert.Equal(new PixelPoint(40, 90), result[2]);
        Assert.Equal(Triangle[0], result[0]);
    }

    [Fact]
    public void MoveVertex_PolygonThatWouldCrossItselfIsRefused()
    {
        PixelPoint[] square = { new(0, 0), new(40, 0), new(40, 40), new(0, 40) };

        // Vertex 1 dragged to (40, 60): the edge from the origin now crosses the bottom edge.
        Assert.Null(ShapeEditing.MoveVertex(ShapeKind.Polygon, square, 1, new PixelPoint(40, 60), W, H));
        Assert.NotNull(ShapeEditing.MoveVertex(ShapeKind.Polygon, square, 1, new PixelPoint(60, 10), W, H));
    }

    [Fact]
    public void MoveVertex_RotatedBoxKeepsItsAngleAndStaysARectangle()
    {
        // A box rotated 45 degrees: axis (50,20)-(80,50), width ~14.14.
        var corners = new RotatedRect(new PixelPoint(50, 20), new PixelPoint(80, 50), 20).Corners();

        var result = ShapeEditing.MoveVertex(ShapeKind.RotatedBox, corners, 1, new PixelPoint(100, 50), W, H)!;

        // Sides stay perpendicular, and the pinned opposite corner did not move.
        Assert.Equal(corners[3], result[3]);
        Assert.Equal(0, Dot(result[1], result[0], result[2]), 6);
        Assert.Equal(0, Dot(result[2], result[1], result[3]), 6);
        Assert.Equal(angle(corners), angle(result), 6);
        Assert.True(PolygonMath.Area(result) > 0);
    }

    [Fact]
    public void MoveVertex_RotatedBoxPulledOutsideShrinksInsteadOfDeforming()
    {
        var corners = new RotatedRect(new PixelPoint(100, 50), new PixelPoint(140, 70), 20).Corners();

        var result = ShapeEditing.MoveVertex(ShapeKind.RotatedBox, corners, 2, new PixelPoint(900, 900), W, H)!;

        Assert.All(result, point =>
        {
            Assert.InRange(point.X, -1e-6, W + 1e-6);
            Assert.InRange(point.Y, -1e-6, H + 1e-6);
        });
        Assert.Equal(0, Dot(result[1], result[0], result[2]), 6);
        Assert.Equal(corners[0], result[0]);
    }

    [Fact]
    public void MoveVertex_PointJustMoves()
    {
        var result = ShapeEditing.MoveVertex(ShapeKind.Point, new[] { new PixelPoint(5, 5) }, 0, new PixelPoint(-9, 40), W, H)!;

        Assert.Equal(new[] { new PixelPoint(0, 40) }, result);
    }

    [Fact]
    public void MoveVertex_BadIndexOrNaNIsRefused()
    {
        Assert.Null(ShapeEditing.MoveVertex(ShapeKind.Box, Box, 7, new PixelPoint(1, 1), W, H));
        Assert.Null(ShapeEditing.MoveVertex(ShapeKind.Box, Box, 1, new PixelPoint(double.NaN, 1), W, H));
    }

    [Fact]
    public void InsertVertex_GoesAfterTheEdgeStart()
    {
        var result = ShapeEditing.InsertVertex(Triangle, 0, new PixelPoint(50, 10));

        Assert.Equal(4, result.Count);
        Assert.Equal(new PixelPoint(50, 10), result[1]);
        Assert.Equal(Triangle[1], result[2]);
    }

    // Dot product of the vectors at a corner: 0 for a right angle.
    private static double Dot(PixelPoint corner, PixelPoint a, PixelPoint b) =>
        (a.X - corner.X) * (b.X - corner.X) + (a.Y - corner.Y) * (b.Y - corner.Y);

    private static double angle(IReadOnlyList<PixelPoint> points) =>
        Math.Atan2(points[1].Y - points[0].Y, points[1].X - points[0].X);
}
