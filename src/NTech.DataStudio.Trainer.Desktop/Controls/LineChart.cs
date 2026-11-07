using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NTech.DataStudio.Trainer.Desktop.ViewModels;
using NTech.DataStudio.Trainer.Desktop.Views;

namespace NTech.DataStudio.Trainer.Desktop.Controls;

/// <summary>
/// A minimal line chart per epoch: one line per series, the scale and three grid lines. Drawing
/// only; the values come from the ViewModel and the colors from the theme.
/// </summary>
public sealed class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<LineChart, IReadOnlyList<ChartSeries>?>(nameof(Series));

    private static readonly string[] SeriesBrushes = ["Brush.Chart.Primary", "Brush.Chart.Secondary"];

    static LineChart() => AffectsRender<LineChart>(SeriesProperty);

    public IReadOnlyList<ChartSeries>? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var series = Series ?? [];
        var values = series.SelectMany(item => item.Values).OfType<double>().Where(double.IsFinite).ToList();
        var bounds = Bounds;
        var gridBrush = Converters.Resource("Brush.Chart.Grid") as IBrush ?? Brushes.Gray;
        var textBrush = Converters.Resource("Brush.Text.Muted") as IBrush ?? Brushes.Gray;
        var fontSize = Converters.Resource("FontSize.Micro") as double? ?? 11;
        var font = Converters.Resource("Font.Monospace") as FontFamily ?? FontFamily.Default;
        var stroke = Converters.Resource("Layout.ChartStroke") as double? ?? 2;
        var gridPen = new Pen(gridBrush, 1);

        const double left = 48, top = 8, right = 8, bottom = 20;
        var plot = new Rect(left, top, Math.Max(1, bounds.Width - left - right), Math.Max(1, bounds.Height - top - bottom));
        var count = series.Count == 0 ? 0 : series.Max(item => item.Values.Count);

        var min = values.Count > 0 ? values.Min() : 0;
        var max = values.Count > 0 ? values.Max() : 1;
        if (max - min < 1e-9)
        {
            max += 0.5;
            min -= 0.5;
        }

        for (var line = 0; line <= 2; line++)
        {
            var y = plot.Top + plot.Height * line / 2;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = max - (max - min) * line / 2;
            Text(context, value.ToString("0.###", CultureInfo.InvariantCulture), new Point(0, y - fontSize * 0.7), font, fontSize, textBrush);
        }

        if (count > 0)
        {
            Text(context, "1", new Point(plot.Left, plot.Bottom + 2), font, fontSize, textBrush);
            Text(context, $"epoch {count}", new Point(Math.Max(plot.Left, plot.Right - 60), plot.Bottom + 2), font, fontSize, textBrush);
        }

        for (var index = 0; index < series.Count; index++)
        {
            var brush = Converters.Resource(SeriesBrushes[index % SeriesBrushes.Length]) as IBrush ?? Brushes.White;
            var pen = new Pen(brush, stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var points = new List<Point>();
            var list = series[index].Values;
            for (var at = 0; at < list.Count; at++)
            {
                if (list[at] is not { } value || !double.IsFinite(value)) continue;
                var x = count <= 1 ? plot.Left + plot.Width / 2 : plot.Left + plot.Width * at / (count - 1);
                var y = plot.Bottom - (value - min) / (max - min) * plot.Height;
                points.Add(new Point(x, y));
            }

            if (points.Count == 1) context.DrawEllipse(brush, null, points[0], stroke * 1.5, stroke * 1.5);
            for (var at = 1; at < points.Count; at++) context.DrawLine(pen, points[at - 1], points[at]);
        }
    }

    private static void Text(DrawingContext context, string text, Point at, FontFamily font, double size, IBrush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font), size, brush);
        context.DrawText(formatted, at);
    }
}
