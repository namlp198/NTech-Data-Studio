using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Import;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Exporters.Yolo;
using static NTech.DataStudio.Exporters.Tests.ExporterFixture;

namespace NTech.DataStudio.Exporters.Tests;

public class YoloExporterTests : IDisposable
{
    private readonly ExporterFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private void Write(IDatasetExporter exporter, params ExportEntry[] entries) =>
        exporter.WriteLabels(new ExportOutput(_fixture.Root, _fixture.Classes, entries));

    [Fact]
    public void Detect_WritesClassCentreAndSizeAsFractionsOfThePicture()
    {
        Write(new YoloDetectExporter(), Entry("a", SplitKind.Train, new[] { BoxLabel(Blue, 20, 10, 120, 60) }));

        // centre (70, 35) of 200x100, size 100x50.
        Assert.Equal("1 0.35 0.35 0.5 0.5\n", _fixture.Read("labels/train/a.txt"));
    }

    [Fact]
    public void APictureWithoutLabelsGetsAnEmptyLabelFile()
    {
        Write(new YoloDetectExporter(), Entry("empty", SplitKind.Val, Array.Empty<RenderedLabel>()));

        Assert.True(_fixture.Exists("labels/val/empty.txt"));
        Assert.Equal(string.Empty, _fixture.Read("labels/val/empty.txt"));
    }

    [Fact]
    public void ImagesAndLabelsSitInMirroredFolders()
    {
        var exporter = new YoloDetectExporter();
        var entry = Entry("x", SplitKind.Test, Array.Empty<RenderedLabel>());

        Assert.Equal("images/test/x.jpg", exporter.ImagePath(entry));
        Write(exporter, entry);
        Assert.True(_fixture.Exists("labels/test/x.txt"));
    }

    [Fact]
    public void Segment_WritesEveryVertexNormalized()
    {
        Write(new YoloSegmentExporter(), Entry("a", SplitKind.Train, new[]
        {
            Label(Red, ShapeKind.Polygon, (20, 10), (120, 10), (70, 60))
        }));

        Assert.Equal("0 0.1 0.1 0.6 0.1 0.35 0.6\n", _fixture.Read("labels/train/a.txt"));
    }

    [Fact]
    public void Obb_WritesFourCorners_AndABoxBecomesItsOwnCorners()
    {
        Write(new YoloObbExporter(), Entry("a", SplitKind.Train, new[] { BoxLabel(Red, 20, 10, 120, 60) }));

        Assert.Equal("0 0.1 0.1 0.6 0.1 0.6 0.6 0.1 0.6\n", _fixture.Read("labels/train/a.txt"));
    }

    [Fact]
    public void Obb_ARotatedBoxThePictureEdgeCutIsPulledInsideSoNoCoordinateExceedsOne()
    {
        // A label the border cut, refitted by LabelTransformer, can poke out; Ultralytics throws such a file out.
        var poking = Label(Red, ShapeKind.RotatedBox, (-8, 30), (60, 20), (64, 40), (-4, 50));
        Write(new YoloObbExporter(), Entry("a", SplitKind.Train, new[] { poking }));

        var parsed = YoloLabelParser.Parse(_fixture.Read("labels/train/a.txt"), YoloLabelKind.Obb, 200, 100);

        var label = Assert.Single(parsed);
        Assert.All(label.Points, point =>
        {
            Assert.InRange(point.X, 0, 200);
            Assert.InRange(point.Y, 0, 100);
        });
        foreach (var number in _fixture.Read("labels/train/a.txt").Split(' ', '\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            Assert.InRange(double.Parse(number, System.Globalization.CultureInfo.InvariantCulture), 0, 1);
    }

    [Fact]
    public void ALabelThatCannotBeWritten_IsLeftOut_NotWrittenBroken()
    {
        Write(new YoloDetectExporter(), Entry("a", SplitKind.Train, new[]
        {
            Label(Red, ShapeKind.Point, (5, 5)),
            BoxLabel(Red, 20, 10, 120, 60)
        }));

        Assert.Single(_fixture.Read("labels/train/a.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void LabelsOfAClassNotInTheDataset_AreSkipped()
    {
        var stranger = new ExportClass(9, 99, "other", "other");
        Write(new YoloDetectExporter(), Entry("a", SplitKind.Train, new[] { BoxLabel(stranger, 20, 10, 120, 60) }));

        Assert.Equal(string.Empty, _fixture.Read("labels/train/a.txt"));
    }

    [Fact]
    public void DataYaml_ListsTheSplitsPresent_AndTheClassNamesSafely()
    {
        Write(new YoloDetectExporter(),
            Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>()),
            Entry("b", SplitKind.Val, Array.Empty<RenderedLabel>()));

        var yaml = _fixture.Read("data.yaml");

        Assert.Contains("train: images/train", yaml);
        Assert.Contains("val: images/val", yaml);
        Assert.DoesNotContain("test:", yaml);
        Assert.DoesNotContain("path:", yaml);
        var names = YoloLabelParser.ParseNames(yaml);
        Assert.Equal("red", names[0]);
        // A colon, a hash and non-ASCII letters in a name must survive.
        Assert.Equal("Đầu nối: xanh #2", names[1]);
    }

    [Fact]
    public void DataYaml_AddsTestWhenThereAreTestPictures()
    {
        Write(new YoloDetectExporter(), Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>()), Entry("t", SplitKind.Test, Array.Empty<RenderedLabel>()));

        Assert.Contains("test: images/test", _fixture.Read("data.yaml"));
    }

    [Fact]
    public void FilesCarryNoByteOrderMark()
    {
        Write(new YoloDetectExporter(), Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>()));

        var bytes = File.ReadAllBytes(Path.Combine(_fixture.Root, "data.yaml"));
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Theory]
    [InlineData(YoloLabelKind.Detect)]
    [InlineData(YoloLabelKind.Segment)]
    [InlineData(YoloLabelKind.Obb)]
    public void RoundTrip_ReadingTheExportGivesTheLabelsBack(YoloLabelKind kind)
    {
        IDatasetExporterFactory.For(kind, out var exporter, out var labels);
        Write(exporter, Entry("a", SplitKind.Train, labels, 640, 480));

        var parsed = YoloLabelParser.Parse(_fixture.Read("labels/train/a.txt"), kind, 640, 480);

        Assert.Equal(labels.Count, parsed.Count);
        for (var index = 0; index < labels.Count; index++)
        {
            Assert.Equal(index == 0 ? 0 : 1, parsed[index].ClassKey);
            var expected = Expected(kind, labels[index]);
            Assert.Equal(expected.Count, parsed[index].Points.Count);
            for (var corner = 0; corner < expected.Count; corner++)
            {
                // Six decimals of a fraction of 640 px: well under a thousandth of a pixel.
                Assert.Equal(expected[corner].X, parsed[index].Points[corner].X, 2);
                Assert.Equal(expected[corner].Y, parsed[index].Points[corner].Y, 2);
            }
        }
    }

    private static IReadOnlyList<PixelPoint> Expected(YoloLabelKind kind, Domain.Augment.RenderedLabel label) =>
        kind == YoloLabelKind.Detect ? PolygonMath.BoundingBox(label.Points).Corners() : label.Points;

    private static class IDatasetExporterFactory
    {
        public static void For(YoloLabelKind kind, out IDatasetExporter exporter, out IReadOnlyList<Domain.Augment.RenderedLabel> labels)
        {
            switch (kind)
            {
                case YoloLabelKind.Detect:
                    exporter = new YoloDetectExporter();
                    labels = new[] { BoxLabel(Red, 12.5, 33.25, 301.75, 200.5), BoxLabel(Blue, 0, 0, 640, 480) };
                    break;
                case YoloLabelKind.Segment:
                    exporter = new YoloSegmentExporter();
                    labels = new[]
                    {
                        Label(Red, ShapeKind.Polygon, (10.5, 20.25), (300.125, 40), (250, 400.75), (30, 380)),
                        Label(Blue, ShapeKind.Polygon, (100, 100), (200, 100), (150, 200))
                    };
                    break;
                default:
                    exporter = new YoloObbExporter();
                    labels = new[]
                    {
                        Label(Red, ShapeKind.RotatedBox, (100, 100), (300, 150), (280, 250), (80, 200)),
                        Label(Blue, ShapeKind.RotatedBox, (400, 300), (500, 300), (500, 400), (400, 400))
                    };
                    break;
            }
        }
    }

    [Fact]
    public void Classify_PutsEachPictureInItsClassFolder_AndKeepsEmptyClassFolders()
    {
        var exporter = new YoloClassifyExporter();
        var entry = Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>(), classify: Blue);

        Assert.Equal("train/Dau_noi_xanh_2/a.jpg", exporter.ImagePath(entry));
        Write(exporter, entry, Entry("b", SplitKind.Val, Array.Empty<RenderedLabel>(), classify: Red));

        // Both classes exist in both splits, though each split has a picture of one class only.
        Assert.True(_fixture.Exists("train/red"));
        Assert.True(_fixture.Exists("val/Dau_noi_xanh_2"));
        Assert.False(_fixture.Exists("data.yaml"));
    }

    [Fact]
    public void Classify_APictureWithoutAClassIsRefused()
    {
        var entry = Entry("a", SplitKind.Train, Array.Empty<RenderedLabel>());

        Assert.Throws<InvalidOperationException>(() => new YoloClassifyExporter().ImagePath(entry));
    }
}
