using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NTech.DataStudio.Domain.Annotations;

namespace NTech.DataStudio.Desktop.Controls;

/// <summary>A label ready to draw: output-picture pixels and its class colour.</summary>
public sealed record PreviewShape(ShapeKind Kind, IReadOnlyList<Point> Points, Color Color);

/// <summary>
/// A picture with its labels, fitted into whatever room it gets. Labels are in the
/// picture's own pixels and drawn in screen units, so strokes stay as thin in a small
/// cell as in a big one. Read-only: the editing canvas is <see cref="AnnotationCanvas"/>.
/// </summary>
public sealed class LabelPreview : FrameworkElement
{
    private const double PointRadius = 4;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(LabelPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShapesProperty = DependencyProperty.Register(
        nameof(Shapes), typeof(IReadOnlyList<PreviewShape>), typeof(LabelPreview),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Dictionary<Color, (Brush Fill, Pen Pen)> _looks = new();

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IReadOnlyList<PreviewShape>? Shapes
    {
        get => (IReadOnlyList<PreviewShape>?)GetValue(ShapesProperty);
        set => SetValue(ShapesProperty, value);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnRender(DrawingContext context)
    {
        if (Source is not { } source || source.PixelWidth == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        var scale = Math.Min(ActualWidth / source.PixelWidth, ActualHeight / source.PixelHeight);
        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;
        var left = (ActualWidth - width) / 2;
        var top = (ActualHeight - height) / 2;
        context.DrawImage(source, new Rect(left, top, width, height));

        if (Shapes is not { } shapes) return;

        var stroke = TryFindResource("Annotation.Stroke") is double thickness ? thickness : 1.5;
        var fillOpacity = TryFindResource("Annotation.FillOpacity") is double opacity ? opacity : 0.16;
        foreach (var shape in shapes)
        {
            var (fill, pen) = LookOf(shape.Color, stroke, fillOpacity);
            var points = shape.Points.Select(point => new Point(left + point.X * scale, top + point.Y * scale)).ToList();

            if (shape.Kind == ShapeKind.Point)
            {
                context.DrawEllipse(fill, pen, points[0], PointRadius, PointRadius);
                continue;
            }

            var geometry = new StreamGeometry();
            using (var open = geometry.Open())
            {
                open.BeginFigure(points[0], isFilled: true, isClosed: true);
                open.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
            }

            geometry.Freeze();
            context.DrawGeometry(fill, pen, geometry);
        }
    }

    private (Brush Fill, Pen Pen) LookOf(Color color, double stroke, double fillOpacity)
    {
        if (_looks.TryGetValue(color, out var look)) return look;

        var fill = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * fillOpacity), color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(color), stroke) { LineJoin = PenLineJoin.Round };
        fill.Freeze();
        pen.Freeze();
        return _looks[color] = (fill, pen);
    }
}
