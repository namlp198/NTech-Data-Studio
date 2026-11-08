using System.Windows.Controls;
using System.Windows.Input;
using NTech.DataStudio.Desktop.ViewModels.Settings;

namespace NTech.DataStudio.Desktop.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // While a row waits for a key, every key goes to it: Tab, Enter and Space must not click or move the focus.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel { IsCapturing: true } model) return;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };
        if (model.HandleCapture(key, Keyboard.Modifiers))
            e.Handled = true;
    }
}
