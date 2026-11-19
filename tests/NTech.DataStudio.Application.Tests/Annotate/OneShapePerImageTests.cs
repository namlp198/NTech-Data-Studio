using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Tests.Annotate;

/// <summary>Decision 0022: an image holds boxes or polygons, never both; boxes become polygons with SAM in place.</summary>
public class OneShapePerImageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly AnnotateFixture _fixture = new(imageCount: 3);

    public void Dispose() => _fixture.Dispose();

    private static Annotation BoxAt(long imageId, long classId, double x = 10, double y = 10) =>
        new(Guid.NewGuid(), imageId, classId, ShapeKind.Box, new PixelRect(x, y, 40, 30).Corners(), AnnotationSource.Manual, Now, Now);

    private static Annotation PolygonAt(long imageId, long classId, double x = 100, double y = 30) =>
        new(Guid.NewGuid(), imageId, classId, ShapeKind.Polygon,
            new[] { new PixelPoint(x, y), new PixelPoint(x + 40, y), new PixelPoint(x + 20, y + 30) }, AnnotationSource.Manual, Now, Now);

    private sealed class FakeSegmenter : ISmartSegmenter
    {
        public string? Unavailable { get; set; }

        /// <summary>What SAM answers for a box; null (the default) answers the box shrunk by one pixel on each side.</summary>
        public Func<PixelRect, IReadOnlyList<PixelPoint>?>? Answer { get; set; }

        public Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
            string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default)
        {
            var box = request.Box!.Value;
            return Task.FromResult(Answer is null
                ? new PixelRect(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2).Corners()
                : Answer(box));
        }
    }

    // ---- the rule ----

    [Fact]
    public void ABoxCannotBeAddedToAnImageWithPolygons_AndNothingIsWritten()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("tape").Id;
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { PolygonAt(1, classId) }));

        var error = Assert.Throws<ShapeMixException>(() =>
            service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, classId) })));

        Assert.Contains("đã có polygon", error.Message);
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 1"));
    }

    [Fact]
    public void APolygonCannotBeAddedToAnImageWithBoxes_AndTheMessagePointsToTheConversion()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("tape").Id;
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, classId) }));

        var error = Assert.Throws<ShapeMixException>(() =>
            service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { PolygonAt(1, classId) })));

        Assert.Contains("đã có box", error.Message);
        Assert.Contains("Box thành polygon", error.Message);
    }

    [Fact]
    public void TheSameShapeKind_OrADifferentImage_IsFine()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("tape").Id;
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, classId) }));

        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, classId, 100, 40) }));
        service.Execute(_fixture.OpenImage(service, 2), EditRequest.Add(new[] { PolygonAt(2, classId) }));

        Assert.Equal(2, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 1"));
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 2"));
    }

    [Fact]
    public void AnImageThatAlreadyMixesShapes_CanStillBeTidied()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var classId = new ClassService(store).Add("tape").Id;
        var box = BoxAt(1, classId);
        var polygon = PolygonAt(1, classId);
        // As it was labelled before the rule: both shapes in the database.
        var legacy = _fixture.OpenImage(service, 1);
        Assert.Null(ShapeFamilyRule.Check(new[] { box, polygon }, EditRequest.Remove(new[] { box })));
        Assert.Null(ShapeFamilyRule.Check(new[] { box, polygon }, EditRequest.Replace(box, box.WithPoints(new PixelRect(20, 20, 40, 30).Corners(), Now))));
        Assert.NotNull(legacy);
    }

    [Fact]
    public void AReplacementOfBoxesByPolygons_IsNotAMix()
    {
        var box = BoxAt(1, 1);
        var polygon = PolygonAt(1, 1);

        Assert.Null(ShapeFamilyRule.Check(new[] { box }, new EditRequest(new[] { box }, new[] { polygon }, Array.Empty<long>(), Array.Empty<long>())));
    }

    // ---- the conversion ----

    [Fact]
    public async Task Convert_ReplacesEachBoxByAPolygonOfTheSameClass_InOneEditThatUndoBringsBack()
    {
        using var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        var tape = new ClassService(store).Add("tape").Id;
        var wire = new ClassService(store).Add("wire").Id;
        service.Execute(_fixture.OpenImage(service, 1), EditRequest.Add(new[] { BoxAt(1, tape, 10, 10), BoxAt(1, wire, 100, 40) }));
        var document = _fixture.OpenImage(service, 1);
        var boxes = document.Annotations.ToList();

        var converter = new BoxToPolygonConverter(new FakeSegmenter());
        var conversion = await converter.ConvertAsync("a.jpg", 200, 100, boxes, Now);

        Assert.Equal(2, conversion.CutOut);
        Assert.Equal(0, conversion.RectangleOnly);
        Assert.Equal(boxes.Select(box => box.ClassId), conversion.Polygons.Select(polygon => polygon.ClassId));
        Assert.All(conversion.Polygons, polygon => Assert.Equal(ShapeKind.Polygon, polygon.Kind));
        Assert.All(conversion.Polygons, polygon => Assert.Equal(AnnotationSource.Model, polygon.Source));

        service.Execute(document, new EditRequest(conversion.Boxes, conversion.Polygons, Array.Empty<long>(), Array.Empty<long>()));
        Assert.Equal(2, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 1 AND kind = 'polygon'"));
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 1 AND kind = 'box'"));

        service.Undo(document);
        Assert.Equal(2, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 1 AND kind = 'box'"));
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM annotations WHERE image_id = 1 AND kind = 'polygon'"));
    }

    [Fact]
    public async Task Convert_ABoxSamCannotCutOut_BecomesItsRectangleAsAPolygon_SoNoBoxIsLeft()
    {
        var box = BoxAt(1, 1, 10, 10);
        var converter = new BoxToPolygonConverter(new FakeSegmenter { Answer = _ => null });

        var conversion = await converter.ConvertAsync("a.jpg", 200, 100, new[] { box }, Now);

        Assert.Equal(0, conversion.CutOut);
        Assert.Equal(1, conversion.RectangleOnly);
        var polygon = Assert.Single(conversion.Polygons);
        Assert.Equal(ShapeKind.Polygon, polygon.Kind);
        Assert.Equal(4, polygon.Points.Count);
        var bounds = PolygonMath.BoundingBox(polygon.Points);
        Assert.Equal(10, bounds.X, 6);
        Assert.Equal(40, bounds.Width, 6);
        Assert.Equal(30, bounds.Height, 6);
    }

    [Fact]
    public async Task Convert_ACutOutThatDoesNotLookLikeTheBox_IsNotTrusted()
    {
        var box = BoxAt(1, 1, 10, 10);
        var far = new PixelRect(150, 60, 20, 20).Corners();
        var converter = new BoxToPolygonConverter(new FakeSegmenter { Answer = _ => far });

        var conversion = await converter.ConvertAsync("a.jpg", 200, 100, new[] { box }, Now);

        Assert.Equal(1, conversion.RectangleOnly);
    }

    [Fact]
    public async Task Convert_WithoutSam_SaysWhatIsMissing()
    {
        var converter = new BoxToPolygonConverter(new FakeSegmenter { Unavailable = "Thiếu model SAM." });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            converter.ConvertAsync("a.jpg", 200, 100, new[] { BoxAt(1, 1) }, Now));

        Assert.Equal("Thiếu model SAM.", error.Message);
    }
}
