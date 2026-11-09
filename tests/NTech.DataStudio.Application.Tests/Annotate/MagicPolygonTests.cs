using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Tests.Annotate;

/// <summary>Decision 0016: the Magic polygon tool, without a model: a fake segmenter stands in.</summary>
public class MagicPolygonTests : IDisposable
{
    private sealed class FakeSegmenter : ISmartSegmenter
    {
        private readonly object _lock = new();

        public string? Unavailable { get; set; }
        public List<PixelRect?> Prepared { get; } = new();
        public List<(PixelRect? Region, SegmentRequest Request)> Calls { get; } = new();
        public TimeSpan Delay { get; set; }
        public bool ReturnNothing { get; set; }
        public Exception? Failure { get; set; }

        public int PreparedCount
        {
            get { lock (_lock) return Prepared.Count; }
        }

        public int CallCount
        {
            get { lock (_lock) return Calls.Count; }
        }

        public Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default)
        {
            lock (_lock) Prepared.Add(region);
            return Task.Delay(Delay, cancellationToken);
        }

        public async Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
            string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default)
        {
            lock (_lock) Calls.Add((region, request));
            await Task.Delay(Delay, cancellationToken);
            if (Failure is not null) throw Failure;
            if (ReturnNothing) return null;

            // A 20 x 20 square around the last positive point, or the box itself.
            if (request.Box is { } box) return box.Corners();
            var centre = request.Points.Last(prompt => prompt.Positive).Position;
            return new[]
            {
                new PixelPoint(centre.X - 10, centre.Y - 10), new PixelPoint(centre.X + 10, centre.Y - 10),
                new PixelPoint(centre.X + 10, centre.Y + 10), new PixelPoint(centre.X - 10, centre.Y + 10)
            };
        }
    }

    private readonly AnnotateFixture _fixture = new();
    private readonly FakeSegmenter _segmenter = new();
    private readonly MagicPolygonController _magic;
    private readonly AnnotationEditor _editor;
    private readonly long _classId;

    public MagicPolygonTests()
    {
        var store = _fixture.OpenStore();
        var service = _fixture.Service(store);
        _classId = new ClassService(store).Add("connector").Id;
        _magic = new MagicPolygonController(_segmenter);
        _editor = new AnnotationEditor(service, new AnnotationClipboard()) { ActiveClassId = _classId, Magic = _magic };
        _editor.Open(_fixture.OpenImage(service, 1));
        _magic.SetImage("master.jpg");
    }

    public void Dispose() => _fixture.Dispose();

    private static void WaitFor(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, $"timed out waiting for: {what}");
            Thread.Sleep(10);
        }
    }

    private void Click(double x, double y, bool refine = false)
    {
        _editor.PointerDown(new PixelPoint(x, y), 4, refine: refine);
        _editor.PointerUp(new PixelPoint(x, y), 4);
    }

    private void Ready()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");
    }

    private void BoxReady()
    {
        _magic.SetBoxMode(true);
        Ready();
    }

    [Fact]
    public void ToolNeedsNothingUntilItIsChosen_ThenAnalysesThePictureOnce()
    {
        Assert.Equal(0, _segmenter.PreparedCount);

        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _segmenter.PreparedCount == 1 && _magic.Status is null, "the picture to be analysed");

        _editor.SetTool(AnnotateTool.Select);
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _segmenter.PreparedCount == 2, "the picture to be analysed again after a tool change");
    }

    [Fact]
    public void Hover_ShowsTheOutlineUnderThePointer()
    {
        Ready();

        _editor.PointerMove(new PixelPoint(100, 120), 4);

        WaitFor(() => _magic.Preview is not null, "a preview");
        Assert.Equal(new PixelPoint(90, 110), _magic.Preview![0]);
    }

    [Fact]
    public void Hover_IsLatestWins_WhileTheSegmenterIsBusy()
    {
        _segmenter.Delay = TimeSpan.FromMilliseconds(120);
        Ready();

        for (var x = 10; x <= 300; x += 10)
            _editor.PointerMove(new PixelPoint(x, 50), 4);

        WaitFor(() => _magic.Preview is { } preview && preview[0].X == 290, "the preview of the last position");
        // 30 moves must not mean 30 calls: the ones in between were replaced.
        Assert.True(_segmenter.CallCount < 10, $"{_segmenter.CallCount} calls");
    }

    [Fact]
    public void Click_MakesAPolygonLabelOfTheActiveClassAndSelectsIt()
    {
        Ready();

        Click(100, 50);

        WaitFor(() => _editor.Document!.Annotations.Count == 1, "the label");
        var label = _editor.Document!.Annotations.Single();
        Assert.Equal(ShapeKind.Polygon, label.Kind);
        Assert.Equal(_classId, label.ClassId);
        Assert.Equal(label.Id, _editor.SelectedId);
        Assert.Null(_magic.Preview);
        Assert.Empty(_magic.Prompts);
        // The tool stays on for the next object.
        Assert.Equal(AnnotateTool.MagicPolygon, _editor.Tool);
    }

    [Fact]
    public void ADraggedBox_IsWhatGetsSegmentedAndNotTheClick()
    {
        BoxReady();

        _editor.PointerDown(new PixelPoint(40, 20), 4);
        _editor.PointerMove(new PixelPoint(80, 40), 4);
        Assert.Equal(new PixelRect(40, 20, 40, 20), _editor.MagicDragBox);
        _editor.PointerMove(new PixelPoint(120, 60), 4);
        _editor.PointerUp(new PixelPoint(120, 60), 4);

        WaitFor(() => _editor.Document!.Annotations.Count == 1, "the label");
        // Hover work may have called the segmenter too; the accepting call is the one with the box.
        var request = _segmenter.Calls.Last(call => call.Request.Box is not null).Request;
        Assert.Equal(new PixelRect(40, 20, 80, 40), request.Box);
        Assert.Empty(request.Points);
        Assert.Null(_editor.MagicDragBox);
        Assert.Equal(ShapeKind.Polygon, _editor.Document!.Annotations.Single().Kind);
    }

    [Fact]
    public void InBoxMode_HoverPointsAndClicksDoNothingButADragDoes()
    {
        BoxReady();

        _editor.PointerMove(new PixelPoint(100, 50), 4);
        Click(100, 50, refine: true);
        Assert.False(_editor.SecondaryDown(new PixelPoint(150, 80)));
        Click(100, 50);
        Thread.Sleep(150);

        Assert.Equal(0, _segmenter.CallCount);
        Assert.Empty(_magic.Prompts);
        Assert.Null(_magic.Preview);
        Assert.Empty(_editor.Document!.Annotations);
        Assert.NotNull(_editor.Message);
    }

    [Fact]
    public void AMovementTooSmallToBeABox_IsStillAClick()
    {
        Ready();

        _editor.PointerDown(new PixelPoint(100, 50), 4);
        _editor.PointerMove(new PixelPoint(102, 51), 4);
        Assert.Null(_editor.MagicDragBox);
        _editor.PointerUp(new PixelPoint(102, 51), 4);

        WaitFor(() => _editor.Document!.Annotations.Count == 1, "the label");
        Assert.All(_segmenter.Calls, call => Assert.Null(call.Request.Box));
    }

    [Fact]
    public void ShiftClickAndRightClick_AddPointsThatRefineTheOutlineBeforeItIsAccepted()
    {
        Ready();

        Click(100, 50, refine: true);
        Assert.True(_editor.SecondaryDown(new PixelPoint(150, 80)));
        Assert.Equal(new[] { true, false }, _magic.Prompts.Select(prompt => prompt.Positive));
        Assert.Empty(_editor.Document!.Annotations);

        Click(105, 55);

        WaitFor(() => _editor.Document!.Annotations.Count == 1, "the label");
        // The accepting call carried the two earlier points and the click (hover work calls the segmenter too).
        Assert.Contains(_segmenter.Calls, call => call.Request.Points.Count == 3 && !call.Request.Points[1].Positive);
        Assert.Empty(_magic.Prompts);
    }

    [Fact]
    public void Backspace_TakesTheLastAddedPointBack()
    {
        Ready();
        Click(100, 50, refine: true);
        _editor.SecondaryDown(new PixelPoint(150, 80));

        _editor.UndoPoint();

        Assert.Single(_magic.Prompts);
        Assert.True(_magic.Prompts[0].Positive);
    }

    [Fact]
    public void RightClickOutsideTheMagicTool_IsNotTaken()
    {
        Assert.False(_editor.SecondaryDown(new PixelPoint(10, 10)));
    }

    [Fact]
    public void WithoutAClass_NothingIsSegmentedAndTheUserIsTold()
    {
        _editor.ActiveClassId = null;
        Ready();

        Click(100, 50);

        Assert.Contains("class", _editor.Message);
        Assert.Equal(0, _segmenter.CallCount);
    }

    [Fact]
    public void WhenTheModelIsMissing_TheToolSaysWhyAndNeverCallsTheSegmenter()
    {
        _segmenter.Unavailable = "Chưa có model SAM.";
        _editor.SetTool(AnnotateTool.MagicPolygon);

        Assert.Equal("Chưa có model SAM.", _magic.Status);
        _editor.PointerMove(new PixelPoint(100, 100), 4);
        Click(100, 100);

        Assert.Equal("Chưa có model SAM.", _editor.Message);
        Assert.Equal(0, _segmenter.CallCount);
        Assert.Empty(_editor.Document!.Annotations);
    }

    [Fact]
    public void ASegmenterFailureBecomesAStatusNotACrash()
    {
        _segmenter.Failure = new InvalidOperationException("model hỏng");
        Ready();

        _editor.PointerMove(new PixelPoint(100, 100), 4);

        WaitFor(() => _magic.Status == "model hỏng", "the failure shown");
        Assert.Null(_magic.Preview);
    }

    [Fact]
    public void AnObjectThatCannotBeCutOut_SaysSoAndAddsNothing()
    {
        _segmenter.ReturnNothing = true;
        Ready();

        Click(100, 50);

        WaitFor(() => _magic.Status is not null, "the message");
        Assert.Contains("Không tách được", _magic.Status);
        Assert.Empty(_editor.Document!.Annotations);
    }

    [Fact]
    public void LeavingTheTool_DropsThePreviewThePointsAndADragInProgress()
    {
        Ready();
        Click(100, 50, refine: true);
        _editor.PointerMove(new PixelPoint(120, 60), 4);
        WaitFor(() => _magic.Preview is not null, "a preview");

        _editor.Escape();

        Assert.Equal(AnnotateTool.Select, _editor.Tool);
        Assert.Null(_magic.Preview);
        Assert.Empty(_magic.Prompts);
    }

    [Fact]
    public void LeavingTheTool_DropsADragInProgress()
    {
        BoxReady();
        _editor.PointerDown(new PixelPoint(10, 10), 4);
        _editor.PointerMove(new PixelPoint(60, 40), 4);
        Assert.NotNull(_editor.MagicDragBox);

        _editor.Escape();

        Assert.Null(_editor.MagicDragBox);
    }

    [Fact]
    public void ANewPicture_StartsFromScratch()
    {
        Ready();
        Click(100, 50, refine: true);

        _magic.SetImage("other.jpg");

        Assert.Empty(_magic.Prompts);
        Assert.Null(_magic.Preview);
        WaitFor(() => _segmenter.PreparedCount == 2, "the new picture to be analysed");
    }

    // ---- zooming in analyses just the part on screen ----

    [Fact]
    public void ZoomedIn_OnlyThePartOnScreenIsAnalysed_AndZoomingBackReturnsToTheWholePicture()
    {
        BoxReady();

        _magic.SetView(new PixelRect(60, 30, 50, 25), 200, 100);
        WaitFor(() => _segmenter.PreparedCount == 2, "the zoomed region to be analysed");
        var region = _segmenter.Prepared[1];
        Assert.NotNull(region);
        Assert.True(region!.Value.X <= 60 && region.Value.Right >= 110, "the region holds what is on screen");
        Assert.Equal(region, _magic.Region);

        _magic.SetView(new PixelRect(0, 0, 200, 100), 200, 100);
        WaitFor(() => _segmenter.PreparedCount == 3, "the whole picture to be analysed again");
        Assert.Null(_segmenter.Prepared[2]);
        Assert.Null(_magic.Region);
    }

    [Fact]
    public void ZoomedIn_ABoxUsesTheRegion()
    {
        BoxReady();
        _magic.SetView(new PixelRect(60, 30, 50, 25), 200, 100);
        WaitFor(() => _segmenter.PreparedCount == 2 && _magic.Status is null, "the zoomed region");
        var region = _magic.Region!.Value;

        _editor.PointerDown(new PixelPoint(70, 35), 4);
        _editor.PointerMove(new PixelPoint(100, 50), 4);
        _editor.PointerUp(new PixelPoint(100, 50), 4);

        WaitFor(() => _editor.Document!.Annotations.Count == 1, "the label");
        Assert.Equal(region, _segmenter.Calls.Last().Region);
    }

    [Fact]
    public void SwitchingBoxModeOff_GoesBackToTheWholePictureAndThePointer()
    {
        BoxReady();
        _magic.SetView(new PixelRect(60, 30, 50, 25), 200, 100);
        WaitFor(() => _magic.Region is not null && _magic.Status is null, "the zoomed region");

        _magic.SetBoxMode(false);
        WaitFor(() => _magic.Region is null && _magic.Status is null, "the whole picture");
        _magic.SetView(new PixelRect(60, 30, 50, 25), 200, 100);
        Thread.Sleep(500);
        Assert.Null(_magic.Region);

        _editor.PointerMove(new PixelPoint(100, 50), 4);
        WaitFor(() => _magic.Preview is not null, "a preview again");
    }

    [Fact]
    public void SwitchingBoxModeOn_UsesTheViewAlreadyOnScreen()
    {
        Ready();
        _magic.SetView(new PixelRect(60, 30, 50, 25), 200, 100);
        Thread.Sleep(500);
        Assert.Null(_magic.Region);

        _magic.SetBoxMode(true);

        WaitFor(() => _magic.Region is not null, "the zoomed region");
    }

    [Fact]
    public void PanningALittle_DoesNotStartANewAnalysis()
    {
        BoxReady();
        _magic.SetView(new PixelRect(60, 30, 50, 25), 200, 100);
        WaitFor(() => _segmenter.PreparedCount == 2, "the zoomed region");

        _magic.SetView(new PixelRect(65, 33, 50, 25), 200, 100);
        Thread.Sleep(600);

        Assert.Equal(2, _segmenter.PreparedCount);
    }

    [Fact]
    public void PanningFarOut_AnalysesTheNewPart()
    {
        BoxReady();
        _magic.SetView(new PixelRect(10, 10, 30, 15), 200, 100);
        WaitFor(() => _segmenter.PreparedCount == 2, "the first region");

        _magic.SetView(new PixelRect(150, 70, 30, 15), 200, 100);

        WaitFor(() => _segmenter.PreparedCount == 3, "the second region");
        Assert.True(_segmenter.Prepared[2]!.Value.X > 100);
    }

    [Theory]
    [InlineData(0, 0, 200, 100, null)]       // the whole picture
    [InlineData(10, 10, 150, 80, null)]      // most of it: no point in zooming
    public void ChooseRegion_StaysWholeUnlessWellZoomedIn(double x, double y, double w, double h, double? expected)
    {
        var region = MagicPolygonController.ChooseRegion(new PixelRect(x, y, w, h), 200, 100, null);
        Assert.Equal(expected is null, region is null);
    }

    [Fact]
    public void ChooseRegion_AddsAMarginAndStaysInsideThePicture()
    {
        var region = MagicPolygonController.ChooseRegion(new PixelRect(0, 0, 40, 20), 200, 100, null)!.Value;

        Assert.Equal(0, region.X);
        Assert.Equal(0, region.Y);
        Assert.Equal(50, region.Width, 3);
        Assert.Equal(25, region.Height, 3);

        var inside = MagicPolygonController.ChooseRegion(new PixelRect(80, 40, 40, 20), 200, 100, null)!.Value;
        Assert.Equal(70, inside.X, 3);
        Assert.Equal(60, inside.Width, 3);
    }

    [Fact]
    public void ChooseRegion_KeepsTheCurrentOneWhileItStillHoldsTheScreen_ButNotWhenItIsMuchTooBig()
    {
        var current = new PixelRect(50, 25, 100, 50);

        Assert.Equal(current, MagicPolygonController.ChooseRegion(new PixelRect(70, 35, 60, 30), 200, 100, current));

        // The screen shrank to a fraction of the region: analyse it more closely.
        var closer = MagicPolygonController.ChooseRegion(new PixelRect(90, 45, 20, 10), 200, 100, current)!.Value;
        Assert.NotEqual(current, closer);
        Assert.True(closer.Area < current.Area);
    }

    [Theory]
    [InlineData(TaskKind.Segment, true)]
    [InlineData(null, true)]
    [InlineData(TaskKind.Detect, false)]
    [InlineData(TaskKind.Obb, false)]
    [InlineData(TaskKind.Classify, false)]
    public void TheToolBelongsToSegmentProjects(TaskKind? target, bool allowed) =>
        Assert.Equal(allowed, TaskTools.Allows(target, AnnotateTool.MagicPolygon));
}
