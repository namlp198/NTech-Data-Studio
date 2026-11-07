using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Desktop.ViewModels;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Processes;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.Bootstrap;

/// <summary>Manual composition root, as in Data Studio and the CLI.</summary>
public static class AppComposition
{
    public static ShellViewModel CreateShell()
    {
        var runner = new ProcessRunner();
        var containers = new DockerCli(runner);
        var services = new TrainerServices(
            ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog")),
            new HardwareProbe(runner, containers),
            new JobService(containers),
            new DatasetChecker(containers),
            containers,
            new AvaloniaDialogs(),
            new JsonSettingsStore());
        return new ShellViewModel(services);
    }
}
