using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>
/// The window: five tabs over one workspace. A tab is chosen through IsSelected, never a command,
/// because a RadioButton can become checked without a click (same trap as Data Studio).
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly TrainerServices _services;

    public ShellViewModel(TrainerServices services)
    {
        _services = services;
        Session = new TrainerSession();
        Machine = new MachineViewModel(Session, services);
        Dataset = new DatasetViewModel(Session, services);
        Config = new ConfigViewModel(Session, services);
        Train = new TrainViewModel(Session, services);
        Results = new ResultsViewModel(Session, services);
        Pages = [Machine, Dataset, Config, Train, Results];
        foreach (var page in Pages) page.PropertyChanged += OnPagePropertyChanged;
        Session.RunStarted += _ => Train.IsSelected = true;
        Session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(TrainerSession.Workspace)) return;
            OnPropertyChanged(nameof(WorkspaceName));
            OnPropertyChanged(nameof(WorkspacePath));
            OnPropertyChanged(nameof(HasWorkspace));
            OnPropertyChanged(nameof(NeedsWorkspace));
        };
    }

    public TrainerSession Session { get; }

    public MachineViewModel Machine { get; }

    public DatasetViewModel Dataset { get; }

    public ConfigViewModel Config { get; }

    public TrainViewModel Train { get; }

    public ResultsViewModel Results { get; }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsWorkspace))]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasWorkspace => Session.HasWorkspace;

    /// <summary>Every tab but Machine works on a workspace; without one it shows how to get one.</summary>
    public bool NeedsWorkspace => !Session.HasWorkspace && CurrentPage is not null && CurrentPage != Machine;

    public string WorkspaceName => Session.Workspace?.File_.Name ?? "Chưa mở workspace";

    public string WorkspacePath => Session.Workspace?.Root ?? "";

    /// <summary>Called once the window is up: opens the last workspace, then shows Machine.</summary>
    public void Start()
    {
        if (_services.Settings.LastWorkspace is { Length: > 0 } last && Directory.Exists(last))
        {
            try
            {
                Session.Workspace = Workspace.Open(last);
            }
            catch (Exception problem) when (PageViewModel.IsExpected(problem))
            {
                Error = $"Không mở lại được workspace {last}: {problem.Message}";
            }
        }

        Machine.IsSelected = true;
    }

    public void Stop() => CurrentPage?.Deactivate();

    [RelayCommand]
    private async Task OpenWorkspaceAsync()
    {
        var folder = await _services.Dialogs.PickFolderAsync("Chọn thư mục workspace (có workspace.json)");
        if (folder is null) return;
        Use(() => Workspace.Open(folder));
    }

    /// <summary>A workspace only references the dataset: nothing is copied and data.yaml is never changed.</summary>
    [RelayCommand]
    private async Task CreateWorkspaceAsync()
    {
        var dataYaml = await _services.Dialogs.PickDataYamlAsync();
        if (dataYaml is null) return;
        var folder = await _services.Dialogs.PickFolderAsync("Chọn thư mục cho workspace mới (ngoài thư mục dataset)");
        if (folder is null) return;
        Use(() => Workspace.Create(folder, dataYaml));
    }

    private void Use(Func<Workspace> open)
    {
        try
        {
            var workspace = open();
            Session.Workspace = workspace;
            _services.Settings.LastWorkspace = workspace.Root;
            Error = null;
            CurrentPage?.Deactivate();
            CurrentPage?.Activate();
        }
        catch (Exception problem) when (PageViewModel.IsExpected(problem))
        {
            Error = problem.Message;
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName != nameof(PageViewModel.IsSelected) || sender is not PageViewModel page || !page.IsSelected) return;
        if (CurrentPage == page) return;
        var previous = CurrentPage;
        foreach (var other in Pages)
            if (other != page) other.IsSelected = false;
        previous?.Deactivate();
        CurrentPage = page;
        page.Activate();
    }
}
