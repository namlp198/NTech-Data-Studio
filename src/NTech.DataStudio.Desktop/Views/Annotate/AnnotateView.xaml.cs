using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        _subscribed = null;
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
