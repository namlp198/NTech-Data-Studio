using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Desktop.ViewModels.Project;

public sealed partial class ProjectViewModel : ObservableObject
{
    private readonly ProjectService _projects;
    private readonly IFolderPicker _folderPicker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _newProjectName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _parentFolder;

    [ObservableProperty]
    private bool _isBusy;

    private bool _syncingTask;

    [ObservableProperty]
    private string? _errorMessage;

    public ProjectViewModel(ProjectService projects, IFolderPicker folderPicker, string defaultParentFolder, ProjectPackageViewModel package)
    {
        Package = package;
        _projects = projects;
        _folderPicker = folderPicker;
        _parentFolder = defaultParentFolder;

        foreach (var task in TargetTasks.All)
        {
            // Segment by default for a new project: polygons also export as Detect.
            NewTasks.Add(new TaskChoice(task, task == TaskKind.Segment, () => { }));
            CurrentTasks.Add(new TaskChoice(task, false, () => OnCurrentTaskToggled()));
        }

        SyncCurrentTasks();

        _projects.CurrentChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(HasProject));
            SyncCurrentTasks();
        };
    }

    /// <summary>Packing the project into a .zip and opening one (decision 0020).</summary>
    public ProjectPackageViewModel Package { get; }

    /// <summary>The tasks offered for the project about to be created (decision 0019: any number can be ticked).</summary>
    public ObservableCollection<TaskChoice> NewTasks { get; } = new();

    /// <summary>The tasks of the open project; ticking or unticking one saves it at once.</summary>
    public ObservableCollection<TaskChoice> CurrentTasks { get; } = new();

    private void SyncCurrentTasks()
    {
        _syncingTask = true;
        try
        {
            var tasks = _projects.Current?.Tasks ?? Array.Empty<TaskKind>();
            foreach (var choice in CurrentTasks) choice.SetWithoutNotice(tasks.Contains(choice.Task));
        }
        finally
        {
            _syncingTask = false;
        }
    }

    private void OnCurrentTaskToggled()
    {
        if (_syncingTask || _projects.Current is null) return;

        var tasks = CurrentTasks.Where(choice => choice.IsChecked).Select(choice => choice.Task).ToList();
        _ = RunAsync(async () =>
        {
            await _projects.SetTargetTasksAsync(tasks);
            // A failed save leaves the boxes showing what the project really has.
            SyncCurrentTasks();
        });
    }

    public ProjectSummary? Current => _projects.Current;

    public bool HasProject => _projects.Current is not null;

    private bool CanCreate() =>
        !string.IsNullOrWhiteSpace(NewProjectName) && !string.IsNullOrWhiteSpace(ParentFolder);

    [RelayCommand]
    private void BrowseParentFolder()
    {
        var folder = _folderPicker.Pick("Chọn thư mục đặt project", ParentFolder);
        if (folder is not null)
            ParentFolder = folder;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        await RunAsync(async () =>
        {
            await _projects.CreateAsync(ParentFolder, NewProjectName, NewTasks.Where(choice => choice.IsChecked).Select(choice => choice.Task).ToList());
            NewProjectName = string.Empty;
        });
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        var folder = _folderPicker.Pick("Mở thư mục project");
        if (folder is not null)
            await RunAsync(() => _projects.OpenAsync(folder));
    }

    /// <summary>Opens the project of the last run. A folder that is gone or broken shows its error here; nothing opens.</summary>
    public async Task RestoreAsync(string folder)
    {
        if (_projects.Current is null)
            await RunAsync(() => _projects.OpenAsync(folder));
    }

    [RelayCommand]
    private void Close()
    {
        ErrorMessage = null;
        _projects.Close();
    }

    private async Task RunAsync(Func<Task> action)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (ProjectException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>One task of a project with its tick box (decision 0019).</summary>
public sealed partial class TaskChoice : ObservableObject
{
    private readonly Action _changed;
    private bool _silent;

    public TaskChoice(TaskKind task, bool isChecked, Action changed)
    {
        Task = task;
        _silent = true;
        IsChecked = isChecked;
        _silent = false;
        _changed = changed;
        Label = TargetTasks.Label(task);
    }

    public TaskKind Task { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_silent) _changed();
    }

    /// <summary>Shows what the project has without telling the owner a change was made.</summary>
    public void SetWithoutNotice(bool value)
    {
        _silent = true;
        try { IsChecked = value; }
        finally { _silent = false; }
    }
}
