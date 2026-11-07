using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Import;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Exporters.Coco;
using static NTech.DataStudio.Exporters.Tests.ExporterFixture;

namespace NTech.DataStudio.Exporters.Tests;

public class CocoExporterTests : IDisposable
{
    private readonly ExporterFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private void Write(bool segmentation, params ExportEntry[] entries) =>
        new CocoExporter(segmentation).WriteLabels(new ExportOutput(_fixture.Root, _fixture.Classes, entries));

    [Fact]
    public void EachSplitGetsItsOwnFolderAndAnnotationFile_WithRoboflowNames()
    {
        var exporter = new CocoExporter(false);
        Write(false,
            Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>()),
            Entry("b", SplitKind.Val, Array.Empty<RenderedLabel>()),
            Entry("c", SplitKind.Test, Array.Empty<RenderedLabel>()));

        Assert.Equal("train/a.jpg", exporter.ImagePath(Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>())));
        Assert.Equal("valid/b.jpg", exporter.ImagePath(Entry("b", SplitKind.Val, Array.Empty<RenderedLabel>())));
        Assert.True(_fixture.Exists("train/_annotations.coco.json"));
        Assert.True(_fixture.Exists("valid/_annotations.coco.json"));
        Assert.True(_fixture.Exists("test/_annotations.coco.json"));
    }

    [Fact]
    public void Detect_WritesPixelBoxes_CategoriesFromOne_AndImageSizes()
    {
        Write(false, Entry("a", SplitKind.Train, new[] { BoxLabel(Blue, 20, 10, 120, 60) }, 640, 480));

        var coco = CocoLabelParser.Parse(_fixture.Read("train/_annotations.coco.json"));

        Assert.Equal(new[] { 1, 2 }, coco.Categories.Select(category => category.Id));
        Assert.Equal("Đầu nối: xanh #2", coco.Categories[1].Name);
        var image = Assert.Single(coco.Images);
        Assert.Equal(("a.jpg", 640, 480), (image.FileName, image.Width, image.Height));
        var label = Assert.Single(image.Labels);
        Assert.Equal(2, label.ClassKey);
        Assert.Equal(ShapeKind.Box, label.Kind);
        var box = PolygonMath.BoundingBox(label.Points);
        Assert.Equal((20, 10, 100, 50), (box.X, box.Y, box.Width, box.Height));
    }

    [Fact]
    public void Detect_AreaIsTheBoxArea_AndSegmentationIsEmpty()
    {
        Write(false, Entry("a", SplitKind.Train, new[] { BoxLabel(Red, 20, 10, 120, 60) }));

        var text = _fixture.Read("train/_annotations.coco.json");

        Assert.Contains("\"area\": 5000", text);
        Assert.Matches(@"""segmentation"":\s*\[\s*\]", text);
    }

    [Fact]
    public void Segment_WritesThePolygon_AndTheAreaOfThePolygonNotTheBox()
    {
        var triangle = Label(Red, ShapeKind.Polygon, (20, 10), (120, 10), (70, 60));
        Write(true, Entry("a", SplitKind.Train, new[] { triangle }));

        var text = _fixture.Read("train/_annotations.coco.json");
        var coco = CocoLabelParser.Parse(text);

        var label = Assert.Single(coco.Images[0].Labels);
        Assert.Equal(ShapeKind.Polygon, label.Kind);
        Assert.Equal(3, label.Points.Count);
        Assert.Contains("\"area\": 2500", text);
    }

    [Fact]
    public void RoundTrip_ReadingTheExportGivesTheLabelsBack_ForEveryPictureAndSplit()
    {
        var first = Entry("a", SplitKind.Train, new[]
        {
            Label(Red, ShapeKind.Polygon, (10.5, 20.25), (300.125, 40), (250, 400.75), (30, 380)),
            Label(Blue, ShapeKind.Polygon, (100, 100), (200, 100), (150, 200))
        }, 640, 480);
        var second = Entry("b", SplitKind.Train, Array.Empty<RenderedLabel>(), 640, 480);
        var third = Entry("c", SplitKind.Val, new[] { Label(Blue, ShapeKind.Polygon, (1, 1), (50, 1), (25, 40)) }, 320, 240);
        Write(true, first, second, third);

        var train = CocoLabelParser.Parse(_fixture.Read("train/_annotations.coco.json"));
        var valid = CocoLabelParser.Parse(_fixture.Read("valid/_annotations.coco.json"));

        Assert.Equal(new[] { "a.jpg", "b.jpg" }, train.Images.Select(image => image.FileName));
        Assert.Empty(train.Images[1].Labels);
        Assert.Equal(2, train.Images[0].Labels.Count);
        for (var index = 0; index < 2; index++)
        {
            var expected = first.Labels[index];
            var actual = train.Images[0].Labels[index];
            Assert.Equal(expected.ClassId == Red.ClassId ? 1 : 2, actual.ClassKey);
            Assert.Equal(expected.Points.Count, actual.Points.Count);
            for (var corner = 0; corner < expected.Points.Count; corner++)
            {
                Assert.Equal(expected.Points[corner].X, actual.Points[corner].X, 3);
                Assert.Equal(expected.Points[corner].Y, actual.Points[corner].Y, 3);
            }
        }

        Assert.Equal((320, 240), (valid.Images[0].Width, valid.Images[0].Height));
        Assert.Single(valid.Images[0].Labels);
    }

    [Fact]
    public void Segment_ALabelWithoutAnOutlineIsLeftOut()
    {
        Write(true, Entry("a", SplitKind.Train, new[]
        {
            Label(Red, ShapeKind.Point, (5, 5)),
            BoxLabel(Red, 20, 10, 120, 60)
        }));

        var coco = CocoLabelParser.Parse(_fixture.Read("train/_annotations.coco.json"));

        Assert.Single(coco.Images[0].Labels);
    }

    [Fact]
    public void TheFileIsValidJson_EvenWithNothingInIt()
    {
        Write(false, Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>()));

        var coco = CocoLabelParser.Parse(_fixture.Read("train/_annotations.coco.json"));

        Assert.Single(coco.Images);
        Assert.Empty(coco.Images[0].Labels);
    }
}
