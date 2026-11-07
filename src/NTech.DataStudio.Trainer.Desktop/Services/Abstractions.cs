using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.Services;

/// <summary>What the screens need from the platform. Kept out of the ViewModels so they test without a window.</summary>
public interface IDialogs
{
    Task<string?> PickDataYamlAsync();

    Task<string?> PickFolderAsync(string title);

    Task OpenFolderAsync(string path);
}

/// <summary>Remembers the last workspace between sessions; nothing else.</summary>
public interface ISettingsStore
{
    string? LastWorkspace { get; set; }
}

/// <summary>A screen that holds a timer must start it when shown and release it when left (as in Data Studio).</summary>
public interface IActivatable
{
    void Activate();

    void Deactivate();
}

/// <summary>The use cases of NTech.DataStudio.Trainer, the same ones the CLI calls.</summary>
public sealed record TrainerServices(
    ModelCatalog Catalog,
    HardwareProbe Probe,
    JobService Jobs,
    DatasetChecker Datasets,
    IContainerRuntime Containers,
    IDialogs Dialogs,
    ISettingsStore Settings,
    bool Polling = true);
