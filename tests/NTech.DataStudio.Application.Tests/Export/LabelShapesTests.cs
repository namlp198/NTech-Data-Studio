using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Tests.Export;

public class LabelShapesTests
{
    private static RenderedLabel Label(ShapeKind kind, params (double X, double Y)[] points) =>
        new(Guid.NewGuid(), 1, kind, points.Select(point => new PixelPoint(point.X, point.Y)).ToList());

    private static RenderedLabel Rotated(double centreX, double centreY, double length, double thickness, double degrees)
    {
        var angle = degrees * Math.PI / 180;
        var (sin, cos) = (Math.Sin(angle), Math.Cos(angle));
        var corners = new[] { (-length, -thickness), (length, -thickness), (length, thickness), (-length, thickness) }
            .Select(corner => (centreX + corner.Item1 * cos - corner.Item2 * sin, centreY + corner.Item1 * sin + corner.Item2 * cos))
            .ToArray();
        return Label(ShapeKind.RotatedBox, corners);
    }

    private static double Dot(PixelPoint from, PixelPoint toA, PixelPoint toB) =>
        (toA.X - from.X) * (toB.X - from.X) + (toA.Y - from.Y) * (toB.Y - from.Y);

    [Fact]
    public void Box_IsTheBoxAroundTheLabelKeptInsideThePicture()
    {
        var label = Label(ShapeKind.RotatedBox, (-5, 10), (30, 5), (35, 40), (0, 45));

        var box = LabelShapes.Box(label, 100, 50);

        Assert.NotNull(box);
        Assert.Equal(0, box.Value.X);
        Assert.Equal(5, box.Value.Y);
        Assert.Equal(35, box.Value.Right);
        Assert.Equal(45, box.Value.Bottom);
    }

    [Fact]
    public void PointsAndFlatLabelsHaveNoShape()
    {
        var point = Label(ShapeKind.Point, (5, 5));
        var flat = Label(ShapeKind.Box, (10, 10), (10, 10), (10, 10), (10, 10));

        Assert.Null(LabelShapes.Box(point, 100, 100));
        Assert.Null(LabelShapes.Polygon(point, 100, 100));
        Assert.Null(LabelShapes.Oriented(point, 100, 100));
        Assert.Null(LabelShapes.Box(flat, 100, 100));
        Assert.Null(LabelShapes.Polygon(flat, 100, 100));
    }

    [Fact]
    public void Oriented_ARotatedBoxInsideThePictureKeepsItsCorners()
    {
        var label = Rotated(50, 50, 20, 8, 30);

        var corners = LabelShapes.Oriented(label, 100, 100);

        Assert.NotNull(corners);
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(label.Points[index].X, corners[index].X, 9);
            Assert.Equal(label.Points[index].Y, corners[index].Y, 9);
        }
    }

    [Fact]
    public void Oriented_ARotatedBoxThatPokesOutIsShrunkToFit_AndStaysARectangle()
    {
        // Centre near the left border; the long axis reaches about 15 px past it.
        var label = Rotated(10, 50, 25, 6, 10);
        Assert.Contains(label.Points, point => point.X < 0);

        var corners = LabelShapes.Oriented(label, 100, 100);

        Assert.NotNull(corners);
        Assert.All(corners, point =>
        {
            Assert.InRange(point.X, 0, 100);
            Assert.InRange(point.Y, 0, 100);
        });
        // Still a rectangle: right angles at every corner, which clamping the corners would have broken.
        for (var index = 0; index < 4; index++)
        {
            var corner = corners[index];
            Assert.Equal(0, Dot(corner, corners[(index + 1) % 4], corners[(index + 3) % 4]), 6);
        }

        Assert.True(PolygonMath.Area(corners) < PolygonMath.Area(label.Points));
    }

    [Fact]
    public void Oriented_WithTheCentreOutsideThePictureThereIsNothingToKeep()
    {
        var label = Rotated(-30, 50, 10, 5, 0);

        Assert.Null(LabelShapes.Oriented(label, 100, 100));
    }

    [Fact]
    public void Oriented_APolygonBecomesItsSmallestEnclosingRectangle()
    {
        var label = Label(ShapeKind.Polygon, (10, 10), (50, 10), (30, 40));

        var corners = LabelShapes.Oriented(label, 100, 100);

        Assert.NotNull(corners);
        Assert.Equal(4, corners.Count);
        Assert.Equal(40 * 30, PolygonMath.Area(corners), 6);
    }

    [Fact]
    public void Polygon_OfABoxIsItsFourCorners()
    {
        var label = Label(ShapeKind.Box, (10, 10), (30, 10), (30, 20), (10, 20));

        var polygon = LabelShapes.Polygon(label, 100, 100);

        Assert.Equal(4, polygon!.Count);
        Assert.Equal(200, PolygonMath.Area(polygon), 6);
    }
}
