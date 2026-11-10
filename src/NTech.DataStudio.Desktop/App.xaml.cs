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
        new Services.DialogPrompt().Inform(
            "Đã xảy ra lỗi không mong muốn",
            e.Exception.Message,
            Services.PromptSeverity.Error,
            e.Exception.ToString());
        e.Handled = true;
    }
}
