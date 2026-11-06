using System.Windows;
using System.Windows.Threading;
using NTech.DataStudio.Desktop.Bootstrap;

namespace NTech.DataStudio.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        MainWindow = AppComposition.CreateMainWindow();
        MainWindow.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Đã xảy ra lỗi không mong muốn:\n\n{e.Exception.Message}",
            "NTech Data Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
