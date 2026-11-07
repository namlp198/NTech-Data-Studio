using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>
/// What the screens share: the open workspace and the last hardware reading. Nothing about a run
/// is kept here; runs are always read back from the workspace folder.
/// </summary>
public sealed partial class TrainerSession : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWorkspace))]
    private Workspace? _workspace;

    [ObservableProperty]
    private HardwareSnapshot? _machine;

    [ObservableProperty]
    private DatasetReport? _datasetReport;

    public bool HasWorkspace => Workspace is not null;

    /// <summary>Asked by Config after starting a run, so Train opens on it.</summary>
    public event Action<string>? RunStarted;

    public void AnnounceRun(string runId) => RunStarted?.Invoke(runId);

    partial void OnWorkspaceChanged(Workspace? value) => DatasetReport = null;
}
