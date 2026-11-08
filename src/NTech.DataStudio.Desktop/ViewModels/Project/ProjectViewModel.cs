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

    private Option<TaskKind?> _newTask;
    private bool _syncingTask;

    [ObservableProperty]
    private string? _errorMessage;

    public ProjectViewModel(ProjectService projects, IFolderPicker folderPicker, string defaultParentFolder)
    {
        _projects = projects;
        _folderPicker = folderPicker;
        _parentFolder = defaultParentFolder;

        TaskOptions.Add(new Option<TaskKind?>("Chưa chọn (dùng được mọi công cụ vẽ)", null));
        foreach (var task in TargetTasks.All)
            TaskOptions.Add(new Option<TaskKind?>(TargetTasks.Label(task), task));
        _newTask = TaskOptions.First(option => option.Value == TaskKind.Segment);
        SyncCurrentTask();

        _projects.CurrentChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(HasProject));
            SyncCurrentTask();
        };
    }

    /// <summary>Choices for the task a project aims at (decision 0014).</summary>
    public ObservableCollection<Option<TaskKind?>> TaskOptions { get; } = new();

    /// <summary>Task of the project about to be created. Segment by default: polygons also export as Detect.</summary>
    public Option<TaskKind?> NewTask
    {
        get => _newTask;
        set => SetProperty(ref _newTask, value);
    }

    private Option<TaskKind?>? _currentTask;

    /// <summary>Task of the open project; choosing another one saves it at once.</summary>
    public Option<TaskKind?>? CurrentTask
    {
        get => _currentTask;
        set
        {
            if (value is null || !SetProperty(ref _currentTask, value)) return;
            if (!_syncingTask) _ = SaveTaskAsync(value.Value);
        }
    }

    private void SyncCurrentTask()
    {
        _syncingTask = true;
        try
        {
            CurrentTask = _projects.Current is { } project
                ? TaskOptions.First(option => option.Value == project.TargetTask)
                : null;
        }
        finally
        {
            _syncingTask = false;
        }
    }

    private async Task SaveTaskAsync(TaskKind? task) => await RunAsync(() => _projects.SetTargetTaskAsync(task));
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
            await _projects.CreateAsync(ParentFolder, NewProjectName, NewTask.Value);
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
