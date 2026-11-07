using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Geometry;

public class Affine2DTests
{
    private static void AssertPoint(double x, double y, PixelPoint actual)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
    }

    [Fact]
    public void Then_AppliesTheFirstMapBeforeTheSecond()
    {
        var map = Affine2D.Translation(10, 0).Then(Affine2D.Scale(2, 2));

        AssertPoint(22, 2, map.Apply(new PixelPoint(1, 1)));
    }

    [Fact]
    public void Invert_UndoesAnyMixOfTheBuildingBlocks()
    {
        var map = Affine2D.About(Affine2D.Scale(1.3, 0.8).Then(Affine2D.Shear(0.1, 0.05)).Then(Affine2D.Rotation(0.7)), 50, 30)
            .Then(Affine2D.Translation(7, -3));
        var point = new PixelPoint(12.5, 88);

        AssertPoint(point.X, point.Y, map.Invert().Apply(map.Apply(point)));
    }

    [Fact]
    public void Invert_RefusesAMapThatCollapsesThePlane()
    {
        Assert.Throws<InvalidOperationException>(() => Affine2D.Scale(0, 1).Invert());
    }

    [Fact]
    public void FlipHorizontal_MirrorsInsideTheFrame()
    {
        AssertPoint(90, 5, Affine2D.FlipHorizontal(100).Apply(new PixelPoint(10, 5)));
    }

    [Fact]
    public void QuarterTurnClockwise_SendsTheTopLeftCornerToTheTopRight()
    {
        // A 100 wide, 50 tall frame becomes 50 wide, 100 tall.
        var turn = Affine2D.QuarterTurnClockwise(50);

        AssertPoint(50, 0, turn.Apply(new PixelPoint(0, 0)));
        AssertPoint(50, 100, turn.Apply(new PixelPoint(100, 0)));
        AssertPoint(0, 100, turn.Apply(new PixelPoint(100, 50)));
        AssertPoint(0, 0, turn.Apply(new PixelPoint(0, 50)));
    }

    [Fact]
    public void Rotation_IsClockwiseOnScreen()
    {
        // Y points down: a point to the right of the origin swings down.
        var point = Affine2D.Rotation(Math.PI / 2).Apply(new PixelPoint(1, 0));

        AssertPoint(0, 1, point);
    }

    [Fact]
    public void KeepsAxes_IsTrueForFlipsTurnsAndScalesButNotForSlightRotation()
    {
        Assert.True(Affine2D.FlipHorizontal(10).KeepsAxes);
        Assert.True(Affine2D.QuarterTurnClockwise(10).KeepsAxes);
        Assert.True(Affine2D.Scale(2, 3).Then(Affine2D.Translation(4, 4)).KeepsAxes);
        Assert.False(Affine2D.Rotation(0.1).KeepsAxes);
    }

    [Fact]
    public void IsSimilarity_AllowsRotationMirrorAndUniformScaleOnly()
    {
        Assert.True(Affine2D.Rotation(0.4).Then(Affine2D.Scale(2, 2)).IsSimilarity);
        Assert.True(Affine2D.FlipVertical(10).Then(Affine2D.Rotation(1)).IsSimilarity);
        Assert.False(Affine2D.Scale(2, 1).IsSimilarity);
        Assert.False(Affine2D.Shear(0.2, 0).IsSimilarity);
    }

    [Fact]
    public void ToPixelCenters_MakesAFlipLandOnTheMirroredPixel()
    {
        // In edge coordinates pixel 0 spans 0..1 and pixel 9 spans 9..10. OpenCV addresses
        // pixels by centre, so the flip of a 10 pixel row must send index 0 to index 9.
        var flip = Affine2D.FlipHorizontal(10).ToPixelCenters();

        AssertPoint(9, 0, flip.Apply(new PixelPoint(0, 0)));
        AssertPoint(0, 0, flip.Apply(new PixelPoint(9, 0)));
    }

    [Fact]
    public void BuildGeometry_QuarterTurnSwapsTheFrame()
    {
        var geometry = new AugmentParams { QuarterTurns = 1 }.BuildGeometry(100, 50, out var width, out var height);

        Assert.Equal((50, 100), (width, height));
        AssertPoint(50, 0, geometry.Apply(new PixelPoint(0, 0)));
    }

    [Fact]
    public void BuildGeometry_FourQuarterTurnsAreTheIdentity()
    {
        var geometry = new AugmentParams { QuarterTurns = 4 }.BuildGeometry(100, 50, out var width, out var height);

        Assert.Equal((100, 50), (width, height));
        AssertPoint(37, 11, geometry.Apply(new PixelPoint(37, 11)));
    }

    [Fact]
    public void BuildGeometry_SmallRotationKeepsTheCentreFixed()
    {
        var geometry = new AugmentParams { RotateDegrees = 23, Scale = 1.2 }.BuildGeometry(200, 100, out _, out _);

        AssertPoint(100, 50, geometry.Apply(new PixelPoint(100, 50)));
    }
}
