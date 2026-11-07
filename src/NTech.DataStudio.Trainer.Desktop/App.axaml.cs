using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NTech.DataStudio.Trainer.Desktop.Bootstrap;
using NTech.DataStudio.Trainer.Desktop.Views;

namespace NTech.DataStudio.Trainer.Desktop;

public sealed class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = AppComposition.CreateShell();
            var window = new MainWindow { DataContext = shell };
            window.Opened += (_, _) => shell.Start();
            // Closing the window leaves every job running: the workspace is the source of truth.
            window.Closing += (_, _) => shell.Stop();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
