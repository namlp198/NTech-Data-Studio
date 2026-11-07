using System.Globalization;
using Avalonia.Data.Converters;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.Views;

/// <summary>
/// Maps grades, states and log levels to theme resources by key, so colors stay in Themes/ and a
/// missing key shows up in a test instead of as a blank control.
/// </summary>
public static class Converters
{
    public static readonly IValueConverter GradeBrush = new ResourceConverter(value =>
        value is Grade grade ? "Brush.Grade." + grade : "Brush.Grade.Unknown");

    public static readonly IValueConverter GradeIcon = new ResourceConverter(value => value switch
    {
        Grade.Excellent or Grade.Good => "Icon.Grade.Check",
        Grade.Moderate or Grade.Weak => "Icon.Grade.Alert",
        Grade.Inadequate => "Icon.Grade.Block",
        _ => "Icon.Grade.Question"
    });

    public static readonly IValueConverter StateBrush = new ResourceConverter(value => (value as string) switch
    {
        RunStates.Completed => "Brush.Status.Success",
        RunStates.Failed => "Brush.Status.Error",
        RunStates.Interrupted => "Brush.Status.Warning",
        RunStates.Preparing or RunStates.Preflight or RunStates.Running or RunStates.Evaluating => "Brush.Status.Info",
        _ => "Brush.Status.Neutral"
    });

    public static readonly IValueConverter LogBrush = new ResourceConverter(value => value switch
    {
        LogLevel.Error => "Brush.Status.Error",
        LogLevel.Warning => "Brush.Status.Warning",
        _ => "Brush.Text.Secondary"
    });

    public static readonly IValueConverter ExportBrush = new ResourceConverter(value => (value as string) switch
    {
        "Đạt" => "Brush.Status.Success",
        "Không đạt" => "Brush.Status.Error",
        "Chưa kết luận" => "Brush.Status.Warning",
        "Đang export" => "Brush.Status.Info",
        _ => "Brush.Text.Muted"
    });

    /// <summary>A share in 0..1 to a width, for the class bars on Dataset.</summary>
    public static readonly IValueConverter ShareToWidth = new FuncValueConverter<double, double>(share =>
        Math.Max(2, share * (Resource("Layout.ClassBarWidth") as double? ?? 0)));

    public static object? Resource(string key) =>
        Avalonia.Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var value) ? value : null;

    private sealed class ResourceConverter(Func<object?, string> key) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Resource(key(value));

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
