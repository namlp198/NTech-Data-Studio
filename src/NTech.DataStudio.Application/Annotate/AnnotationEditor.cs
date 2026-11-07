using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>
/// The pointer and keyboard logic of the annotation canvas, in image pixels, so
/// it can be tested without a window. The canvas only converts mouse positions
/// and draws what this exposes. Tolerances are in image pixels too: the canvas
/// divides its hit radius in screen pixels by the zoom.
/// </summary>
public sealed class AnnotationEditor
{
    private const double PasteOffsetPixels = 12;

    private sealed record Drag(Annotation Original, int VertexIndex, PixelPoint Start);

    private readonly AnnotateService _service;
    private readonly AnnotationClipboard _clipboard;
    private readonly HashSet<Guid> _hidden = new();
    private readonly HashSet<Guid> _locked = new();
    private Drag? _drag;

    public AnnotationEditor(AnnotateService service, AnnotationClipboard clipboard)
    {
        _service = service;
        _clipboard = clipboard;
    }

    /// <summary>Something to draw changed: the shapes, the selection, the preview.</summary>
    public event EventHandler? Changed;

    /// <summary>An edit reached the database.</summary>
    public event EventHandler<AnnotationEdit>? Edited;

    public ImageDocument? Document { get; private set; }
    public AnnotateTool Tool { get; private set; } = AnnotateTool.Select;
    public long? ActiveClassId { get; set; }
    public Guid? SelectedId { get; private set; }
    public AnnotationDrawingSession? Drawing { get; private set; }

    /// <summary>The shape being dragged, before the drag is saved.</summary>
    public Guid? PreviewId { get; private set; }
    public IReadOnlyList<PixelPoint>? PreviewPoints { get; private set; }

    /// <summary>A short hint or error for the user; cleared by the next action.</summary>
    public string? Message { get; private set; }
    public bool ShapesVisible { get; private set; } = true;
    public IReadOnlySet<Guid> Hidden => _hidden;
    public IReadOnlySet<Guid> Locked => _locked;

    public Annotation? Selected => SelectedId is { } id ? Document?.Find(id) : null;
    public bool IsDrawingTool => Tool is AnnotateTool.Box or AnnotateTool.Polygon or AnnotateTool.RotatedBox;

    public void Open(ImageDocument? document)
    {
        Document = document;
        _drag = null;
        Drawing = null;
        PreviewId = null;
        PreviewPoints = null;
        SelectedId = null;
        Message = null;
        _hidden.Clear();
        _locked.Clear();
        RaiseChanged();
    }

    public void SetTool(AnnotateTool tool)
    {
        Cancel();
        Tool = tool;
        RaiseChanged();
    }

    /// <summary>
    /// Esc: leaves whatever is going on and goes back to the Select tool, dropping a half-drawn shape.
    /// Already in Select with nothing being drawn, it clears the selection instead.
    /// </summary>
    public void Escape()
    {
        if (Drawing is not null || Tool != AnnotateTool.Select)
            SetTool(AnnotateTool.Select);
        else
            Select(null);
    }

    /// <summary>The next tool (+1) or the previous one (-1) in rail order, wrapping around.</summary>
    public void CycleTool(int step)
    {
        var tools = Enum.GetValues<AnnotateTool>();
        var index = Array.IndexOf(tools, Tool);
        SetTool(tools[((index + step) % tools.Length + tools.Length) % tools.Length]);
    }

    public void Select(Guid? id)
    {
        SelectedId = id is { } value && Document?.Find(value) is not null ? value : null;
        RaiseChanged();
    }

    public void ToggleHidden(Guid id)
    {
        if (!_hidden.Remove(id)) _hidden.Add(id);
        if (SelectedId == id && _hidden.Contains(id)) SelectedId = null;
        RaiseChanged();
    }

    public void ToggleLocked(Guid id)
    {
        if (!_locked.Remove(id)) _locked.Add(id);
        RaiseChanged();
    }

    public void ToggleShapesVisible()
    {
        ShapesVisible = !ShapesVisible;
        if (!ShapesVisible) SelectedId = null;
        RaiseChanged();
    }

    // ---- pointer ----

    public void PointerDown(PixelPoint point, double tolerance, int clickCount = 1)
    {
        if (Document is null) return;
        Message = null;

        if (Tool == AnnotateTool.Select)
            SelectDown(point, tolerance, clickCount);
        else if (IsDrawingTool)
            DrawDown(point, tolerance);

        RaiseChanged();
    }

    public void PointerMove(PixelPoint point, double tolerance)
    {
        if (Document is null) return;

        if (Drawing is not null)
        {
            Drawing.UpdateCandidate(point);
            RaiseChanged();
        }
        else if (_drag is { } drag)
        {
            PreviewPoints = Dragged(drag, point) ?? PreviewPoints;
            RaiseChanged();
        }
    }

    public void PointerUp(PixelPoint point, double tolerance)
    {
        if (Document is null) return;

        if (Drawing is { } session)
        {
            session.UpdateCandidate(point);
            switch (session.Kind)
            {
                case ShapeKind.Box:
                    if (!Finish()) Cancel();
                    break;
                case ShapeKind.RotatedBox when session.Vertices.Count == 1
                                               && session.Vertices[0].DistanceTo(session.Candidate) > tolerance:
                    // Dragged the main axis out in one go; the next click sets the width.
                    session.AddCandidateAsVertex();
                    break;
            }
        }
        else if (_drag is { } drag)
        {
            var moved = Dragged(drag, point) ?? PreviewPoints;
            EndDrag();
            if (moved is not null && !SamePoints(moved, drag.Original.Points))
                Run(EditRequest.Replace(drag.Original, drag.Original.WithPoints(moved, _service.Now)));
        }

        RaiseChanged();
    }

    /// <summary>Adds the shape being drawn. False, with a message, when it is not valid yet.</summary>
    public bool Finish()
    {
        if (Document is null || Drawing is not { } session) return false;

        if (!session.CanFinish)
        {
            Message = session.IsSelfIntersecting
                ? "Đa giác đang tự cắt, hãy bỏ bớt đỉnh hoặc đổi đỉnh cuối."
                : "Chưa đủ điểm để kết thúc hình này.";
            RaiseChanged();
            return false;
        }

        if (ActiveClassId is not { } classId)
        {
            Message = "Hãy chọn class trước khi vẽ.";
            RaiseChanged();
            return false;
        }

        var now = _service.Now;
        var created = new Annotation(
            Guid.NewGuid(), Document.ImageId, classId, session.Kind, session.FinalPoints, AnnotationSource.Manual, now, now);
        Drawing = null;

        if (Run(EditRequest.Add(new[] { created })) is not null)
            SelectedId = created.Id;

        RaiseChanged();
        return true;
    }

    /// <summary>Backspace while drawing: drops the last vertex, or the whole shape when only one is left.</summary>
    public void UndoPoint()
    {
        if (Drawing is null) return;

        if (!Drawing.UndoLastVertex())
            Drawing = null;
        RaiseChanged();
    }

    public void Cancel()
    {
        var changed = Drawing is not null || _drag is not null;
        Drawing = null;
        EndDrag();
        if (changed) RaiseChanged();
    }

    // ---- actions on the selection and the image ----

    public bool DeleteSelected()
    {
        if (Selected is not { } annotation) return false;
        if (_locked.Contains(annotation.Id))
        {
            Message = "Nhãn đang bị khóa.";
            RaiseChanged();
            return false;
        }

        SelectedId = null;
        return Run(EditRequest.Remove(new[] { annotation })) is not null;
    }

    public bool ChangeSelectedClass(long classId)
    {
        if (Selected is not { } annotation || annotation.ClassId == classId || _locked.Contains(annotation.Id))
            return false;

        return Run(EditRequest.Replace(annotation, annotation.WithClass(classId, _service.Now))) is not null;
    }

    /// <summary>The tag tool: a class is on the image or not.</summary>
    public bool ToggleTag(long classId)
    {
        if (Document is null) return false;

        var request = Document.Tags.Contains(classId)
            ? EditRequest.Tags(Array.Empty<long>(), new[] { classId })
            : EditRequest.Tags(new[] { classId }, Array.Empty<long>());
        return Run(request) is not null;
    }

    public bool Undo() => Move(_service.Undo);

    public bool Redo() => Move(_service.Redo);

    public bool ToggleNull() => Run(() => _service.ToggleNull(Document!)) is not null;

    public bool ToggleReviewed() => Run(() => _service.ToggleReviewed(Document!)) is not null;

    public bool Copy()
    {
        if (Document is null || Selected is not { } annotation) return false;

        _clipboard.Set(new[] { annotation }, Document.ImageId, Document.Width, Document.Height);
        return true;
    }

    /// <summary>Pastes the copied labels, scaled when this image has another size. On the image they came from, shifted a little.</summary>
    public bool Paste()
    {
        if (Document is null || !_clipboard.HasItems) return false;

        var offset = _clipboard.SourceImageId == Document.ImageId ? PasteOffsetPixels * ++_clipboard.PasteCount : 0;
        var added = Fit(_clipboard.Items, _clipboard.SourceWidth, _clipboard.SourceHeight, offset);
        if (added.Count == 0) return false;

        var result = Run(EditRequest.Add(added));
        if (result is not null) SelectedId = added[^1].Id;
        return result is not null;
    }

    /// <summary>Copies the labels and tags of another image, for a camera that did not move.</summary>
    public bool CopyFrom(ImageContent source, int sourceWidth, int sourceHeight)
    {
        if (Document is null) return false;

        var added = Fit(source.Annotations, sourceWidth, sourceHeight, 0);
        var request = new EditRequest(
            Array.Empty<Annotation>(), added, source.Tags.ToList(), Array.Empty<long>());
        return Run(request) is not null;
    }

    // ---- internals ----

    private void SelectDown(PixelPoint point, double tolerance, int clickCount)
    {
        if (Selected is { } selected && !_locked.Contains(selected.Id))
        {
            if (clickCount >= 2 && selected.Kind == ShapeKind.Polygon
                && ShapeEditing.HitEdge(selected.Points, point, tolerance) is { } edge)
            {
                var points = ShapeEditing.InsertVertex(selected.Points, edge.Edge, edge.Point);
                Run(EditRequest.Replace(selected, selected.WithPoints(points, _service.Now)));
                return;
            }

            var vertex = selected.Kind == ShapeKind.Point ? -1 : ShapeEditing.HitVertex(selected.Points, point, tolerance);
            if (vertex >= 0)
            {
                BeginDrag(new Drag(selected, vertex, point));
                return;
            }
        }

        var hit = HitTest(point, tolerance);
        SelectedId = hit?.Id;
        if (hit is not null && !_locked.Contains(hit.Id))
            BeginDrag(new Drag(hit, -1, point));
    }

    /// <summary>The label a press at this point would hit: the topmost visible one, or null.</summary>
    public Annotation? LabelAt(PixelPoint point, double tolerance) => Document is null ? null : HitTest(point, tolerance);

    /// <summary>
    /// Whether a press here would grab something with the Select tool: a label, or a vertex of the
    /// selected one. The canvas pans instead when it would not.
    /// </summary>
    public bool IsOverLabel(PixelPoint point, double tolerance)
    {
        if (Document is null) return false;

        if (Selected is { } selected && selected.Kind != ShapeKind.Point
            && ShapeEditing.HitVertex(selected.Points, point, tolerance) >= 0)
            return true;

        return HitTest(point, tolerance) is not null;
    }

    private Annotation? HitTest(PixelPoint point, double tolerance)
    {
        if (!ShapesVisible || Document is null) return null;

        // The last one is drawn on top, so it is picked first.
        for (var index = Document.Annotations.Count - 1; index >= 0; index--)
        {
            var annotation = Document.Annotations[index];
            if (!_hidden.Contains(annotation.Id) && ShapeEditing.HitShape(annotation.Kind, annotation.Points, point, tolerance))
                return annotation;
        }

        return null;
    }

    private void DrawDown(PixelPoint point, double tolerance)
    {
        if (ActiveClassId is null)
        {
            Message = "Hãy chọn class trước khi vẽ.";
            return;
        }

        var document = Document!;
        if (Drawing is null)
        {
            Drawing = new AnnotationDrawingSession(KindOf(Tool), point, document.Width, document.Height);
            SelectedId = null;
            return;
        }

        Drawing.UpdateCandidate(point);
        switch (Drawing.Kind)
        {
            case ShapeKind.Polygon:
                // Clicking the first vertex again closes the polygon.
                if (Drawing.Vertices.Count >= 3 && Drawing.Vertices[0].DistanceTo(Drawing.Candidate) <= tolerance)
                    Finish();
                else
                    Drawing.AddCandidateAsVertex();
                break;

            case ShapeKind.RotatedBox:
                if (Drawing.Vertices.Count == 1)
                    Drawing.AddCandidateAsVertex();
                else
                    Finish();
                break;

            default:
                if (!Finish()) Drawing = null;
                break;
        }
    }

    private void BeginDrag(Drag drag)
    {
        _drag = drag;
        PreviewId = drag.Original.Id;
        PreviewPoints = drag.Original.Points;
    }

    private void EndDrag()
    {
        _drag = null;
        PreviewId = null;
        PreviewPoints = null;
    }

    private IReadOnlyList<PixelPoint>? Dragged(Drag drag, PixelPoint point)
    {
        var document = Document!;
        return drag.VertexIndex < 0
            ? ShapeEditing.Translate(drag.Original.Points, point.X - drag.Start.X, point.Y - drag.Start.Y, document.Width, document.Height)
            : ShapeEditing.MoveVertex(drag.Original.Kind, drag.Original.Points, drag.VertexIndex, point, document.Width, document.Height);
    }

    private IReadOnlyList<Annotation> Fit(IReadOnlyList<Annotation> source, int sourceWidth, int sourceHeight, double offset)
    {
        var document = Document!;
        var scaleX = document.Width / (double)Math.Max(1, sourceWidth);
        var scaleY = document.Height / (double)Math.Max(1, sourceHeight);
        var now = _service.Now;

        var result = new List<Annotation>();
        foreach (var annotation in source)
        {
            var scaled = annotation.Points
                .Select(point => new PixelPoint(point.X * scaleX, point.Y * scaleY).ClampTo(document.Width, document.Height))
                .ToList();
            var moved = offset == 0 ? scaled : ShapeEditing.Translate(scaled, offset, offset, document.Width, document.Height);
            if (annotation.Kind != ShapeKind.Point && PolygonMath.Area(moved) < 1) continue;

            result.Add(annotation.CopyTo(document.ImageId, moved, now));
        }

        return result;
    }

    private bool Move(Func<ImageDocument, AnnotationEdit?> step)
    {
        if (Document is null) return false;

        Message = null;
        AnnotationEdit? edit;
        try
        {
            edit = step(Document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Message = $"Không lưu được nhãn: {exception.Message}";
            RaiseChanged();
            return false;
        }

        if (edit is null) return false;
        if (SelectedId is { } id && Document.Find(id) is null) SelectedId = null;
        Edited?.Invoke(this, edit);
        RaiseChanged();
        return true;
    }

    private AnnotationEdit? Run(EditRequest request) => Run(() => _service.Execute(Document!, request));

    /// <summary>A failed save shows a message and leaves the document as it was.</summary>
    private AnnotationEdit? Run(Func<AnnotationEdit?> action)
    {
        if (Document is null) return null;

        try
        {
            var edit = action();
            if (edit is not null)
            {
                Edited?.Invoke(this, edit);
                RaiseChanged();
            }

            return edit;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Message = $"Không lưu được nhãn: {exception.Message}";
            RaiseChanged();
            return null;
        }
    }

    private static ShapeKind KindOf(AnnotateTool tool) => tool switch
    {
        AnnotateTool.Box => ShapeKind.Box,
        AnnotateTool.Polygon => ShapeKind.Polygon,
        AnnotateTool.RotatedBox => ShapeKind.RotatedBox,
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    private static bool SamePoints(IReadOnlyList<PixelPoint> first, IReadOnlyList<PixelPoint> second)
    {
        if (first.Count != second.Count) return false;
        for (var index = 0; index < first.Count; index++)
        {
            if (first[index].DistanceTo(second[index]) > 1e-6) return false;
        }

        return true;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
