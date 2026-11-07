using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Desktop.Controls;

public sealed record ClassStyle(Color Color, string Name);

/// <summary>
/// Shows one master image with its labels, and turns the mouse into image
/// pixels for the <see cref="AnnotationEditor"/>. The picture is drawn once in
/// image coordinates and only its transform changes while zooming, so panning
/// a 20 MP image stays cheap. Labels are redrawn in screen coordinates, which
/// keeps their strokes the same width at every zoom. All the editing logic
/// lives in the editor; this class only does mouse math and drawing.
/// </summary>
public sealed class AnnotationCanvas : FrameworkElement
{
    private const double MinScale = 0.02;
    private const double MaxScale = 64;
    private const double ZoomStep = 1.15;
    private const double FitMargin = 0.98;
    private const double PixelGridScale = 4;
    private const double ZoomToLabelPadding = 0.5;
    private const double PointRadius = 5;
    private const double VertexDotRadius = 3.5;
    private const double MinLabelWidth = 14;

    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(
        nameof(Editor), typeof(AnnotationEditor), typeof(AnnotationCanvas),
        new PropertyMetadata(null, (d, e) => ((AnnotationCanvas)d).OnEditorChanged((AnnotationEditor?)e.OldValue, (AnnotationEditor?)e.NewValue)));

    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image), typeof(BitmapSource), typeof(AnnotationCanvas),
        new PropertyMetadata(null, (d, _) => ((AnnotationCanvas)d).OnImageChanged()));

    public static readonly DependencyProperty ClassStylesProperty = DependencyProperty.Register(
        nameof(ClassStyles), typeof(IReadOnlyDictionary<long, ClassStyle>), typeof(AnnotationCanvas),
        new PropertyMetadata(null, (d, _) => ((AnnotationCanvas)d).OnStylesChanged()));

    public static readonly DependencyProperty LabelOpacityProperty = DependencyProperty.Register(
        nameof(LabelOpacity), typeof(double), typeof(AnnotationCanvas),
        new PropertyMetadata(1.0, (d, _) => ((AnnotationCanvas)d).OnStylesChanged()));

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _imageVisual = new();
    private readonly DrawingVisual _shapesVisual = new();
    private readonly DrawingVisual _overlayVisual = new();
    private readonly Dictionary<(Color, bool), (Brush Fill, Pen Pen)> _looks = new();
    private readonly Dictionary<long, FormattedText> _labels = new();

    private double _scale = 1;
    private double _tx;
    private double _ty;
    private bool _autoFit = true;
    private Point? _pointer;
    private Point _panStart;
    private double _panTx;
    private double _panTy;
    private bool _panning;

    // Design tokens, read once from Themes/Tokens/Annotation.xaml.
    private bool _resourcesRead;
    private double _stroke = 1.5;
    private double _strokeSelected = 2.5;
    private double _fillOpacity = 0.16;
    private double _fillOpacitySelected = 0.28;
    private double _handleSize = 8;
    private Brush _handleFill = Brushes.White;
    private Brush _handleBorder = Brushes.DodgerBlue;
    private Brush _crosshair = Brushes.Gray;
    private Brush _labelText = Brushes.Black;
    private FontFamily _font = new("Segoe UI");
    private double _fontSize = 12;

    public AnnotationCanvas()
    {
        _visuals = new VisualCollection(this) { _imageVisual, _shapesVisual, _overlayVisual };
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        // The canvas takes single-letter shortcuts. A Vietnamese IME (Telex) would turn "ee" into a Backspace
        // plus "ê" and swallow every other press, so it must not see keys while the canvas has the focus.
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    public AnnotationEditor? Editor
    {
        get => (AnnotationEditor?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public BitmapSource? Image
    {
        get => (BitmapSource?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public IReadOnlyDictionary<long, ClassStyle>? ClassStyles
    {
        get => (IReadOnlyDictionary<long, ClassStyle>?)GetValue(ClassStylesProperty);
        set => SetValue(ClassStylesProperty, value);
    }

    public double LabelOpacity
    {
        get => (double)GetValue(LabelOpacityProperty);
        set => SetValue(LabelOpacityProperty, value);
    }

    /// <summary>Screen pixels per image pixel.</summary>
    public double Scale => _scale;

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    // Without this the empty parts of the control would not receive the mouse.
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    // ---- view ----

    public void FitToWindow()
    {
        _autoFit = true;
        ApplyFit();
    }

    public void ZoomIn() => ZoomAt(Center(), ZoomStep);

    public void ZoomOut() => ZoomAt(Center(), 1 / ZoomStep);

    /// <summary>Brings a label into view with some room around it.</summary>
    public void ZoomToRect(PixelRect area)
    {
        if (Image is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        var width = Math.Max(area.Width, 1) * (1 + 2 * ZoomToLabelPadding);
        var height = Math.Max(area.Height, 1) * (1 + 2 * ZoomToLabelPadding);
        _scale = Math.Clamp(Math.Min(ActualWidth / width, ActualHeight / height), MinScale, MaxScale);
        _tx = ActualWidth / 2 - (area.X + area.Width / 2) * _scale;
        _ty = ActualHeight / 2 - (area.Y + area.Height / 2) * _scale;
        _autoFit = false;
        UpdateView();
    }

    private Point Center() => new(ActualWidth / 2, ActualHeight / 2);

    private Cursor IdleCursor() => Editor?.Tool switch
    {
        AnnotateTool.Pan => Cursors.Hand,
        { } when Editor.IsDrawingTool => Cursors.Cross,
        _ => Cursors.Arrow
    };


    /// <summary>
    /// Where a left press may drag the picture instead of the editor: with the Select tool when nothing
    /// is under the pointer, with the Tag tool (nothing to do on the canvas) and with the Pan tool. A drawing tool
    /// needs the drag for drawing; there Space or the middle button pans.
    /// </summary>
    private bool StartsOnBarePicture(Point screen)
    {
        if (Editor is null) return false;
        return Editor.Tool switch
        {
            AnnotateTool.Select => !Editor.IsOverLabel(ToImage(screen), Tolerance),
            AnnotateTool.Tag or AnnotateTool.Pan => true,
            _ => false
        };
    }

    private void ZoomAt(Point anchor, double factor)
    {
        if (Image is null) return;

        var next = Math.Clamp(_scale * factor, MinScale, MaxScale);
        factor = next / _scale;
        _tx = anchor.X - (anchor.X - _tx) * factor;
        _ty = anchor.Y - (anchor.Y - _ty) * factor;
        _scale = next;
        _autoFit = false;
        UpdateView();
    }

    private void ApplyFit()
    {
        if (Image is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        _scale = Math.Min(ActualWidth / Image.PixelWidth, ActualHeight / Image.PixelHeight) * FitMargin;
        _tx = (ActualWidth - Image.PixelWidth * _scale) / 2;
        _ty = (ActualHeight - Image.PixelHeight * _scale) / 2;
        UpdateView();
    }

    private PixelPoint ToImage(Point screen) => new((screen.X - _tx) / _scale, (screen.Y - _ty) / _scale);

    private Point ToScreen(PixelPoint point) => new(point.X * _scale + _tx, point.Y * _scale + _ty);

    private double Tolerance
    {
        get
        {
            ReadResources();
            return _handleSize / Math.Max(_scale, MinScale);
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_autoFit) ApplyFit(); else UpdateView();
    }

    // ---- property changes ----

    private void OnEditorChanged(AnnotationEditor? oldEditor, AnnotationEditor? newEditor)
    {
        if (oldEditor is not null) oldEditor.Changed -= OnEditorStateChanged;
        if (newEditor is not null) newEditor.Changed += OnEditorStateChanged;
        RenderLayers();
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        Cursor = IdleCursor();
        RenderLayers();
    }

    private void OnImageChanged()
    {
        DrawImage();
        FitToWindow();
    }

    private void OnStylesChanged()
    {
        _looks.Clear();
        _labels.Clear();
        RenderLayers();
    }

    // ---- drawing ----

    private void UpdateView()
    {
        _imageVisual.Transform = new MatrixTransform(_scale, 0, 0, _scale, _tx, _ty);
        // Show real pixels when zoomed far in, and a smooth picture when zoomed out.
        RenderOptions.SetBitmapScalingMode(_imageVisual, _scale >= PixelGridScale ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
        RenderLayers();
    }

    private void DrawImage()
    {
        using var context = _imageVisual.RenderOpen();
        if (Image is { } image)
            context.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
    }

    private void RenderLayers()
    {
        RenderShapes();
        RenderOverlay();
    }

    private void ReadResources()
    {
        if (_resourcesRead) return;
        _resourcesRead = true;

        _stroke = Resource("Annotation.Stroke", _stroke);
        _strokeSelected = Resource("Annotation.StrokeSelected", _strokeSelected);
        _fillOpacity = Resource("Annotation.FillOpacity", _fillOpacity);
        _fillOpacitySelected = Resource("Annotation.FillOpacitySelected", _fillOpacitySelected);
        _handleSize = Resource("Annotation.HandleSize", _handleSize);
        _handleFill = Resource<Brush>("Brush.Annotation.HandleFill", _handleFill);
        _handleBorder = Resource<Brush>("Brush.Annotation.HandleBorder", _handleBorder);
        _crosshair = Resource<Brush>("Brush.Annotation.Crosshair", _crosshair);
        _labelText = Resource<Brush>("Brush.Background.App", _labelText);
        _font = Resource("Font.Primary", _font);
        _fontSize = Resource("FontSize.Caption", _fontSize);
    }

    private T Resource<T>(string key, T fallback) => TryFindResource(key) is T value ? value : fallback;

    private (Brush Fill, Pen Pen) LookOf(Color color, bool selected)
    {
        if (_looks.TryGetValue((color, selected), out var look)) return look;

        var opacity = Math.Clamp(LabelOpacity, 0, 1);
        var fill = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(255 * (selected ? _fillOpacitySelected : _fillOpacity) * opacity), color.R, color.G, color.B));
        var stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B));
        var pen = new Pen(stroke, selected ? _strokeSelected : _stroke) { LineJoin = PenLineJoin.Round };
        fill.Freeze();
        pen.Freeze();
        return _looks[(color, selected)] = (fill, pen);
    }

    private Color ColorOf(long classId) =>
        ClassStyles is { } styles && styles.TryGetValue(classId, out var style) ? style.Color : Colors.Gray;

    private FormattedText LabelTextOf(long classId)
    {
        if (_labels.TryGetValue(classId, out var cached)) return cached;

        var name = ClassStyles is { } styles && styles.TryGetValue(classId, out var style) ? style.Name : "?";
        return _labels[classId] = new FormattedText(
            name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            _fontSize, _labelText, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private void RenderShapes()
    {
        ReadResources();
        using var context = _shapesVisual.RenderOpen();
        if (Editor is not { Document: { } document } editor || Image is null || !editor.ShapesVisible) return;

        foreach (var annotation in document.Annotations)
        {
            if (editor.Hidden.Contains(annotation.Id)) continue;

            var selected = annotation.Id == editor.SelectedId;
            var points = annotation.Id == editor.PreviewId && editor.PreviewPoints is { } preview ? preview : annotation.Points;
            var (fill, pen) = LookOf(ColorOf(annotation.ClassId), selected);

            DrawShape(context, annotation.Kind, points, fill, pen);
            DrawName(context, annotation, points);
        }

        if (editor.Selected is { } current && !editor.Hidden.Contains(current.Id) && current.Kind != ShapeKind.Point)
        {
            var points = current.Id == editor.PreviewId && editor.PreviewPoints is { } preview ? preview : current.Points;
            var handle = new Pen(_handleBorder, 1.5);
            foreach (var point in points)
            {
                var screen = ToScreen(point);
                context.DrawRectangle(
                    _handleFill, handle,
                    new Rect(screen.X - _handleSize / 2, screen.Y - _handleSize / 2, _handleSize, _handleSize));
            }
        }
    }

    private void DrawShape(DrawingContext context, ShapeKind kind, IReadOnlyList<PixelPoint> points, Brush fill, Pen pen)
    {
        if (kind == ShapeKind.Point)
        {
            context.DrawEllipse(fill, pen, ToScreen(points[0]), PointRadius, PointRadius);
            return;
        }

        context.DrawGeometry(fill, pen, Outline(points, closed: true));
    }

    private StreamGeometry Outline(IReadOnlyList<PixelPoint> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToScreen(points[0]), isFilled: closed, isClosed: closed);
            for (var index = 1; index < points.Count; index++)
                context.LineTo(ToScreen(points[index]), isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private void DrawName(DrawingContext context, Annotation annotation, IReadOnlyList<PixelPoint> points)
    {
        var box = PolygonMath.BoundingBox(points);
        if (box.Width * _scale < MinLabelWidth) return;

        var text = LabelTextOf(annotation.ClassId);
        var corner = ToScreen(new PixelPoint(box.X, box.Y));
        var top = corner.Y - text.Height - 2;
        if (top < 0) top = corner.Y + 2;

        var (_, pen) = LookOf(ColorOf(annotation.ClassId), false);
        context.DrawRectangle(pen.Brush, null, new Rect(corner.X, top, text.Width + 6, text.Height + 2));
        context.DrawText(text, new Point(corner.X + 3, top + 1));
    }

    private void RenderOverlay()
    {
        ReadResources();
        using var context = _overlayVisual.RenderOpen();
        if (Editor is not { Document: not null } editor || Image is null) return;

        if (editor.IsDrawingTool && _pointer is { } pointer && !_panning)
            DrawCrosshair(context, pointer);

        if (editor.Drawing is { } session)
            DrawSession(context, editor, session);
    }

    private void DrawCrosshair(DrawingContext context, Point pointer)
    {
        if (Image is null) return;

        var left = ToScreen(new PixelPoint(0, 0));
        var right = ToScreen(new PixelPoint(Image.PixelWidth, Image.PixelHeight));
        var pen = new Pen(_crosshair, 1);
        if (pointer.Y >= left.Y && pointer.Y <= right.Y)
            context.DrawLine(pen, new Point(left.X, pointer.Y), new Point(right.X, pointer.Y));
        if (pointer.X >= left.X && pointer.X <= right.X)
            context.DrawLine(pen, new Point(pointer.X, left.Y), new Point(pointer.X, right.Y));
    }

    private void DrawSession(DrawingContext context, AnnotationEditor editor, AnnotationDrawingSession session)
    {
        var color = editor.ActiveClassId is { } classId ? ColorOf(classId) : Colors.Gray;
        var (fill, pen) = LookOf(color, true);
        var preview = session.PreviewPoints;

        if (preview.Count >= 3)
            context.DrawGeometry(fill, pen, Outline(preview, closed: true));
        else if (preview.Count == 2)
            context.DrawLine(pen, ToScreen(preview[0]), ToScreen(preview[1]));

        foreach (var vertex in session.Vertices)
            context.DrawEllipse(_handleFill, new Pen(pen.Brush, 1.5), ToScreen(vertex), VertexDotRadius, VertexDotRadius);
    }

    // ---- mouse ----

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Image is null) return;

        var position = e.GetPosition(this);
        var wantsPan = e.ChangedButton == MouseButton.Middle
                       || (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space));
        // At any zoom, a drag that starts on bare picture moves the picture (and with the Pan tool, anywhere).
        // The press still reaches the editor, so a plain click on bare picture deselects as before.
        var dragsPicture = !wantsPan && e.ChangedButton == MouseButton.Left && StartsOnBarePicture(position);
        if (dragsPicture && Editor is { Tool: not AnnotateTool.Pan })
            Editor.PointerDown(ToImage(position), Tolerance, e.ClickCount);
        if (wantsPan || dragsPicture)
        {
            _panning = true;
            _panStart = position;
            _panTx = _tx;
            _panTy = _ty;
            Cursor = Cursors.Hand;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && Editor is not null)
        {
            CaptureMouse();
            Editor.PointerDown(ToImage(position), Tolerance, e.ClickCount);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        _pointer = position;

        if (_panning)
        {
            _tx = _panTx + position.X - _panStart.X;
            _ty = _panTy + position.Y - _panStart.Y;
            _autoFit = false;
            UpdateView();
            return;
        }

        if (Image is null) return;

        Editor?.PointerMove(ToImage(position), Tolerance);
        // The crosshair follows the pointer even when the editor has nothing to redraw.
        RenderOverlay();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
        {
            _panning = false;
            Cursor = IdleCursor();
            ReleaseMouseCapture();
            RenderOverlay();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (Image is not null && Editor is not null)
                Editor.PointerUp(ToImage(e.GetPosition(this)), Tolerance);
            ReleaseMouseCapture();
        }
    }

    /// <summary>Raised when the right button is released over a label, which is selected first.</summary>
    public event Action<Annotation>? LabelMenuRequested;

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (Image is null || Editor is not { Drawing: null } editor || _panning) return;

        if (editor.LabelAt(ToImage(e.GetPosition(this)), Tolerance) is { } hit)
        {
            editor.Select(hit.Id);
            LabelMenuRequested?.Invoke(hit);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _pointer = null;
        RenderOverlay();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.GetPosition(this), Math.Pow(ZoomStep, e.Delta / 120.0));
        e.Handled = true;
    }
}
