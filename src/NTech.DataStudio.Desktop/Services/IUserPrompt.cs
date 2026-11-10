using System.Windows;
using NTech.DataStudio.Desktop.Views.Dialogs;

namespace NTech.DataStudio.Desktop.Services;

/// <summary>How serious a dialog is: its accent colour and badge.</summary>
public enum PromptSeverity
{
    Info,
    Question,
    Warning,
    Error
}

/// <summary>A dialog button's look: the main action, a dangerous one, or a plain one.</summary>
public enum PromptButtonRole
{
    Secondary,
    Primary,
    Danger
}

public sealed record PromptButton(
    string Id,
    string Label,
    PromptButtonRole Role = PromptButtonRole.Secondary,
    bool IsDefault = false,
    bool IsCancel = false);

/// <summary>
/// The app's own message dialog in place of the system MessageBox (the look follows RoboStation's AppDialog).
/// View models depend on this interface so tests can answer for the user.
/// </summary>
public interface IUserPrompt
{
    /// <summary>Two buttons, Cancel and the action. True only for the action; Cancel is the safe way out.</summary>
    /// <param name="danger">The action destroys something: red button, and Cancel is the default.</param>
    bool Confirm(string title, string message, string yesLabel = "Đồng ý", bool danger = false);

    /// <summary>The id of the button pressed, or null when the dialog was closed without one.</summary>
    string? Choose(string title, string message, IReadOnlyList<PromptButton> buttons, PromptSeverity severity = PromptSeverity.Question, string? details = null);

    /// <summary>One Close button.</summary>
    void Inform(string title, string message, PromptSeverity severity = PromptSeverity.Info, string? details = null);
}

public sealed class DialogPrompt : IUserPrompt
{
    public const string Cancel = "cancel";
    public const string Yes = "yes";

    public bool Confirm(string title, string message, string yesLabel = "Đồng ý", bool danger = false) =>
        Choose(title, message,
            new[]
            {
                new PromptButton(Cancel, "Hủy", IsCancel: true, IsDefault: danger),
                new PromptButton(Yes, yesLabel, danger ? PromptButtonRole.Danger : PromptButtonRole.Primary, IsDefault: !danger)
            },
            danger ? PromptSeverity.Warning : PromptSeverity.Question) == Yes;

    public string? Choose(string title, string message, IReadOnlyList<PromptButton> buttons, PromptSeverity severity = PromptSeverity.Question, string? details = null) =>
        AppDialogWindow.ShowRequest(ActiveWindow(), title, message, severity, buttons, details);

    public void Inform(string title, string message, PromptSeverity severity = PromptSeverity.Info, string? details = null) =>
        Choose(title, message, new[] { new PromptButton("ok", "Đóng", PromptButtonRole.Primary, IsDefault: true, IsCancel: true) }, severity, details);

    private static Window? ActiveWindow()
    {
        var app = System.Windows.Application.Current;
        return app?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? app?.MainWindow;
    }
}
