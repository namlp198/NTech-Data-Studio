using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Desktop.Converters;

/// <summary>"#RRGGBB" to a frozen brush, for class swatches.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || ColorConverter.ConvertFromString(text) is not Color color)
            return Brushes.Transparent;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An image status to its dot brush from Themes/Tokens/Annotation.xaml.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is ImageStatus status ? $"Brush.ImageStatus.{status}" : "Brush.ImageStatus.New";
        return System.Windows.Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the value's name equals the parameter, for the dashed outline of Null images.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
