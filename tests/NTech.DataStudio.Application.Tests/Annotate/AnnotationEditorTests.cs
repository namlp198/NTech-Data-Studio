using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Annotate;

public class AnnotationEditorTests : IDisposable
{
    private const double Tolerance = 4;

    private readonly AnnotateFixture _fixture = new();
    private readonly Application.Abstractions.IAnnotationStore _store;
    private readonly AnnotateService _service;
    private readonly AnnotationClipboard _clipboard = new();
    private readonly AnnotationEditor _editor;
    private readonly long _classId;
    private readonly long _otherClassId;

    public AnnotationEditorTests()
    {
        _store = _fixture.OpenStore();
        _service = _fixture.Service(_store);
        var classes = new ClassService(_store);
        _classId = classes.Add("connector").Id;
        _otherClassId = classes.Add("tape").Id;
        _editor = new AnnotationEditor(_service, _clipboard) { ActiveClassId = _classId };
        _editor.Open(_fixture.OpenImage(_service, 1));
    }

    public void Dispose() => _fixture.Dispose();

    private ImageDocument Document => _editor.Document!;

    private void Click(double x, double y, int count = 1)
    {
        _editor.PointerDown(new PixelPoint(x, y), Tolerance, count);
        _editor.PointerUp(new PixelPoint(x, y), Tolerance);
    }

    private void Drag(double x1, double y1, double x2, double y2)
    {
        _editor.PointerDown(new PixelPoint(x1, y1), Tolerance);
        _editor.PointerMove(new PixelPoint((x1 + x2) / 2, (y1 + y2) / 2), Tolerance);
        _editor.PointerMove(new PixelPoint(x2, y2), Tolerance);
        _editor.PointerUp(new PixelPoint(x2, y2), Tolerance);
    }

    private void DrawBox(double x1, double y1, double x2, double y2)
    {
        _editor.SetTool(AnnotateTool.Box);
        Drag(x1, y1, x2, y2);
        _editor.SetTool(AnnotateTool.Select);
    }

    [Theory]
    [InlineData(AnnotateTool.Box)]
    [InlineData(AnnotateTool.Polygon)]
    [InlineData(AnnotateTool.RotatedBox)]
    [InlineData(AnnotateTool.Tag)]
    [InlineData(AnnotateTool.Pan)]
    public void Escape_FromAnyOtherToolGoesBackToSelect(AnnotateTool tool)
    {
        _editor.SetTool(tool);

        _editor.Escape();

        Assert.Equal(AnnotateTool.Select, _editor.Tool);
    }

    [Fact]
    public void Escape_WhileDrawingDropsTheShapeAndGoesBackToSelectInOnePress()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Click(60, 10);
        Assert.NotNull(_editor.Drawing);

        _editor.Escape();

        Assert.Null(_editor.Drawing);
        Assert.Equal(AnnotateTool.Select, _editor.Tool);
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void Escape_InSelectClearsTheSelectionAndKeepsTheLabel()
    {
        DrawBox(20, 20, 80, 60);
        Assert.NotNull(_editor.SelectedId);

        _editor.Escape();

        Assert.Null(_editor.SelectedId);
        Assert.Equal(AnnotateTool.Select, _editor.Tool);
        Assert.Single(Document.Annotations);
    }

    [Fact]
    public void PanTool_NeitherDrawsNorSelects()
    {
        DrawBox(20, 20, 80, 60);
        _editor.Select(null);
        _editor.SetTool(AnnotateTool.Pan);

        Drag(30, 30, 70, 50);
        Click(50, 40);

        Assert.Single(Document.Annotations);
        Assert.Null(_editor.SelectedId);
    }

    [Fact]
    public void CycleTool_StepsThroughTheRailInBothDirectionsAndWraps()
    {
        Assert.Equal(AnnotateTool.Select, _editor.Tool);

        _editor.CycleTool(1);
        Assert.Equal(AnnotateTool.Box, _editor.Tool);
        _editor.CycleTool(-1);
        _editor.CycleTool(-1);
        Assert.Equal(AnnotateTool.Pan, _editor.Tool);
        _editor.CycleTool(1);
        Assert.Equal(AnnotateTool.Select, _editor.Tool);
    }

    [Fact]
    public void CycleTool_DropsAShapeThatIsHalfDrawn()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Assert.NotNull(_editor.Drawing);

        _editor.CycleTool(1);

        Assert.Null(_editor.Drawing);
        Assert.Equal(AnnotateTool.RotatedBox, _editor.Tool);
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void LabelAt_FindsTheTopmostVisibleLabelUnderThePoint()
    {
        DrawBox(20, 20, 80, 60);
        DrawBox(40, 30, 120, 80);
        var second = Document.Annotations[1];

        Assert.Equal(second.Id, _editor.LabelAt(new PixelPoint(60, 40), Tolerance)!.Id);
        Assert.Equal(Document.Annotations[0].Id, _editor.LabelAt(new PixelPoint(25, 25), Tolerance)!.Id);
        Assert.Null(_editor.LabelAt(new PixelPoint(180, 95), Tolerance));

        _editor.ToggleShapesVisible();
        Assert.Null(_editor.LabelAt(new PixelPoint(60, 40), Tolerance));
    }

    [Fact]
    public void RightClickMenuActions_ChangeClassDeleteAndSwitchToSelect()
    {
        DrawBox(20, 20, 80, 60);
        _editor.SetTool(AnnotateTool.Box);
        var label = _editor.LabelAt(new PixelPoint(50, 40), Tolerance)!;
        _editor.Select(label.Id);

        // What the menu items call, in order: Class, Select, Delete.
        Assert.True(_editor.ChangeSelectedClass(_otherClassId));
        Assert.Equal(_otherClassId, Document.Annotations.Single().ClassId);
        _editor.SetTool(AnnotateTool.Select);
        Assert.Equal(AnnotateTool.Select, _editor.Tool);
        Assert.Equal(label.Id, _editor.SelectedId);
        Assert.True(_editor.DeleteSelected());
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void IsOverLabel_IsTrueOnALabel_AndFalseOnEmptyPicture()
    {
        DrawBox(20, 20, 80, 60);
        _editor.Select(null);

        Assert.True(_editor.IsOverLabel(new PixelPoint(50, 40), Tolerance));
        Assert.False(_editor.IsOverLabel(new PixelPoint(150, 90), Tolerance));
    }

    [Fact]
    public void IsOverLabel_ASelectedLabelsVertexCountsEvenJustOutsideTheShape()
    {
        DrawBox(20, 20, 80, 60);

        // Two pixels outside the corner, inside the handle tolerance of the selected box.
        Assert.True(_editor.IsOverLabel(new PixelPoint(18, 18), Tolerance));
        _editor.Select(null);
        Assert.False(_editor.IsOverLabel(new PixelPoint(10, 10), Tolerance));
    }

    [Fact]
    public void IsOverLabel_HiddenLabelsAreNotGrabbed()
    {
        DrawBox(20, 20, 80, 60);
        _editor.Select(null);
        _editor.ToggleShapesVisible();

        Assert.False(_editor.IsOverLabel(new PixelPoint(50, 40), Tolerance));
    }

    [Fact]
    public void Box_DragAddsASavedLabelOfTheActiveClassAndSelectsIt()
    {
        _editor.SetTool(AnnotateTool.Box);

        Drag(20, 20, 80, 60);

        var box = Document.Annotations.Single();
        Assert.Equal((_classId, ShapeKind.Box), (box.ClassId, box.Kind));
        Assert.Equal(new PixelRect(20, 20, 60, 40), PolygonMath.BoundingBox(box.Points));
        Assert.Equal(box.Id, _editor.SelectedId);
        Assert.Equal(1, _fixture.Scalar("SELECT COUNT(*) FROM annotations"));
        Assert.Equal(AnnotateTool.Box, _editor.Tool);
    }

    [Fact]
    public void Box_ClickWithoutDraggingAddsNothing()
    {
        _editor.SetTool(AnnotateTool.Box);

        Click(50, 50);

        Assert.Empty(Document.Annotations);
        Assert.Null(_editor.Drawing);
    }

    [Fact]
    public void Box_DraggedOutsideTheImageIsClamped()
    {
        _editor.SetTool(AnnotateTool.Box);

        Drag(150, 50, 900, 900);

        Assert.Equal(new PixelRect(150, 50, 50, 50), PolygonMath.BoundingBox(Document.Annotations.Single().Points));
    }

    [Fact]
    public void Drawing_WithoutAClassAsksForOneAndStartsNothing()
    {
        _editor.ActiveClassId = null;
        _editor.SetTool(AnnotateTool.Box);

        Drag(20, 20, 80, 60);

        Assert.Empty(Document.Annotations);
        Assert.Null(_editor.Drawing);
        Assert.Contains("class", _editor.Message);
    }

    [Fact]
    public void Polygon_ClicksAddVerticesAndFinishSavesIt()
    {
        _editor.SetTool(AnnotateTool.Polygon);

        Click(10, 10);
        Click(90, 10);
        Click(50, 80);
        Assert.Empty(Document.Annotations);
        Assert.True(_editor.Finish());

        var polygon = Document.Annotations.Single();
        Assert.Equal(ShapeKind.Polygon, polygon.Kind);
        Assert.Equal(new PixelPoint[] { new(10, 10), new(90, 10), new(50, 80) }, polygon.Points);
    }

    [Fact]
    public void Polygon_ClickingTheFirstVertexClosesIt()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Click(90, 10);
        Click(50, 80);

        Click(11, 11);

        Assert.Equal(3, Document.Annotations.Single().Points.Count);
        Assert.Null(_editor.Drawing);
    }

    [Fact]
    public void Polygon_FinishWithTooFewPointsKeepsDrawingAndSaysWhy()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Click(90, 10);

        Assert.False(_editor.Finish());

        Assert.NotNull(_editor.Drawing);
        Assert.NotNull(_editor.Message);
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void Polygon_SelfIntersectionCannotBeFinished()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(0, 0);
        Click(50, 50);
        Click(50, 0);
        Click(0, 50);

        Assert.False(_editor.Finish());

        Assert.Contains("tự cắt", _editor.Message);
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void Polygon_BackspaceDropsTheLastVertexAndTheLastOneCancels()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Click(90, 10);

        _editor.UndoPoint();
        Assert.Single(_editor.Drawing!.Vertices);

        _editor.UndoPoint();
        Assert.Null(_editor.Drawing);
    }

    [Fact]
    public void Escape_CancelsTheShapeBeingDrawn()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Click(90, 10);

        _editor.Cancel();

        Assert.Null(_editor.Drawing);
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void RotatedBox_DragTheAxisThenClickTheWidth()
    {
        _editor.SetTool(AnnotateTool.RotatedBox);

        Drag(30, 50, 130, 50);
        Assert.Equal(2, _editor.Drawing!.Vertices.Count);
        Assert.Empty(Document.Annotations);

        _editor.PointerMove(new PixelPoint(80, 65), Tolerance);
        _editor.PointerDown(new PixelPoint(80, 65), Tolerance);
        _editor.PointerUp(new PixelPoint(80, 65), Tolerance);

        var obb = Document.Annotations.Single();
        Assert.Equal(ShapeKind.RotatedBox, obb.Kind);
        Assert.Equal(100 * 30, PolygonMath.Area(obb.Points), 6);
    }

    [Fact]
    public void RotatedBox_ThreeClicksAlsoWork()
    {
        _editor.SetTool(AnnotateTool.RotatedBox);

        Click(30, 50);
        Click(130, 50);
        Click(80, 70);

        Assert.Equal(100 * 40, PolygonMath.Area(Document.Annotations.Single().Points), 6);
    }

    [Fact]
    public void Select_ClickPicksTheTopmostShapeAndEmptySpaceClears()
    {
        DrawBox(20, 20, 100, 80);
        DrawBox(60, 30, 140, 90);
        var top = Document.Annotations[1];

        Click(80, 50);
        Assert.Equal(top.Id, _editor.SelectedId);

        Click(190, 5);
        Assert.Null(_editor.SelectedId);
    }

    [Fact]
    public void Select_DragMovesTheShapeAndSavesOnRelease()
    {
        DrawBox(20, 20, 60, 50);
        var box = Document.Annotations.Single();

        _editor.PointerDown(new PixelPoint(40, 35), Tolerance);
        _editor.PointerMove(new PixelPoint(60, 45), Tolerance);
        Assert.Equal(box.Points, Document.Annotations.Single().Points);
        Assert.NotNull(_editor.PreviewPoints);
        _editor.PointerUp(new PixelPoint(60, 45), Tolerance);

        Assert.Equal(new PixelRect(40, 30, 40, 30), PolygonMath.BoundingBox(Document.Annotations.Single().Points));
        Assert.Null(_editor.PreviewPoints);
        Assert.Equal(new PixelRect(40, 30, 40, 30), PolygonMath.BoundingBox(_fixture.OpenImage(_service, 1).Annotations.Single().Points));
    }

    [Fact]
    public void Select_ClickWithoutMovingIsNotAnEdit()
    {
        DrawBox(20, 20, 60, 50);
        var before = _service.CanUndo(1);
        var edits = 0;
        _editor.Edited += (_, _) => edits++;

        Click(40, 35);

        Assert.Equal(0, edits);
        Assert.True(before);
        _service.Undo(Document);
        Assert.Empty(Document.Annotations);
    }

    [Fact]
    public void Select_DragCornerResizesTheBox()
    {
        DrawBox(20, 20, 60, 50);
        Click(40, 35);

        Drag(60, 50, 100, 80);

        Assert.Equal(new PixelRect(20, 20, 80, 60), PolygonMath.BoundingBox(Document.Annotations.Single().Points));
    }

    [Fact]
    public void Select_DoubleClickOnAPolygonEdgeAddsAVertex()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        Click(10, 10);
        Click(90, 10);
        Click(50, 80);
        _editor.Finish();
        _editor.SetTool(AnnotateTool.Select);

        Click(50, 10, count: 2);

        Assert.Equal(4, Document.Annotations.Single().Points.Count);
        Assert.Equal(new PixelPoint(50, 10), Document.Annotations.Single().Points[1]);
    }

    [Fact]
    public void Select_PolygonVertexDragThatWouldCrossItselfKeepsTheLastValidShape()
    {
        _editor.SetTool(AnnotateTool.Polygon);
        foreach (var (x, y) in new[] { (10.0, 10.0), (90.0, 10.0), (90.0, 60.0), (10.0, 60.0) })
            Click(x, y);
        _editor.Finish();
        _editor.SetTool(AnnotateTool.Select);
        Click(50, 30);

        // Vertex 1 to (90, 90): its edge to vertex 0 would cross the bottom edge. The drag passes
        // (90, 50) on the way, which is still fine, so that is where the vertex stays.
        Drag(90, 10, 90, 90);

        var points = Document.Annotations.Single().Points;
        Assert.Equal(new PixelPoint(90, 50), points[1]);
        Assert.False(PolygonMath.HasSelfIntersection(points, closed: true));
    }

    [Fact]
    public void Delete_RemovesTheSelectedShapeAndUndoBringsItBack()
    {
        DrawBox(20, 20, 60, 50);
        Click(40, 35);

        Assert.True(_editor.DeleteSelected());
        Assert.Empty(Document.Annotations);
        Assert.Null(_editor.SelectedId);

        Assert.True(_editor.Undo());
        Assert.Single(Document.Annotations);
    }

    [Fact]
    public void Delete_WithNothingSelectedDoesNothing()
    {
        Assert.False(_editor.DeleteSelected());
    }

    [Fact]
    public void Locked_ShapesCanBeSelectedButNotMovedDeletedOrReclassified()
    {
        DrawBox(20, 20, 60, 50);
        var box = Document.Annotations.Single();
        Click(40, 35);
        _editor.ToggleLocked(box.Id);

        Drag(40, 35, 70, 60);
        Assert.Equal(box.Points, Document.Annotations.Single().Points);
        Assert.False(_editor.DeleteSelected());
        Assert.False(_editor.ChangeSelectedClass(_otherClassId));
        Assert.Equal(box.Id, _editor.SelectedId);
    }

    [Fact]
    public void Hidden_ShapesCannotBePickedAndHideAllClearsTheSelection()
    {
        DrawBox(20, 20, 60, 50);
        var box = Document.Annotations.Single();
        _editor.ToggleHidden(box.Id);

        Click(40, 35);
        Assert.Null(_editor.SelectedId);

        _editor.ToggleHidden(box.Id);
        Click(40, 35);
        Assert.Equal(box.Id, _editor.SelectedId);

        _editor.ToggleShapesVisible();
        Assert.Null(_editor.SelectedId);
        Click(40, 35);
        Assert.Null(_editor.SelectedId);
    }

    [Fact]
    public void ChangeSelectedClass_IsOneUndoableEdit()
    {
        DrawBox(20, 20, 60, 50);
        Click(40, 35);

        Assert.True(_editor.ChangeSelectedClass(_otherClassId));
        Assert.Equal(_otherClassId, Document.Annotations.Single().ClassId);
        Assert.False(_editor.ChangeSelectedClass(_otherClassId));

        _editor.Undo();
        Assert.Equal(_classId, Document.Annotations.Single().ClassId);
    }

    [Fact]
    public void Undo_RedoWalkTheWholeDrawingSession()
    {
        DrawBox(10, 10, 50, 40);
        DrawBox(70, 10, 110, 40);
        Click(30, 25);
        _editor.DeleteSelected();

        _editor.Undo();
        _editor.Undo();
        Assert.Single(Document.Annotations);
        _editor.Redo();
        Assert.Equal(2, Document.Annotations.Count);
        Assert.False(_editor.Undo() && _editor.Undo() && _editor.Undo());
    }

    [Fact]
    public void Undo_ClearsASelectionThatNoLongerExists()
    {
        DrawBox(20, 20, 60, 50);
        Assert.NotNull(_editor.SelectedId);

        _editor.Undo();

        Assert.Null(_editor.SelectedId);
    }

    [Fact]
    public void CopyPaste_DuplicatesWithANewIdAndShiftsOnTheSameImage()
    {
        DrawBox(20, 20, 60, 50);
        var original = Document.Annotations.Single();
        Click(40, 35);

        Assert.True(_editor.Copy());
        Assert.True(_editor.Paste());
        Assert.True(_editor.Paste());

        Assert.Equal(3, Document.Annotations.Count);
        Assert.Equal(3, Document.Annotations.Select(annotation => annotation.Id).Distinct().Count());
        Assert.Equal(new PixelRect(32, 32, 40, 30), PolygonMath.BoundingBox(Document.Annotations[1].Points));
        Assert.Equal(new PixelRect(44, 44, 40, 30), PolygonMath.BoundingBox(Document.Annotations[2].Points));
        Assert.Equal(original.Points, Document.Annotations[0].Points);
        Assert.Equal(Document.Annotations[2].Id, _editor.SelectedId);
    }

    [Fact]
    public void CopyPaste_ToAnotherImageKeepsPositionAndScalesToItsSize()
    {
        DrawBox(20, 20, 60, 50);
        Click(40, 35);
        _editor.Copy();

        var second = _service.Open(_service.LoadImages().Single(image => image.Id == 2) with { Width = 400, Height = 200 });
        _editor.Open(second);
        Assert.True(_editor.Paste());

        Assert.Equal(new PixelRect(40, 40, 80, 60), PolygonMath.BoundingBox(second.Annotations.Single().Points));
        Assert.Equal(2, second.ImageId);
    }

    [Fact]
    public void Paste_WithAnEmptyClipboardDoesNothing()
    {
        Assert.False(_editor.Paste());
        Assert.False(_editor.Copy());
    }

    [Fact]
    public void CopyFrom_AnotherImageBringsLabelsAndTagsInOneEdit()
    {
        DrawBox(20, 20, 60, 50);
        _editor.ToggleTag(_otherClassId);
        var content = _service.LoadContent(1);

        _editor.Open(_fixture.OpenImage(_service, 2));
        Assert.True(_editor.CopyFrom(content, 200, 100));

        Assert.Single(Document.Annotations);
        Assert.Equal(new[] { _otherClassId }, Document.Tags);
        Assert.Equal(2, Document.Annotations[0].ImageId);
        Assert.Equal(ImageStatus.Labeled, Document.Status);

        _editor.Undo();
        Assert.Empty(Document.Annotations);
        Assert.Empty(Document.Tags);
    }

    [Fact]
    public void ToggleTag_AddsAndRemovesAnImageLevelLabel()
    {
        Assert.True(_editor.ToggleTag(_classId));
        Assert.Contains(_classId, Document.Tags);
        Assert.True(_editor.ToggleTag(_classId));
        Assert.Empty(Document.Tags);
    }

    [Fact]
    public void ToggleNull_RefusedWhileTheImageHasLabels()
    {
        Assert.True(_editor.ToggleNull());
        Assert.Equal(ImageStatus.Null, Document.Status);
        Assert.True(_editor.ToggleNull());
        DrawBox(20, 20, 60, 50);
        Assert.False(_editor.ToggleNull());
    }

    [Fact]
    public void FailedSave_ShowsAMessageAndKeepsTheDocument()
    {
        var classes = new ClassService(_store);
        _editor.ActiveClassId = 999;
        _editor.SetTool(AnnotateTool.Box);

        Drag(20, 20, 80, 60);

        Assert.Empty(Document.Annotations);
        Assert.StartsWith("Không lưu được nhãn", _editor.Message);
        Assert.Equal(2, classes.Load().Count);
    }

    [Fact]
    public void Open_ResetsSelectionHiddenAndLocked()
    {
        DrawBox(20, 20, 60, 50);
        _editor.ToggleLocked(Document.Annotations[0].Id);

        _editor.Open(_fixture.OpenImage(_service, 2));

        Assert.Null(_editor.SelectedId);
        Assert.Empty(_editor.Locked);
        Assert.True(_editor.ShapesVisible);
    }

    [Fact]
    public void Edited_FiresOncePerSavedEditNotPerPointerMove()
    {
        var edits = new List<AnnotationEdit>();
        _editor.Edited += (_, edit) => edits.Add(edit);
        _editor.SetTool(AnnotateTool.Box);

        Drag(20, 20, 80, 60);

        Assert.Single(edits);
        Assert.Equal(ImageStatus.Labeled, edits[0].StatusAfter);
    }
}
