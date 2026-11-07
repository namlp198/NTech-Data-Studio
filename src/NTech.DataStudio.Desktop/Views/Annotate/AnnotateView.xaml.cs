using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NTech.DataStudio.Desktop.Controls;
using NTech.DataStudio.Desktop.ViewModels.Annotate;

namespace NTech.DataStudio.Desktop.Views.Annotate;

/// <summary>Keyboard routing and the wiring between the view model's view commands and the canvas.</summary>
public partial class AnnotateView : UserControl
{
    private AnnotateViewModel? _subscribed;

    public AnnotateView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AnnotateViewModel model)
        {
            _subscribed = model;
            model.FitRequested += Canvas.FitToWindow;
            model.ZoomInRequested += Canvas.ZoomIn;
            model.ZoomOutRequested += Canvas.ZoomOut;
            model.ZoomToRequested += Canvas.ZoomToRect;
            Canvas.LabelMenuRequested += OnLabelMenuRequested;
        }

        // The shortcuts only reach this view while something inside it has the focus.
        Canvas.Focus();
    }

    // The view model outlives this view, so its events must not keep a closed view alive.
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed is not { } model) return;

        model.FitRequested -= Canvas.FitToWindow;
        model.ZoomInRequested -= Canvas.ZoomIn;
        model.ZoomOutRequested -= Canvas.ZoomOut;
        model.ZoomToRequested -= Canvas.ZoomToRect;
        Canvas.LabelMenuRequested -= OnLabelMenuRequested;
        _subscribed = null;
    }

    // ---- right-click menu on a label (the RoboStation menu look) ----

    private void OnLabelMenuRequested(Domain.Annotations.Annotation label)
    {
        if (DataContext is not AnnotateViewModel { Editor: { } editor } model) return;

        var locked = editor.Locked.Contains(label.Id);
        var classes = model.Classes.Select(item =>
            AppContextMenu.ItemWithIcon(
                item.Name,
                ClassDot(item.ColorHex),
                () => editor.ChangeSelectedClass(item.Id),
                isEnabled: !locked,
                emphasized: item.Id == label.ClassId)).ToList<Control>();

        AppContextMenu.Create(Canvas,
            AppContextMenu.Item(null, "Icon.Select", () => editor.SetTool(Application.Annotate.AnnotateTool.Select),
                toolTip: "Chọn (chuyển sang công cụ Chọn)"),
            AppContextMenu.SubMenu("Class", "Icon.Tag", classes, isEnabled: classes.Count > 0 && !locked),
            AppContextMenu.Item("Xóa", "Icon.Delete", () => editor.DeleteSelected(), isEnabled: !locked, danger: true))
            .IsOpen = true;
    }

    private static System.Windows.Shapes.Ellipse ClassDot(string hex)
    {
        Brush brush;
        try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch (FormatException) { brush = Brushes.Gray; }
        return new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Typing a class name must not trigger the single-letter shortcuts.
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;

        if (DataContext is AnnotateViewModel model && model.HandleKey(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    private void OnThumbSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThumbList.SelectedItem is { } item)
            ThumbList.ScrollIntoView(item);
    }
}
