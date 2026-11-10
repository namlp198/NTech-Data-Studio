using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NTech.DataStudio.Desktop.Services;

namespace NTech.DataStudio.Desktop.Views.Dialogs;

/// <summary>The window behind <see cref="DialogPrompt"/>. View work only.</summary>
public partial class AppDialogWindow : Window
{
    private string? _result;

    private AppDialogWindow(string title, string message, PromptSeverity severity, IReadOnlyList<PromptButton> buttons, string? details)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsText.Text = details;
            DetailsExpander.Visibility = Visibility.Visible;
        }

        var accent = (Brush)FindResource(severity switch
        {
            PromptSeverity.Info => "Brush.Status.Info",
            PromptSeverity.Question => "Brush.Brand.Primary",
            PromptSeverity.Warning => "Brush.Status.Warning",
            _ => "Brush.Status.Error"
        });
        AccentBar.Background = accent;
        Badge.Background = accent;
        BadgeGlyph.Text = severity switch
        {
            PromptSeverity.Info => "i",
            PromptSeverity.Question => "?",
            _ => "!"
        };

        foreach (var button in buttons)
            ButtonPanel.Children.Add(CreateButton(button));
        Loaded += (_, _) => (ButtonPanel.Children.OfType<Button>().FirstOrDefault(button => button.IsDefault)
                             ?? ButtonPanel.Children.OfType<Button>().LastOrDefault())?.Focus();
    }

    /// <summary>Shows the dialog modally over <paramref name="owner"/>; the pressed button's id, or null.</summary>
    internal static string? ShowRequest(
        Window? owner, string title, string message, PromptSeverity severity, IReadOnlyList<PromptButton> buttons, string? details)
    {
        var window = new AppDialogWindow(title, message, severity, buttons, details);
        if (owner is { IsLoaded: true })
            window.Owner = owner;
        else
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
        return window._result;
    }

    private Button CreateButton(PromptButton spec)
    {
        var button = new Button
        {
            Content = spec.Label,
            MinWidth = (double)FindResource("Layout.DialogButtonMinWidth"),
            Margin = (Thickness)FindResource("Gap.Left.SM"),
            IsDefault = spec.IsDefault,
            IsCancel = spec.IsCancel,
            Style = (Style)FindResource(spec.Role switch
            {
                PromptButtonRole.Primary => "Button.Primary",
                PromptButtonRole.Danger => "Button.Danger",
                _ => "Button.Secondary"
            })
        };
        button.Click += (_, _) =>
        {
            _result = spec.Id;
            Close();
        };
        return button;
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
