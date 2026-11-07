using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NTech.DataStudio.Desktop.Controls;

/// <summary>
/// Builds context menus in code, taken from RoboStation's helper of the same name: the theme
/// (<c>Themes/Components/Menus.xaml</c>) gives every <see cref="ContextMenu"/> its look; this adds the
/// icon, the danger style and sub-menus so menus built here read the same as menus in XAML.
/// </summary>
public static class AppContextMenu
{
    /// <summary>A menu that opens at the mouse over <paramref name="placementTarget"/>.</summary>
    public static ContextMenu Create(UIElement placementTarget, params Control[] items)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.MousePoint
        };
        foreach (var item in items) menu.Items.Add(item);
        return menu;
    }

    /// <summary>
    /// One item with an icon from <c>Themes/Tokens/Icons.xaml</c>. A null <paramref name="header"/> makes an
    /// icon-only item; give it a <paramref name="toolTip"/> and a name for screen readers.
    /// </summary>
    public static MenuItem Item(string? header, string? iconKey, Action onClick, bool isEnabled = true, bool danger = false, string? toolTip = null)
    {
        var item = NewItem(header, isEnabled, danger, toolTip);
        if (iconKey is not null) item.Icon = Icon(iconKey, item);
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>An item with a custom icon element (a colour dot, say) and an optional emphasis.</summary>
    public static MenuItem ItemWithIcon(string header, UIElement icon, Action onClick, bool isEnabled = true, bool emphasized = false)
    {
        var item = NewItem(header, isEnabled, danger: false, toolTip: null);
        item.Icon = icon;
        if (emphasized) item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>An item that opens a sub-menu to its right.</summary>
    public static MenuItem SubMenu(string header, string? iconKey, IEnumerable<Control> children, bool isEnabled = true)
    {
        var item = NewItem(header, isEnabled, danger: false, toolTip: null);
        if (iconKey is not null) item.Icon = Icon(iconKey, item);
        foreach (var child in children) item.Items.Add(child);
        return item;
    }

    public static Separator Separator() => new();

    private static MenuItem NewItem(string? header, bool isEnabled, bool danger, string? toolTip)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled, ToolTip = toolTip };
        if (header is null && toolTip is not null)
            System.Windows.Automation.AutomationProperties.SetName(item, toolTip);
        if (danger && System.Windows.Application.Current?.TryFindResource("MenuItem.Danger") is Style style)
            item.Style = style;
        return item;
    }

    /// <summary>The icon drawn in the item's own colour, so a danger item's icon is red too.</summary>
    public static System.Windows.Shapes.Path? Icon(string iconKey, MenuItem item)
    {
        if (System.Windows.Application.Current?.TryFindResource(iconKey) is not Geometry geometry) return null;
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(MenuItem.Foreground)) { Source = item });
        return path;
    }
}
