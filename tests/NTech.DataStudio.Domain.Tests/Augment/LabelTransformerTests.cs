using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Tests.Augment;

public class LabelTransformerTests
{
    private const int Width = 200;
    private const int Height = 100;

    private static readonly DateTimeOffset Now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

    private static Annotation Make(ShapeKind kind, IReadOnlyList<PixelPoint> points, long classId = 1) =>
        new(Guid.NewGuid(), 1, classId, kind, points, AnnotationSource.Manual, Now, Now);

    private static Annotation Box(double left, double top, double right, double bottom, long classId = 1) =>
        Make(ShapeKind.Box, new PixelRect(left, top, right - left, bottom - top).Corners(), classId);

    private static RenderPlan Plan(AugmentParams augment) =>
        RenderPlanner.Plan(Width, Height, new PreprocessConfig(), augment);

    private static IReadOnlyList<RenderedLabel> Run(Annotation annotation, AugmentParams augment, LabelTransformOptions? options = null) =>
        LabelTransformer.Transform(new[] { annotation }, Plan(augment), options ?? new LabelTransformOptions());

    private static PixelRect Bounds(RenderedLabel label) => PolygonMath.BoundingBox(label.Points);

    [Fact]
    public void FlipHorizontal_MirrorsABoxAcrossTheCentre()
    {
        var label = Assert.Single(Run(Box(20, 10, 60, 30), new AugmentParams { FlipHorizontal = true }));

        var bounds = Bounds(label);
        Assert.Equal(140, bounds.X, 6);
        Assert.Equal(10, bounds.Y, 6);
        Assert.Equal(40, bounds.Width, 6);
        Assert.Equal(20, bounds.Height, 6);
    }

    [Fact]
    public void FlipVertical_MirrorsABoxTopToBottom()
    {
        var label = Assert.Single(Run(Box(20, 10, 60, 30), new AugmentParams { FlipVertical = true }));

        var bounds = Bounds(label);
        Assert.Equal(20, bounds.X, 6);
        Assert.Equal(70, bounds.Y, 6);
    }

    [Fact]
    public void QuarterTurn_MovesABoxLikeThePictureTurns()
    {
        // 200 x 100 turns into 100 x 200: a point (x, y) goes to (100 - y, x).
        var label = Assert.Single(Run(Box(20, 10, 60, 30), new AugmentParams { QuarterTurns = 1 }));

        var bounds = Bounds(label);
        Assert.Equal(70, bounds.X, 6);
        Assert.Equal(20, bounds.Y, 6);
        Assert.Equal(20, bounds.Width, 6);
        Assert.Equal(40, bounds.Height, 6);
        Assert.Equal(ShapeKind.Box, label.Kind);
    }

    [Fact]
    public void SmallRotation_GrowsABoxToTheBoxAroundTheTurnedOne()
    {
        var label = Assert.Single(Run(Box(80, 30, 120, 70), new AugmentParams { RotateDegrees = 20 }));

        Assert.True(Bounds(label).Area > 40 * 40);
        Assert.Equal(ShapeKind.Box, label.Kind);
    }

    [Fact]
    public void SmallRotation_KeepsTheCentreOfACentredBox()
    {
        var label = Assert.Single(Run(Box(80, 30, 120, 70), new AugmentParams { RotateDegrees = 20 }));

        var bounds = Bounds(label);
        Assert.Equal(100, bounds.X + bounds.Width / 2, 6);
        Assert.Equal(50, bounds.Y + bounds.Height / 2, 6);
    }

    [Fact]
    public void Polygon_TurnedByAQuarterKeepsItsArea()
    {
        var polygon = Make(ShapeKind.Polygon, new[] { new PixelPoint(20, 20), new PixelPoint(80, 30), new PixelPoint(40, 70) });

        var label = Assert.Single(Run(polygon, new AugmentParams { QuarterTurns = 3 }));

        Assert.Equal(PolygonMath.Area(polygon.Points), PolygonMath.Area(label.Points), 6);
    }

    [Fact]
    public void Polygon_IsClippedAtTheImageBorder()
    {
        var polygon = Make(ShapeKind.Polygon, new PixelRect(100, 20, 60, 40).Corners());

        var label = Assert.Single(Run(polygon, new AugmentParams { TranslateX = 0.3 }));

        // Shifted right by 60: it spans 160..220 and only 160..200 stays in the picture.
        Assert.Equal(40 * 40, PolygonMath.Area(label.Points), 6);
        Assert.All(label.Points, point => Assert.InRange(point.X, 0, Width));
    }

    [Fact]
    public void LabelMostlyOutsideIsDropped()
    {
        // 130..190 shifted by 60 becomes 190..250: 10 of 60 px visible, under the 30% limit.
        Assert.Empty(Run(Box(130, 20, 190, 60), new AugmentParams { TranslateX = 0.3 }));
    }

    [Fact]
    public void LabelMostlyInsideIsKeptAndClipped()
    {
        var label = Assert.Single(Run(Box(100, 20, 160, 60), new AugmentParams { TranslateX = 0.3 }));

        var bounds = Bounds(label);
        Assert.Equal(160, bounds.X, 6);
        Assert.Equal(40, bounds.Width, 6);
    }

    [Fact]
    public void MinimumVisibleShareComesFromTheOptions()
    {
        var options = new LabelTransformOptions { MinVisiblePercent = 10 };

        Assert.Single(Run(Box(130, 20, 190, 60), new AugmentParams { TranslateX = 0.3 }, options));
    }

    [Fact]
    public void RotatedBox_TurnedByAQuarterKeepsTheSameCorners()
    {
        var rect = new RotatedRect(new PixelPoint(40, 50), new PixelPoint(100, 50), 20).Corners();

        var label = Assert.Single(Run(Make(ShapeKind.RotatedBox, rect), new AugmentParams { QuarterTurns = 1 }));

        Assert.Equal(ShapeKind.RotatedBox, label.Kind);
        Assert.Equal(PolygonMath.Area(rect), PolygonMath.Area(label.Points), 6);
        // The long side was horizontal and is vertical now.
        var bounds = Bounds(label);
        Assert.True(bounds.Height > bounds.Width);
    }

    [Fact]
    public void RotatedBox_CutByTheBorderIsRefittedAsARectangle()
    {
        var rect = new RotatedRect(new PixelPoint(120, 40), new PixelPoint(190, 70), 24).Corners();

        var label = Assert.Single(Run(Make(ShapeKind.RotatedBox, rect), new AugmentParams { TranslateX = 0.1 }));

        Assert.Equal(4, label.Points.Count);
        var p = label.Points;
        var firstSide = (p[1].X - p[0].X, p[1].Y - p[0].Y);
        var secondSide = (p[2].X - p[1].X, p[2].Y - p[1].Y);
        Assert.Equal(0, firstSide.Item1 * secondSide.Item1 + firstSide.Item2 * secondSide.Item2, 3);
        // It still covers everything that stayed in the picture: 50 px of the box lie to the left of x = 200.
        Assert.True(PolygonMath.Area(p) >= PolygonMath.Area(ConvexGeometry.ClipToRectangle(rect.Select(Affine2D.Translation(20, 0).Apply).ToList(), Width, Height)) - 1e-6);
    }

    [Fact]
    public void Point_OutsideThePictureIsDropped()
    {
        var point = Make(ShapeKind.Point, new[] { new PixelPoint(190, 50) });

        Assert.Empty(Run(point, new AugmentParams { TranslateX = 0.2 }));
        Assert.Single(Run(point, new AugmentParams { TranslateX = -0.2 }));
    }

    [Fact]
    public void ClassMap_RenamesMergedClassesAndDropsRemovedOnes()
    {
        var mapper = new Func<long, long?>(id => id switch { 2 => 1, 3 => null, _ => id });
        var options = new LabelTransformOptions { ClassMap = mapper };
        var annotations = new[] { Box(10, 10, 30, 30, classId: 1), Box(40, 10, 60, 30, classId: 2), Box(70, 10, 90, 30, classId: 3) };

        var labels = LabelTransformer.Transform(annotations, Plan(AugmentParams.None), options);

        Assert.Equal(new long[] { 1, 1 }, labels.Select(label => label.ClassId));
    }

    [Fact]
    public void Cutout_DropsALabelItCoversEnough()
    {
        var cutout = new NormalizedRect(0.05, 0.05, 0.4, 0.5);
        var covered = Box(20, 10, 60, 30);
        var options = new LabelTransformOptions { CutoutDropPercent = 50 };

        Assert.Empty(Run(covered, new AugmentParams { Cutouts = new[] { cutout } }, options));
        Assert.Single(Run(covered, new AugmentParams { Cutouts = new[] { cutout } }));
    }

    [Fact]
    public void IdentityPlan_ReturnsTheLabelsUnchanged()
    {
        var box = Box(20, 10, 60, 30);

        var label = Assert.Single(Run(box, AugmentParams.None));

        Assert.Equal(box.Points, label.Points);
        Assert.Equal(box.Id, label.SourceId);
    }

    [Fact]
    public void Preprocess_FitLetterboxesLabelsTogetherWithThePicture()
    {
        var preprocess = new PreprocessConfig { Resize = ResizeMode.Fit, Width = 400, Height = 400 };
        var plan = RenderPlanner.Plan(Width, Height, preprocess, null);

        var label = Assert.Single(LabelTransformer.Transform(new[] { Box(0, 0, 200, 100) }, plan, new LabelTransformOptions()));

        // 200 x 100 scales by 2 to 400 x 200 and sits in the middle: 100 px bars above and below.
        var bounds = Bounds(label);
        Assert.Equal(0, bounds.X, 6);
        Assert.Equal(100, bounds.Y, 6);
        Assert.Equal(400, bounds.Width, 6);
        Assert.Equal(200, bounds.Height, 6);
    }
}


