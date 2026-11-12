using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Storage.Settings;

namespace NTech.DataStudio.Application.Tests.Annotate;

/// <summary>Decision 0018: a detect model finds boxes, SAM cuts each out; the settings choose which labeler runs.</summary>
public class DetectThenSegmentTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private sealed class FakeDetector : IBoxDetector
    {
        public string? Unavailable { get; set; }
        public IReadOnlyList<string> ClassNames { get; set; } = new[] { "tape", "wire" };
        public int Width { get; set; } = 1000;
        public int Height { get; set; } = 800;
        public List<DetectedBox> Boxes { get; } = new();
        public double? LastMinScore { get; private set; }
        public string? Model { get; private set; }

        public void SetModel(string? onnxPath) => Model = onnxPath;

        public Task<BoxDetection> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default)
        {
            LastMinScore = minScore;
            return Task.FromResult(new BoxDetection(Width, Height, Boxes.ToList()));
        }
    }

    private sealed class FakeSegmenter : ISmartSegmenter
    {
        public string? Unavailable { get; set; }

        /// <summary>What SAM answers for a box; null (the default) answers the box shrunk by one pixel on each side, as a real cut-out would be.</summary>
        public Func<PixelRect, IReadOnlyList<PixelPoint>?>? Answer { get; set; }

        public List<(PixelRect? Region, PixelRect Box)> Calls { get; } = new();

        public Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
            string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default)
        {
            var box = request.Box!.Value;
            Calls.Add((region, box));
            if (region is { } part)
            {
                // SAM can only answer for what it analysed.
                Assert.True(box.X >= part.X - 1e-6 && box.Y >= part.Y - 1e-6 && box.Right <= part.Right + 1e-6 && box.Bottom <= part.Bottom + 1e-6,
                    "the box lies inside the region SAM analyses");
            }

            return Task.FromResult(Answer is null
                ? new PixelRect(box.X + 1, box.Y + 1, box.Width - 2, box.Height - 2).Corners()
                : Answer(box));
        }
    }

    private static DetectedBox Box(string name, double score, double x, double y, double width, double height) =>
        new(name, score, new PixelRect(x, y, width, height));

    // ---- the labeler ----

    [Fact]
    public async Task EveryBoxBecomesTheOutlineSamCutOut_WithItsClassAndScore()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 40, 30));
        detector.Boxes.Add(Box("wire", 0.7, 600, 500, 300, 200));
        var segmenter = new FakeSegmenter();
        var labeler = new DetectThenSegmentLabeler(detector, segmenter);

        var found = await labeler.DetectAsync("a.jpg", 0.4);

        Assert.Equal(2, found.Count);
        Assert.Equal(0.4, detector.LastMinScore);
        Assert.Equal("tape", found[0].ClassName);
        Assert.Equal(0.9, found[0].Score);
        Assert.False(found[0].IsBoxOutline);
        Assert.Equal(4, found[0].Outline.Count);
        Assert.Equal(101, found[0].Outline.Min(point => point.X), 6);
        Assert.Equal("wire", found[1].ClassName);
        Assert.Equal(2, segmenter.Calls.Count);
    }

    [Fact]
    public async Task ASmallBoxIsGivenToSamWithARegionAroundIt_ABigOneWithTheWholePicture()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 20, 12));
        detector.Boxes.Add(Box("wire", 0.8, 300, 200, 500, 400));
        var segmenter = new FakeSegmenter();

        await new DetectThenSegmentLabeler(detector, segmenter).DetectAsync("a.jpg", 0.4);

        var small = segmenter.Calls.Single(call => call.Box.Width < 100);
        var big = segmenter.Calls.Single(call => call.Box.Width > 100);
        Assert.NotNull(small.Region);
        Assert.True(small.Region!.Value.Width < 200);
        Assert.Null(big.Region);
    }

    [Fact]
    public async Task WhenSamGivesNothing_TheBoxStaysAsAFourCornerPolygonAndIsFlagged()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 40, 30));
        var segmenter = new FakeSegmenter { Answer = _ => null };

        var found = await new DetectThenSegmentLabeler(detector, segmenter).DetectAsync("a.jpg", 0.4);

        var label = Assert.Single(found);
        Assert.True(label.IsBoxOutline);
        Assert.Equal(4, label.Outline.Count);
        Assert.Equal(new PixelPoint(100, 100), label.Outline[0]);
        Assert.Equal(140, label.Outline.Max(point => point.X), 6);
        Assert.Equal(130, label.Outline.Max(point => point.Y), 6);
    }

    [Fact]
    public async Task ACutOutThatIsNotWhereTheBoxIs_IsReplacedByTheBox()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 40, 30));
        // SAM latched onto something else: far from the box.
        var segmenter = new FakeSegmenter { Answer = _ => new PixelRect(400, 400, 50, 50).Corners() };

        var found = await new DetectThenSegmentLabeler(detector, segmenter).DetectAsync("a.jpg", 0.4);

        Assert.True(Assert.Single(found).IsBoxOutline);
    }

    [Fact]
    public async Task ACutOutThatOnlyCoversPartOfAThinBox_IsKept()
    {
        var detector = new FakeDetector();
        // A tape diagonal across a box: its bounds are the box, but a thin part of it stays a real outline.
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 60, 60));
        var segmenter = new FakeSegmenter
        {
            Answer = box => new PixelPoint[] { new(box.X, box.Y), new(box.X + 8, box.Y), new(box.Right, box.Bottom), new(box.Right - 8, box.Bottom) }
        };

        var found = await new DetectThenSegmentLabeler(detector, segmenter).DetectAsync("a.jpg", 0.4);

        Assert.False(Assert.Single(found).IsBoxOutline);
    }

    [Fact]
    public async Task SmallBoxesCloseTogetherShareOneRegion()
    {
        var detector = new FakeDetector();
        for (var index = 0; index < 5; index++)
            detector.Boxes.Add(Box("tape", 0.9, 100 + index * 30, 100, 16, 10));
        var segmenter = new FakeSegmenter();

        await new DetectThenSegmentLabeler(detector, segmenter).DetectAsync("a.jpg", 0.4);

        Assert.Equal(5, segmenter.Calls.Count);
        Assert.Single(segmenter.Calls.Select(call => call.Region).Distinct());
    }

    [Fact]
    public async Task Unavailable_NamesTheDetectorFirstThenSam()
    {
        var detector = new FakeDetector { Unavailable = "no detect model" };
        var segmenter = new FakeSegmenter { Unavailable = "no sam" };
        var labeler = new DetectThenSegmentLabeler(detector, segmenter);

        Assert.Equal("no detect model", labeler.Unavailable);
        detector.Unavailable = null;
        Assert.Equal("no sam", labeler.Unavailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => labeler.DetectAsync("a.jpg", 0.4));
        segmenter.Unavailable = null;
        Assert.Null(labeler.Unavailable);
        Assert.Equal(new[] { "tape", "wire" }, labeler.ClassNames);
    }

    [Fact]
    public async Task Cancelling_StopsBeforeTheNextBox()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 40, 30));
        detector.Boxes.Add(Box("tape", 0.8, 100, 300, 40, 30));
        using var cancel = new CancellationTokenSource();
        var segmenter = new FakeSegmenter();
        segmenter.Answer = box =>
        {
            cancel.Cancel();
            return box.Corners();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DetectThenSegmentLabeler(detector, segmenter).DetectAsync("a.jpg", 0.4, cancel.Token));
        Assert.Single(segmenter.Calls);
    }

    [Fact]
    public async Task TheServiceCountsTheBoxOutlinesSoTheUserCanBeToldToFixThem()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 40, 30));
        detector.Boxes.Add(Box("tape", 0.8, 100, 300, 40, 30));
        var segmenter = new FakeSegmenter { Answer = box => box.Y < 200 ? null : box.Corners() };
        var service = new AutoLabelService(new DetectThenSegmentLabeler(detector, segmenter));
        var classes = new[] { new NTech.DataStudio.Domain.Classes.LabelClass(1, "tape", "#FF0000", null, 0) };

        var result = await service.LabelAsync("a.jpg", 5, classes, 0.4);

        Assert.Equal(2, result.Annotations.Count);
        Assert.Equal(1, result.BoxOutlineCount);
    }

    // ---- the planner ----

    [Fact]
    public void Plan_WithNoBoxes_IsEmpty() => Assert.Empty(SamRegionPlanner.Plan(1000, 800, Array.Empty<DetectedBox>()));

    [Fact]
    public void Plan_PutsEveryBoxInExactlyOneGroup()
    {
        var boxes = new List<DetectedBox>
        {
            Box("a", 0.9, 10, 10, 20, 20),
            Box("a", 0.9, 400, 10, 20, 20),
            Box("a", 0.9, 800, 700, 20, 20),
            Box("b", 0.9, 100, 100, 600, 500)
        };

        var groups = SamRegionPlanner.Plan(1000, 800, boxes);

        Assert.Equal(boxes.OrderBy(box => box.Box.Y).ThenBy(box => box.Box.X), groups.SelectMany(group => group.Boxes).OrderBy(box => box.Box.Y).ThenBy(box => box.Box.X));
        Assert.Equal(1, groups.Count(group => group.Region is null));
    }

    [Fact]
    public void Plan_EveryRegionContainsItsBoxesAndStaysInsideThePicture()
    {
        var boxes = new List<DetectedBox>
        {
            Box("a", 0.9, 0, 0, 10, 10),
            Box("a", 0.9, 990, 790, 10, 10),
            Box("a", 0.9, 500, 400, 30, 20),
            Box("a", 0.9, 520, 430, 30, 20)
        };

        foreach (var group in SamRegionPlanner.Plan(1000, 800, boxes))
        {
            if (group.Region is not { } region) continue;
            Assert.True(region.X >= 0 && region.Y >= 0 && region.Right <= 1000 && region.Bottom <= 800);
            Assert.All(group.Boxes, item =>
            {
                Assert.True(item.Box.X >= region.X && item.Box.Y >= region.Y && item.Box.Right <= region.Right && item.Box.Bottom <= region.Bottom);
            });
        }
    }

    [Fact]
    public void Plan_OnlyTinyBoxesGetARegion_AndTinyIsJudgedInSamsView()
    {
        // In a 2048 wide picture SAM sees everything at half size: 40 px becomes 20 (tiny), 60 px becomes 30 (not).
        var groups = SamRegionPlanner.Plan(2048, 1294, new[]
        {
            Box("a", 0.9, 100, 100, 40, 30),
            Box("a", 0.9, 800, 100, 60, 40)
        });

        Assert.Equal(2, groups.Count);
        Assert.Null(groups.Single(group => group.Boxes.Any(item => item.Box.Width > 50)).Region);
        Assert.NotNull(groups.Single(group => group.Boxes.Any(item => item.Box.Width < 50)).Region);
    }

    [Fact]
    public void Plan_FarApartSmallBoxesGetTheirOwnRegions()
    {
        var boxes = new List<DetectedBox>
        {
            Box("a", 0.9, 20, 20, 20, 20),
            Box("a", 0.9, 900, 700, 20, 20)
        };

        var groups = SamRegionPlanner.Plan(1000, 800, boxes);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.NotNull(group.Region));
    }

    // ---- the settings choose the labeler ----

    private SettingsService NewSettings() => new(new JsonSettingsStore(Path.Combine(_temp.Path, "settings.json")));

    private sealed class Labelers
    {
        public FakeLabeler Segment { get; } = new("segment");
        public FakeLabeler DetectSam { get; } = new("detect+sam");
        public FakeLabeler Detect { get; } = new("detect");
    }

    private static (ConfiguredAutoLabeler Configured, Labelers Fakes) Configure(SettingsService settings)
    {
        var fakes = new Labelers();
        return (new ConfiguredAutoLabeler(settings, fakes.Segment, fakes.DetectSam, fakes.Detect), fakes);
    }

    private sealed class FakeLabeler : IAutoLabeler
    {
        private readonly string _name;

        public FakeLabeler(string name) => _name = name;

        public string? Unavailable => null;
        public IReadOnlyList<string> ClassNames => new[] { _name };
        public int Calls { get; private set; }

        public Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AutoLabel>>(Array.Empty<AutoLabel>());
        }
    }

    [Fact]
    public async Task WhileTheAdvancedOptionIsOff_NothingRuns()
    {
        var settings = NewSettings();
        var (configured, fakes) = Configure(settings);

        Assert.False(settings.AutoLabelEnabled);
        foreach (var task in new[] { TaskKind.Detect, TaskKind.Segment })
        {
            var labeler = configured.For(task);
            Assert.Equal(ConfiguredAutoLabeler.Disabled, labeler.Unavailable);
            Assert.Empty(labeler.ClassNames);
            await Assert.ThrowsAsync<InvalidOperationException>(() => labeler.DetectAsync("a.jpg", 0.4));
        }

        Assert.Equal(0, fakes.Segment.Calls + fakes.DetectSam.Calls + fakes.Detect.Calls);
    }

    [Fact]
    public async Task TheTaskAndTheModeChooseTheLabeler_AndTheChangeTakesEffectAtOnce()
    {
        var settings = NewSettings();
        var (configured, fakes) = Configure(settings);
        settings.SetAutoLabelEnabled(true);

        Assert.Null(configured.For(TaskKind.Segment).Unavailable);
        Assert.Equal(new[] { "segment" }, configured.For(TaskKind.Segment).ClassNames);
        await configured.For(TaskKind.Segment).DetectAsync("a.jpg", 0.4);
        Assert.Equal((1, 0, 0), (fakes.Segment.Calls, fakes.DetectSam.Calls, fakes.Detect.Calls));

        settings.SetAutoLabelMode(AutoLabelMode.DetectSam);
        Assert.Equal(new[] { "detect+sam" }, configured.For(TaskKind.Segment).ClassNames);
        await configured.For(TaskKind.Segment).DetectAsync("a.jpg", 0.4);
        Assert.Equal((1, 1, 0), (fakes.Segment.Calls, fakes.DetectSam.Calls, fakes.Detect.Calls));

        // Detect does not depend on the Segment mode.
        await configured.For(TaskKind.Detect).DetectAsync("a.jpg", 0.4);
        Assert.Equal((1, 1, 1), (fakes.Segment.Calls, fakes.DetectSam.Calls, fakes.Detect.Calls));
    }

    // ---- the Detect task (decision 0019) ----

    [Fact]
    public async Task TheDetectLabeler_GivesTheDetectedBoxesAsBoxesWithTheirFourCorners()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.6, 100, 100, 40, 30));
        detector.Boxes.Add(Box("wire", 0.9, 300, 300, 100, 80));

        var found = await new BoxAutoLabeler(detector).DetectAsync("a.jpg", 0.4);

        Assert.Equal(0.4, detector.LastMinScore);
        Assert.Equal(new[] { "wire", "tape" }, found.Select(label => label.ClassName));
        Assert.All(found, label =>
        {
            Assert.Equal(NTech.DataStudio.Domain.Annotations.ShapeKind.Box, label.Shape);
            Assert.False(label.IsBoxOutline);
            Assert.Equal(4, label.Outline.Count);
        });
        Assert.Equal(new PixelPoint(300, 300), found[0].Outline[0]);
        Assert.Equal(new PixelPoint(400, 380), found[0].Outline[2]);
    }

    [Fact]
    public async Task TheServiceMakesBoxAnnotationsForDetect_AndPolygonAnnotationsForSegment()
    {
        var detector = new FakeDetector();
        detector.Boxes.Add(Box("tape", 0.9, 100, 100, 40, 30));
        var segmenter = new FakeSegmenter();
        var settings = NewSettings();
        settings.SetAutoLabelEnabled(true);
        var configured = new ConfiguredAutoLabeler(settings, new FakeLabeler("unused"), new DetectThenSegmentLabeler(detector, segmenter), new BoxAutoLabeler(detector));
        var service = new AutoLabelService(configured.For);
        var classes = new[] { new NTech.DataStudio.Domain.Classes.LabelClass(1, "tape", "#FF0000", null, 0) };

        settings.SetAutoLabelMode(AutoLabelMode.DetectSam);
        var boxes = await service.LabelAsync("a.jpg", 5, classes, 0.4, TaskKind.Detect);
        var polygons = await service.LabelAsync("a.jpg", 5, classes, 0.4, TaskKind.Segment);

        Assert.Equal(NTech.DataStudio.Domain.Annotations.ShapeKind.Box, Assert.Single(boxes.Annotations).Kind);
        Assert.Equal(NTech.DataStudio.Domain.Annotations.ShapeKind.Polygon, Assert.Single(polygons.Annotations).Kind);
        // The Detect labels never went through SAM.
        Assert.Single(segmenter.Calls);
    }

    [Fact]
    public void TheTasksOfAProjectThatCanBeLabeledAutomatically()
    {
        Assert.Equal(new[] { TaskKind.Detect }, AutoLabelService.TasksFor(new[] { TaskKind.Detect }));
        Assert.Equal(new[] { TaskKind.Segment }, AutoLabelService.TasksFor(new[] { TaskKind.Segment, TaskKind.Classify }));
        Assert.Equal(new[] { TaskKind.Detect, TaskKind.Segment }, AutoLabelService.TasksFor(new[] { TaskKind.Detect, TaskKind.Segment }));
        // A project that aims at none of them, or at nothing yet, is offered both.
        Assert.Equal(new[] { TaskKind.Detect, TaskKind.Segment }, AutoLabelService.TasksFor(Array.Empty<TaskKind>()));
        Assert.Equal(new[] { TaskKind.Detect, TaskKind.Segment }, AutoLabelService.TasksFor(new[] { TaskKind.Obb }));
    }

    [Fact]
    public void EachTaskHasItsOwnScoreThreshold_KeptAcrossARestart()
    {
        var settings = NewSettings();
        Assert.Equal(0.4, settings.MinScoreFor(TaskKind.Detect));
        Assert.Equal(0.4, settings.MinScoreFor(TaskKind.Segment));

        settings.SetAutoLabelDetectMinScore(0.25);
        settings.SetAutoLabelMinScore(0.6);

        var again = NewSettings();
        Assert.Equal(0.25, again.MinScoreFor(TaskKind.Detect));
        Assert.Equal(0.6, again.MinScoreFor(TaskKind.Segment));
    }

    // ---- the settings are kept ----

    [Fact]
    public void TheAdvancedOptionIsOffByDefault_AndTheChoicesSurviveARestart()
    {
        var first = NewSettings();
        Assert.False(first.AutoLabelEnabled);
        Assert.Equal(AutoLabelMode.Segment, first.AutoLabelMode);
        Assert.Null(first.AutoLabelDetectModel);

        first.SetAutoLabelEnabled(true);
        first.SetAutoLabelMode(AutoLabelMode.DetectSam);
        first.SetAutoLabelDetectModel(@"D:\models\detect\model.onnx");
        first.SetAutoLabelModel(@"D:\models\seg\model.onnx");

        var again = NewSettings();
        Assert.True(again.AutoLabelEnabled);
        Assert.Equal(AutoLabelMode.DetectSam, again.AutoLabelMode);
        Assert.Equal(@"D:\models\detect\model.onnx", again.AutoLabelDetectModel);
        Assert.Equal(@"D:\models\seg\model.onnx", again.AutoLabelModel);
    }

    [Fact]
    public void TurningTheOptionOffKeepsBothModelPaths()
    {
        var settings = NewSettings();
        settings.SetAutoLabelEnabled(true);
        settings.SetAutoLabelDetectModel("d.onnx");
        settings.SetAutoLabelModel("s.onnx");

        settings.SetAutoLabelEnabled(false);

        var again = NewSettings();
        Assert.False(again.AutoLabelEnabled);
        Assert.Equal("d.onnx", again.AutoLabelDetectModel);
        Assert.Equal("s.onnx", again.AutoLabelModel);
    }

    [Fact]
    public void AFileFromBeforeTheOption_ReadsAsOffWithTheSegmentModelKept()
    {
        File.WriteAllText(Path.Combine(_temp.Path, "settings.json"), "{ \"Version\": 1, \"AutoLabelModel\": \"s.onnx\", \"AutoLabelMinScore\": 0.5 }");

        var settings = NewSettings();

        Assert.False(settings.AutoLabelEnabled);
        Assert.Equal(AutoLabelMode.Segment, settings.AutoLabelMode);
        Assert.Equal("s.onnx", settings.AutoLabelModel);
        Assert.Equal(0.5, settings.AutoLabelMinScore);
    }
}
