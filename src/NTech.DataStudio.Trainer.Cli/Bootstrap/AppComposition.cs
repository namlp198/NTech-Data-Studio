using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Processes;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Cli.Bootstrap;

/// <summary>Manual composition root, as in Data Studio.</summary>
public sealed class AppComposition
{
    public AppComposition()
    {
        var runner = new ProcessRunner();
        Containers = new DockerCli(runner);
        Probe = new HardwareProbe(runner, Containers);
        Jobs = new JobService(Containers);
        Catalog = ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog"));
    }

    public IContainerRuntime Containers { get; }

    public HardwareProbe Probe { get; }

    public JobService Jobs { get; }

    public ModelCatalog Catalog { get; }
}
