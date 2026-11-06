using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;

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

    [ObservableProperty]
    private string? _errorMessage;

    public ProjectViewModel(ProjectService projects, IFolderPicker folderPicker, string defaultParentFolder)
    {
        _projects = projects;
        _folderPicker = folderPicker;
        _parentFolder = defaultParentFolder;
        _projects.CurrentChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(HasProject));
        };
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
            await _projects.CreateAsync(ParentFolder, NewProjectName);
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
