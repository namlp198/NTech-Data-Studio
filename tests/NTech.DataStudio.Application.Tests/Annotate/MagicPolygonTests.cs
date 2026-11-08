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
        public int Prepared { get; private set; }
        public List<List<SegmentPrompt>> Calls { get; } = new();
        public TimeSpan Delay { get; set; }
        public bool ReturnNothing { get; set; }
        public Exception? Failure { get; set; }

        public Task PrepareAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            lock (_lock) Prepared++;
            return Task.Delay(Delay, cancellationToken);
        }

        public async Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
            string imagePath, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken = default)
        {
            lock (_lock) Calls.Add(prompts.ToList());
            await Task.Delay(Delay, cancellationToken);
            if (Failure is not null) throw Failure;
            if (ReturnNothing) return null;

            // A 20 x 20 square around the last positive point.
            var centre = prompts.Last(prompt => prompt.Positive).Position;
            return new[]
            {
                new PixelPoint(centre.X - 10, centre.Y - 10), new PixelPoint(centre.X + 10, centre.Y - 10),
                new PixelPoint(centre.X + 10, centre.Y + 10), new PixelPoint(centre.X - 10, centre.Y + 10)
            };
        }

        public int CallCount
        {
            get { lock (_lock) return Calls.Count; }
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

    [Fact]
    public void ToolNeedsNothingUntilItIsChosen_ThenAnalysesThePictureOnce()
    {
        Assert.Equal(0, _segmenter.Prepared);

        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _segmenter.Prepared == 1 && _magic.Status is null, "the picture to be analysed");

        _editor.SetTool(AnnotateTool.Select);
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _segmenter.Prepared == 2, "the picture to be analysed again after a tool change");
    }

    [Fact]
    public void Hover_ShowsTheOutlineUnderThePointer()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        _editor.PointerMove(new PixelPoint(100, 120), 4);

        WaitFor(() => _magic.Preview is not null, "a preview");
        Assert.Equal(new PixelPoint(90, 110), _magic.Preview![0]);
    }

    [Fact]
    public void Hover_IsLatestWins_WhileTheSegmenterIsBusy()
    {
        _segmenter.Delay = TimeSpan.FromMilliseconds(120);
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        for (var x = 10; x <= 300; x += 10)
            _editor.PointerMove(new PixelPoint(x, 50), 4);

        WaitFor(() => _magic.Preview is { } preview && preview[0].X == 290, "the preview of the last position");
        // 30 moves must not mean 30 calls: the ones in between were replaced.
        Assert.True(_segmenter.CallCount < 10, $"{_segmenter.CallCount} calls");
    }

    [Fact]
    public void Click_MakesAPolygonLabelOfTheActiveClassAndSelectsIt()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        _editor.PointerDown(new PixelPoint(100, 50), 4);

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
    public void ShiftClickAndRightClick_AddPointsThatRefineTheOutlineBeforeItIsAccepted()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        _editor.PointerDown(new PixelPoint(100, 100), 4, refine: true);
        Assert.True(_editor.SecondaryDown(new PixelPoint(300, 300)));
        Assert.Equal(new[] { true, false }, _magic.Prompts.Select(prompt => prompt.Positive));
        Assert.Empty(_editor.Document!.Annotations);

        _editor.PointerDown(new PixelPoint(105, 105), 4);

        WaitFor(() => _editor.Document!.Annotations.Count == 1, "the label");
        // The accepting call carried the two earlier points and the click.
        var last = _segmenter.Calls.Last();
        Assert.Equal(3, last.Count);
        Assert.False(last[1].Positive);
        Assert.Empty(_magic.Prompts);
    }

    [Fact]
    public void Backspace_TakesTheLastAddedPointBack()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");
        _editor.PointerDown(new PixelPoint(100, 100), 4, refine: true);
        _editor.SecondaryDown(new PixelPoint(300, 300));

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
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        _editor.PointerDown(new PixelPoint(100, 100), 4);

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
        _editor.PointerDown(new PixelPoint(100, 100), 4);

        Assert.Equal("Chưa có model SAM.", _editor.Message);
        Assert.Equal(0, _segmenter.CallCount);
        Assert.Empty(_editor.Document!.Annotations);
    }

    [Fact]
    public void ASegmenterFailureBecomesAStatusNotACrash()
    {
        _segmenter.Failure = new InvalidOperationException("model hỏng");
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        _editor.PointerMove(new PixelPoint(100, 100), 4);

        WaitFor(() => _magic.Status == "model hỏng", "the failure shown");
        Assert.Null(_magic.Preview);
    }

    [Fact]
    public void AnObjectThatCannotBeCutOut_SaysSoAndAddsNothing()
    {
        _segmenter.ReturnNothing = true;
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");

        _editor.PointerDown(new PixelPoint(100, 100), 4);

        WaitFor(() => _magic.Status is not null, "the message");
        Assert.Contains("Không tách được", _magic.Status);
        Assert.Empty(_editor.Document!.Annotations);
    }

    [Fact]
    public void LeavingTheTool_DropsThePreviewAndThePoints()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");
        _editor.PointerDown(new PixelPoint(100, 100), 4, refine: true);
        _editor.PointerMove(new PixelPoint(120, 120), 4);
        WaitFor(() => _magic.Preview is not null, "a preview");

        _editor.Escape();

        Assert.Equal(AnnotateTool.Select, _editor.Tool);
        Assert.Null(_magic.Preview);
        Assert.Empty(_magic.Prompts);
    }

    [Fact]
    public void ANewPicture_StartsFromScratch()
    {
        _editor.SetTool(AnnotateTool.MagicPolygon);
        WaitFor(() => _magic.Status is null, "ready");
        _editor.PointerDown(new PixelPoint(100, 100), 4, refine: true);

        _magic.SetImage("other.jpg");

        Assert.Empty(_magic.Prompts);
        Assert.Null(_magic.Preview);
        WaitFor(() => _segmenter.Prepared == 2, "the new picture to be analysed");
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
