using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Annotations;

public class AnnotationDrawingSessionTests
{
    private const double ImageWidth = 200;
    private const double ImageHeight = 100;

    private static AnnotationDrawingSession Start(ShapeKind kind, double x = 10, double y = 10) =>
        new(kind, new PixelPoint(x, y), ImageWidth, ImageHeight);

    [Fact]
    public void Box_FinalPointsAreTheFourCorners()
    {
        var session = Start(ShapeKind.Box);
        session.UpdateCandidate(new PixelPoint(30, 40));

        Assert.True(session.CanFinish);
        Assert.Equal(new[]
        {
            new PixelPoint(10, 10), new PixelPoint(30, 10), new PixelPoint(30, 40), new PixelPoint(10, 40)
        }, session.FinalPoints);
    }

    [Fact]
    public void Box_DraggedUpLeftIsNormalized()
    {
        var session = Start(ShapeKind.Box, 30, 40);
        session.UpdateCandidate(new PixelPoint(10, 10));

        Assert.Equal(new PixelPoint(10, 10), session.FinalPoints[0]);
    }

    [Fact]
    public void Box_WithoutAreaCannotFinish()
    {
        var session = Start(ShapeKind.Box);
        Assert.False(session.CanFinish);

        session.UpdateCandidate(new PixelPoint(50, 10));
        Assert.False(session.CanFinish);
    }

    [Fact]
    public void Box_IsClampedInsideTheImage()
    {
        var session = Start(ShapeKind.Box, -20, -5);
        session.UpdateCandidate(new PixelPoint(500, 500));

        Assert.Equal(new PixelRect(0, 0, ImageWidth, ImageHeight), PolygonMath.BoundingBox(session.FinalPoints));
    }

    [Fact]
    public void Polygon_NeedsThreeVertices()
    {
        var session = Start(ShapeKind.Polygon);
        session.UpdateCandidate(new PixelPoint(50, 10));
        Assert.True(session.AddCandidateAsVertex());
        Assert.False(session.CanFinish);

        session.UpdateCandidate(new PixelPoint(50, 50));
        Assert.True(session.AddCandidateAsVertex());
        Assert.True(session.CanFinish);
        Assert.Equal(3, session.FinalPoints.Count);
    }

    [Fact]
    public void Polygon_CandidateOnTheLastVertexIsNotAdded()
    {
        var session = Start(ShapeKind.Polygon);

        Assert.False(session.AddCandidateAsVertex());
        Assert.Single(session.Vertices);
    }

    [Fact]
    public void Polygon_SelfIntersectingCannotFinish()
    {
        var session = Start(ShapeKind.Polygon, 0, 0);
        foreach (var point in new[] { new PixelPoint(50, 50), new PixelPoint(50, 0), new PixelPoint(0, 50) })
        {
            session.UpdateCandidate(point);
            session.AddCandidateAsVertex();
        }

        Assert.True(session.IsSelfIntersecting);
        Assert.False(session.CanFinish);
    }

    [Fact]
    public void Polygon_UndoRemovesTheLastVertexAndKeepsTheFirst()
    {
        var session = Start(ShapeKind.Polygon);
        session.UpdateCandidate(new PixelPoint(50, 10));
        session.AddCandidateAsVertex();

        Assert.True(session.UndoLastVertex());
        Assert.Single(session.Vertices);
        Assert.Equal(new PixelPoint(10, 10), session.Candidate);
        Assert.False(session.UndoLastVertex());
    }

    [Fact]
    public void Polygon_PreviewIncludesTheCandidate()
    {
        var session = Start(ShapeKind.Polygon);
        session.UpdateCandidate(new PixelPoint(50, 10));

        Assert.Equal(2, session.PreviewPoints.Count);
        Assert.Single(session.FinalPoints);
    }

    [Fact]
    public void RotatedBox_CannotFinishUntilAxisAndWidthAreSet()
    {
        var session = Start(ShapeKind.RotatedBox, 20, 50);
        session.UpdateCandidate(new PixelPoint(120, 50));
        Assert.False(session.CanFinish);

        Assert.True(session.AddCandidateAsVertex());
        Assert.False(session.CanFinish);

        session.UpdateCandidate(new PixelPoint(70, 60));
        Assert.True(session.CanFinish);
        Assert.Equal(100 * 20, PolygonMath.Area(session.FinalPoints), 6);
    }

    [Fact]
    public void RotatedBox_TakesOnlyAxisVertices()
    {
        var session = Start(ShapeKind.RotatedBox, 20, 50);
        session.UpdateCandidate(new PixelPoint(120, 50));
        session.AddCandidateAsVertex();
        session.UpdateCandidate(new PixelPoint(70, 60));

        Assert.False(session.AddCandidateAsVertex());
        Assert.Equal(2, session.Vertices.Count);
    }

    [Fact]
    public void RotatedBox_WidthShrinksToStayInsideTheImageAndRemainARectangle()
    {
        var session = Start(ShapeKind.RotatedBox, 20, 5);
        session.UpdateCandidate(new PixelPoint(120, 5));
        session.AddCandidateAsVertex();
        session.UpdateCandidate(new PixelPoint(70, 90));

        Assert.True(session.CanFinish);
        Assert.Equal(100 * 10, PolygonMath.Area(session.FinalPoints), 6);
        Assert.All(session.FinalPoints, corner => Assert.InRange(corner.Y, 0, ImageHeight));
    }

    [Fact]
    public void RotatedBox_UndoReturnsToAxisPicking()
    {
        var session = Start(ShapeKind.RotatedBox, 20, 50);
        session.UpdateCandidate(new PixelPoint(120, 50));
        session.AddCandidateAsVertex();

        Assert.True(session.UndoLastVertex());
        Assert.Single(session.Vertices);
        Assert.Empty(session.FinalPoints);
    }

    [Fact]
    public void Point_FinishesImmediatelyAndHasNoVertices()
    {
        var session = Start(ShapeKind.Point);

        Assert.True(session.CanFinish);
        Assert.False(session.AddCandidateAsVertex());
        Assert.Equal(new[] { new PixelPoint(10, 10) }, session.FinalPoints);
    }

    [Fact]
    public void UpdateCandidate_IgnoresNonFinitePoints()
    {
        var session = Start(ShapeKind.Box);
        session.UpdateCandidate(new PixelPoint(double.NaN, 3));

        Assert.Equal(new PixelPoint(10, 10), session.Candidate);
    }

    [Fact]
    public void Constructor_RejectsEmptyImage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnnotationDrawingSession(ShapeKind.Box, new PixelPoint(0, 0), 0, 100));
    }

    [Theory]
    [InlineData(ShapeKind.Box, 2)]
    [InlineData(ShapeKind.RotatedBox, 3)]
    [InlineData(ShapeKind.Polygon, 2)]
    [InlineData(ShapeKind.Point, 2)]
    public void Annotation_RejectsWrongPointCount(ShapeKind kind, int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new PixelPoint(i, i)).ToArray();

        Assert.Throws<ArgumentException>(() => new Annotation(
            Guid.NewGuid(), 1, 1, kind, points, AnnotationSource.Manual, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
    }
}
