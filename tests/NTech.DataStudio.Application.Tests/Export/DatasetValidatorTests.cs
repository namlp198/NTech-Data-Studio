using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Tests.Export;

public class DatasetValidatorTests
{
    private const int Width = 1000;
    private const int Height = 800;

    private static readonly ModelProfile Detect = new("yolo26-detect", "ultralytics-yolo", TaskKind.Detect, 640, new[] { 640 }, 32, 12, new[] { "yolo" });
    private static readonly ModelProfile Segment = Detect with { Id = "yolo26-segment", Task = TaskKind.Segment };
    private static readonly ModelProfile Classify = Detect with { Id = "yolo26-classify", Task = TaskKind.Classify, MinObjectPx = 0 };
    private static readonly ModelProfile Anomaly = Detect with { Id = "anomaly", Task = TaskKind.Anomaly, ExportFormats = Array.Empty<string>() };

    private static readonly LabelClass Red = new(1, "red", "#E25D5D", 1, 0);
    private static readonly LabelClass Blue = new(2, "blue", "#4A90E2", 2, 1);

    private sealed class Scenario
    {
        public List<DatasetImage> Images { get; } = new();
        public Dictionary<long, SplitKind> Split { get; } = new();
        public Dictionary<long, List<Annotation>> Annotations { get; } = new();
        public List<LabelClass> Classes { get; } = new() { Red, Blue };
        public DatasetConfig Config { get; set; } = new();
        public HashSet<string> MissingMasters { get; } = new();

        public Scenario Image(long id, SplitKind split, ImageStatus status = ImageStatus.Labeled, params long[] tags)
        {
            Images.Add(new DatasetImage(id, 1, null, $"master/m{id}.jpg", Width, Height, status,
                new Dictionary<long, int>(), tags.ToHashSet()));
            Split[id] = split;
            Annotations[id] = new List<Annotation>();
            return this;
        }

        public Scenario Box(long imageId, long classId, double x = 100, double y = 100, double w = 200, double h = 150) =>
            Add(imageId, classId, ShapeKind.Box, new PixelRect(x, y, w, h).Corners());

        public Scenario Polygon(long imageId, long classId, params (double X, double Y)[] points) =>
            Add(imageId, classId, ShapeKind.Polygon, points.Select(point => new PixelPoint(point.X, point.Y)).ToList());

        public Scenario Add(long imageId, long classId, ShapeKind kind, IReadOnlyList<PixelPoint> points)
        {
            var now = DateTimeOffset.UtcNow;
            Annotations[imageId].Add(new Annotation(Guid.NewGuid(), imageId, classId, kind, points, AnnotationSource.Manual, now, now));
            return this;
        }

        public ValidationReport Validate(ModelProfile? profile = null, int imgsz = 640) =>
            DatasetValidator.Validate(new ValidationInput(
                Images, Split, id => Annotations[id], Classes, Config, profile ?? Detect, imgsz, path => !MissingMasters.Contains(path)));
    }

    /// <summary>Enough labelled images in train and val for no count warning to appear.</summary>
    private static Scenario Healthy()
    {
        var scenario = new Scenario();
        for (var id = 1; id <= 60; id++)
        {
            scenario.Image(id, id <= 40 ? SplitKind.Train : SplitKind.Val);
            scenario.Box(id, Red.Id).Box(id, Blue.Id, 500, 400);
        }

        return scenario;
    }

    private static bool Has(ValidationReport report, IssueSeverity severity, string text) =>
        report.Issues.Any(issue => issue.Severity == severity && issue.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ACleanDataset_HasNoIssues()
    {
        var report = Healthy().Validate();

        Assert.Empty(report.Issues);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void AZeroAreaBox_IsAnError()
    {
        var scenario = Healthy().Box(1, Red.Id, 100, 100, 0, 50);

        var report = scenario.Validate();

        Assert.True(report.HasErrors);
        Assert.True(Has(report, IssueSeverity.Error, "diện tích bằng 0"));
        Assert.Contains("m1.jpg", report.Issues.Single(issue => issue.Message.Contains("diện tích")).Message);
    }

    [Fact]
    public void ALabelOutsideThePicture_IsAnError_ButOnePixelOverTheEdgeIsTolerated()
    {
        var tolerated = Healthy().Box(1, Red.Id, 900, 700, 100.4, 100.4);
        var outside = Healthy().Box(1, Red.Id, 900, 700, 140, 100);

        Assert.False(tolerated.Validate().HasErrors);
        Assert.True(Has(outside.Validate(), IssueSeverity.Error, "nằm ngoài ảnh"));
    }

    [Fact]
    public void ASelfIntersectingPolygon_IsAnError()
    {
        var bowTie = Healthy().Polygon(1, Red.Id, (100, 100), (300, 300), (300, 100), (100, 300));

        Assert.True(Has(bowTie.Validate(Segment), IssueSeverity.Error, "tự cắt"));
    }

    [Fact]
    public void ALabelOfAClassThatDoesNotExist_IsAnError()
    {
        var scenario = Healthy().Box(1, 99);

        Assert.True(Has(scenario.Validate(), IssueSeverity.Error, "class không tồn tại"));
    }

    [Fact]
    public void ADroppedClassIsNotChecked_ButAMergeTargetThatDoesNotExistIs()
    {
        var dropped = Healthy().Box(1, Blue.Id, 900, 700, 500, 500);
        dropped.Config = new DatasetConfig { Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(Blue.Id, null) } } };
        var badTarget = Healthy();
        badTarget.Config = new DatasetConfig { Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(Blue.Id, 99) } } };

        // The oversized blue box is outside the picture, but blue is dropped, so it is never written.
        Assert.False(dropped.Validate().HasErrors);
        Assert.True(Has(badTarget.Validate(), IssueSeverity.Error, "class không tồn tại"));
    }

    [Fact]
    public void Segment_AnImageWithOnlyBoxesIsAnError_ButOneWithAPolygonToo_IsNot()
    {
        var onlyBoxes = Healthy();
        var mixed = Healthy().Polygon(1, Red.Id, (10, 10), (60, 10), (30, 50));

        var errors = onlyBoxes.Validate(Segment);

        Assert.True(Has(errors, IssueSeverity.Error, "chỉ có box"));
        Assert.Contains("60 ảnh", errors.Issues.First(issue => issue.Severity == IssueSeverity.Error).Message);
        // Image 1 now has a polygon; the other 59 still only have boxes.
        Assert.Contains("59 ảnh", mixed.Validate(Segment).Issues.First(issue => issue.Severity == IssueSeverity.Error).Message);
    }

    [Fact]
    public void Detect_TurnsPolygonsIntoBoxes_AndSaysSo()
    {
        var scenario = Healthy().Polygon(1, Red.Id, (10, 10), (60, 10), (30, 50));

        var report = scenario.Validate(Detect);

        Assert.False(report.HasErrors);
        Assert.True(Has(report, IssueSeverity.Warning, "box bao quanh"));
    }

    [Fact]
    public void Points_AreWarnedAbout_NotRejected()
    {
        var scenario = Healthy().Add(1, Red.Id, ShapeKind.Point, new[] { new PixelPoint(5, 5) });

        var report = scenario.Validate();

        Assert.False(report.HasErrors);
        Assert.True(Has(report, IssueSeverity.Warning, "point"));
    }

    [Fact]
    public void NoTrainImages_IsAnError_AndNoValImages_IsAWarning()
    {
        var scenario = new Scenario().Image(1, SplitKind.Val).Box(1, Red.Id);
        var onlyTrain = new Scenario().Image(1, SplitKind.Train).Box(1, Red.Id);

        Assert.True(Has(scenario.Validate(), IssueSeverity.Error, "không có ảnh train"));
        Assert.True(Has(onlyTrain.Validate(), IssueSeverity.Warning, "không có ảnh val"));
        Assert.False(onlyTrain.Validate().HasErrors);
    }

    [Fact]
    public void AMasterThatIsGone_IsAnError()
    {
        var scenario = Healthy();
        scenario.MissingMasters.Add("master/m3.jpg");

        Assert.True(Has(scenario.Validate(), IssueSeverity.Error, "không còn trên đĩa"));
    }

    [Fact]
    public void AProfileWithoutAFormat_CannotBeExported()
    {
        var report = Healthy().Validate(Anomaly);

        Assert.True(report.HasErrors);
        Assert.Single(report.Issues);
    }

    [Fact]
    public void AClassWithFewLabels_AndAClassMissingFromVal_AreWarnings()
    {
        var scenario = new Scenario();
        for (var id = 1; id <= 20; id++)
        {
            scenario.Image(id, id <= 15 ? SplitKind.Train : SplitKind.Val).Box(id, Red.Id);
            if (id <= 10) scenario.Box(id, Blue.Id, 500, 400);
        }

        var report = scenario.Validate();

        Assert.False(report.HasErrors);
        Assert.True(Has(report, IssueSeverity.Warning, "dưới 50 nhãn"));
        Assert.True(Has(report, IssueSeverity.Warning, "red (20)"));
        Assert.True(Has(report, IssueSeverity.Warning, "blue (10)"));
        Assert.True(Has(report, IssueSeverity.Warning, "val thiếu class: blue"));
    }

    [Fact]
    public void SmallObjects_AreCountedAtTheTrainingSize()
    {
        // 20 px on a 1000 px picture is 12.8 px at imgsz 640: fine. At 320 it is 6.4 px: too small.
        var scenario = Healthy().Box(1, Red.Id, 100, 100, 20, 20);

        Assert.DoesNotContain(scenario.Validate(imgsz: 640).Issues, issue => issue.Message.Contains("nhỏ hơn"));
        Assert.True(Has(scenario.Validate(imgsz: 320), IssueSeverity.Warning, "nhỏ hơn 12 px"));
    }

    [Fact]
    public void SmallObjects_FollowThePreprocessingResize()
    {
        var scenario = Healthy().Box(1, Red.Id, 100, 100, 20, 20);
        Assert.DoesNotContain(scenario.Validate(imgsz: 640).Issues, issue => issue.Message.Contains("nhỏ hơn"));

        // Fit into a wide 1000x250 strip shrinks the 1000x800 picture to 31%: 20 px becomes 6 px (at imgsz 640 on a
        // 1000 px long edge that is 4 px), far under the 12 px the profile wants.
        scenario.Config = new DatasetConfig { Preprocess = new PreprocessConfig { Resize = ResizeMode.Fit, Width = 1000, Height = 250 } };

        Assert.True(Has(scenario.Validate(imgsz: 640), IssueSeverity.Warning, "nhỏ hơn"));
    }

    [Fact]
    public void AnImgszThatIsNotAMultipleOfTheStride_IsAWarning()
    {
        Assert.True(Has(Healthy().Validate(imgsz: 650), IssueSeverity.Warning, "stride 32"));
        Assert.DoesNotContain(Healthy().Validate(imgsz: 640).Issues, issue => issue.Message.Contains("stride"));
    }

    [Fact]
    public void ImagesStillNew_AreWarnedAbout()
    {
        var scenario = Healthy().Image(100, SplitKind.Train, ImageStatus.New);

        Assert.True(Has(scenario.Validate(), IssueSeverity.Warning, "chưa gán nhãn (New)"));
    }

    [Fact]
    public void Classify_NeedsExactlyOneTagPerImage()
    {
        var scenario = new Scenario();
        scenario.Image(1, SplitKind.Train, ImageStatus.Labeled, Red.Id);
        scenario.Image(2, SplitKind.Train, ImageStatus.Labeled);
        scenario.Image(3, SplitKind.Val, ImageStatus.Labeled, Red.Id, Blue.Id);

        var report = scenario.Validate(Classify, 224);

        Assert.True(Has(report, IssueSeverity.Error, "chưa có tag"));
        Assert.True(Has(report, IssueSeverity.Error, "nhiều tag"));
    }

    [Fact]
    public void Classify_IgnoresBoxesButCountsImages()
    {
        var scenario = new Scenario();
        for (var id = 1; id <= 120; id++)
            scenario.Image(id, id <= 80 ? SplitKind.Train : SplitKind.Val, ImageStatus.Labeled, id % 2 == 0 ? Red.Id : Blue.Id);
        scenario.Box(1, 99); // would be an error for detection; classification does not read it

        var report = scenario.Validate(Classify, 224);

        Assert.False(report.HasErrors);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Classify_TagsGoThroughTheClassMap()
    {
        var scenario = new Scenario();
        for (var id = 1; id <= 60; id++)
            scenario.Image(id, id <= 40 ? SplitKind.Train : SplitKind.Val, ImageStatus.Labeled, id % 2 == 0 ? Red.Id : Blue.Id);
        // Both tags merge into red: still exactly one per image.
        scenario.Config = new DatasetConfig { Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(Blue.Id, Red.Id) } } };
        Assert.False(scenario.Validate(Classify, 224).HasErrors);

        // Dropping blue leaves its images without a tag.
        scenario.Config = new DatasetConfig { Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(Blue.Id, null) } } };
        Assert.True(Has(scenario.Validate(Classify, 224), IssueSeverity.Error, "chưa có tag"));
    }
}
