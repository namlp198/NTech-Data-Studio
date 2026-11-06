using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Desktop.Views.Prepare;

/// <summary>Mouse handling for drawing the ROI on the preview, in source-image pixels.</summary>
public partial class NormalizeStepView : UserControl
{
    private const double MinRoiPixels = 8;

    private Point? _dragStart;

    public NormalizeStepView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        BeforeHost.SizeChanged += (_, _) => DrawRoi();
    }

    private NormalizeStepViewModel? ViewModel => DataContext as NormalizeStepViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldSource)
            oldSource.PropertyChanged -= OnViewModelChanged;
        if (e.NewValue is INotifyPropertyChanged newSource)
            newSource.PropertyChanged += OnViewModelChanged;
        DrawRoi();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NormalizeStepViewModel.Roi)
            or nameof(NormalizeStepViewModel.IsRoi)
            or nameof(NormalizeStepViewModel.PreviewSourceWidth)
            or nameof(NormalizeStepViewModel.PreviewSourceHeight))
            DrawRoi();
    }

    /// <summary>Where the source image sits inside the host, given Stretch="Uniform".</summary>
    private bool TryGetImageArea(out Rect area, out double pixelsPerSourcePixel)
    {
        area = default;
        pixelsPerSourcePixel = 0;
        if (ViewModel is not { PreviewSourceWidth: > 0, PreviewSourceHeight: > 0 } model) return false;
        if (BeforeHost.ActualWidth <= 0 || BeforeHost.ActualHeight <= 0) return false;

        pixelsPerSourcePixel = Math.Min(
            BeforeHost.ActualWidth / model.PreviewSourceWidth,
            BeforeHost.ActualHeight / model.PreviewSourceHeight);
        var width = model.PreviewSourceWidth * pixelsPerSourcePixel;
        var height = model.PreviewSourceHeight * pixelsPerSourcePixel;
        area = new Rect((BeforeHost.ActualWidth - width) / 2, (BeforeHost.ActualHeight - height) / 2, width, height);
        return true;
    }

    private void OnRoiMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsRoi: true }) return;

        _dragStart = e.GetPosition(BeforeHost);
        BeforeHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnRoiMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is { } start)
            DrawBox(new Rect(start, e.GetPosition(BeforeHost)));
    }

    private void OnRoiMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start) return;

        _dragStart = null;
        BeforeHost.ReleaseMouseCapture();
        if (ViewModel is { } model && TryGetImageArea(out var area, out var scale))
        {
            var selection = Rect.Intersect(new Rect(start, e.GetPosition(BeforeHost)), area);
            if (!selection.IsEmpty && selection.Width / scale >= MinRoiPixels && selection.Height / scale >= MinRoiPixels)
            {
                model.SetRoi(new PixelRect(
                    Math.Round((selection.X - area.X) / scale),
                    Math.Round((selection.Y - area.Y) / scale),
                    Math.Round(selection.Width / scale),
                    Math.Round(selection.Height / scale)));
            }
        }

        DrawRoi();
    }

    private void DrawRoi()
    {
        if (ViewModel is { IsRoi: true, Roi: { } roi } && TryGetImageArea(out var area, out var scale))
            DrawBox(new Rect(area.X + roi.X * scale, area.Y + roi.Y * scale, roi.Width * scale, roi.Height * scale));
        else
            RoiBox.Visibility = Visibility.Collapsed;
    }

    private void DrawBox(Rect box)
    {
        RoiBox.Visibility = Visibility.Visible;
        Canvas.SetLeft(RoiBox, box.X);
        Canvas.SetTop(RoiBox, box.Y);
        RoiBox.Width = box.Width;
        RoiBox.Height = box.Height;
    }
}
