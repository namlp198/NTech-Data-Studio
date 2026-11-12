using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;

namespace NTech.DataStudio.Desktop.ViewModels.Project;

/// <summary>
/// The package half of the Project screen (decision 0020): pack the open project into a versioned .zip, and open a project
/// from one. Both run in the background with a progress bar and a Cancel button.
/// </summary>
public sealed partial class ProjectPackageViewModel : ObservableObject
{
    private readonly ProjectService _projects;
    private readonly ProjectPackageService _packages;
    private readonly IFolderPicker _folders;
    private readonly IFilePicker _files;
    private readonly IFolderOpener _opener;
    private readonly IUserPrompt _prompt;
    private readonly string _defaultFolder;
    private CancellationTokenSource? _cancel;
    private PackageEstimate? _estimate;
    private int _estimateVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _includeSource = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    private bool _includeExports;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PackCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFromZipCommand))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _resultMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyCanExecuteChangedFor(nameof(OpenResultFolderCommand))]
    private string? _resultPath;

    [ObservableProperty]
    private string? _errorMessage;

    public ProjectPackageViewModel(
        ProjectService projects, ProjectPackageService packages, IFolderPicker folders, IFilePicker files,
        IFolderOpener opener, IUserPrompt prompt, string defaultFolder)
    {
        _projects = projects;
        _packages = packages;
        _folders = folders;
        _files = files;
        _opener = opener;
        _prompt = prompt;
        _defaultFolder = defaultFolder;
        _projects.CurrentChanged += (_, _) => OnProjectChanged();
        OnProjectChanged();
    }

    /// <summary>The options can be changed while nothing is running.</summary>
    public bool CanEdit => !IsBusy;

    public bool HasResult => ResultPath is not null;

    /// <summary>Sizes of what the package would hold, so the cost of keeping the original pictures is plain before the click.</summary>
    public string SizeText
    {
        get
        {
            if (_estimate is not { } estimate) return "Đang tính dung lượng...";

            var options = new PackageOptions(IncludeSource, IncludeExports);
            return $"Ảnh nguồn {Size(estimate.SourceBytes)} ({estimate.SourceFiles} file) · Dataset đã xuất {Size(estimate.ExportsBytes)} ({estimate.ExportFiles} file) · "
                   + $"Phần luôn có (database, ảnh master, thumbnail, version) {Size(estimate.CoreBytes)}. Gói dự kiến: {Size(estimate.TotalBytes(options))}.";
        }
    }

    public string SourceText => _estimate is { } estimate ? $"Kèm ảnh nguồn ({Size(estimate.SourceBytes)})" : "Kèm ảnh nguồn";

    public string ExportsText => _estimate is { } estimate ? $"Kèm dataset đã xuất ({Size(estimate.ExportsBytes)})" : "Kèm dataset đã xuất";

    private static string Size(long bytes) => bytes switch
    {
        >= 1073741824 => $"{bytes / 1073741824.0:0.0} GB",
        >= 1048576 => $"{bytes / 1048576.0:0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B"
    };

    private async void OnProjectChanged()
    {
        ResultMessage = null;
        ResultPath = null;
        ErrorMessage = null;
        var version = ++_estimateVersion;
        _estimate = null;
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(ExportsText));
        PackCommand.NotifyCanExecuteChanged();
        if (_projects.Current is null) return;

        try
        {
            // Counting thousands of files must not hold up the screen.
            var estimate = await Task.Run(_packages.Estimate);
            if (version != _estimateVersion) return;
            _estimate = estimate;
        }
        catch (Exception exception) when (exception is PackageException or IOException or UnauthorizedAccessException)
        {
            if (version == _estimateVersion) ErrorMessage = exception.Message;
        }

        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(ExportsText));
    }

    private bool CanPack() => !IsBusy && _projects.Current is not null;

    [RelayCommand(CanExecute = nameof(CanPack))]
    private async Task PackAsync()
    {
        var folder = _folders.Pick("Chọn thư mục lưu file .zip", LastFolder ?? _defaultFolder);
        if (folder is null) return;

        LastFolder = folder;
        await RunAsync("Đang đóng gói project...", async (progress, token) =>
        {
            var result = await _packages.PackAsync(folder, new PackageOptions(IncludeSource, IncludeExports), progress, token);
            ResultPath = result.FilePath;
            ResultMessage = $"Đã đóng gói: {Path.GetFileName(result.FilePath)} ({Size(result.ZipBytes)}, {result.Manifest.FileCount} file, gói số {result.Manifest.PackageNumber}).";
        });
    }

    private string? LastFolder { get; set; }

    private bool CanOpen() => !IsBusy;

    /// <summary>Asks for a package, shows what is in it, asks where to unpack, and opens the project.</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenFromZipAsync()
    {
        var zip = _files.Pick("Chọn gói project (.zip)", "Gói project (*.zip)|*.zip", LastFolder);
        if (zip is null) return;

        PackageManifest manifest;
        ErrorMessage = null;
        try
        {
            manifest = await _packages.ReadManifestAsync(zip);
        }
        catch (PackageException exception)
        {
            ErrorMessage = exception.Message;
            return;
        }

        var tasks = manifest.Tasks.Count == 0 ? "chưa chọn" : string.Join(", ", manifest.Tasks);
        var content = (manifest.IncludesSource ? "có ảnh nguồn" : "không có ảnh nguồn") + (manifest.IncludesExports ? ", có dataset đã xuất" : ", không có dataset đã xuất");
        if (!_prompt.Confirm(
                "Mở project từ gói",
                $"Project \"{manifest.ProjectName}\", gói số {manifest.PackageNumber}, tạo {manifest.CreatedAt.LocalDateTime:dd/MM/yyyy HH:mm} bằng phiên bản {manifest.AppVersion}.\n"
                + $"{manifest.ImageCount} ảnh, task: {tasks}; {content}; {manifest.FileCount} file, khoảng {Size(manifest.TotalBytes)} khi giải nén.\n\n"
                + "Bấm Giải nén rồi chọn thư mục để đặt project.",
                "Giải nén"))
            return;

        var parent = _folders.Pick("Chọn thư mục đặt project", LastFolder ?? _defaultFolder);
        if (parent is null) return;

        LastFolder = parent;
        var target = ProjectPackageService.TargetFolder(parent, manifest);
        await RunAsync("Đang giải nén project...", async (progress, token) =>
        {
            await _packages.UnpackAndOpenAsync(zip, target, progress, token);
            ResultPath = null;
            ResultMessage = $"Đã giải nén và mở project \"{manifest.ProjectName}\" tại {target}."
                            + (manifest.IncludesSource ? string.Empty : " Gói này không có ảnh nguồn nên chưa chạy lại được các bước Prepare từ ảnh gốc.");
        });
    }

    [RelayCommand]
    private void Cancel() => _cancel?.Cancel();

    private bool HasResultFolder() => ResultPath is not null;

    [RelayCommand(CanExecute = nameof(HasResultFolder))]
    private void OpenResultFolder()
    {
        if (ResultPath is { } path && Path.GetDirectoryName(path) is { } folder) _opener.Open(folder);
    }

    private async Task RunAsync(string startText, Func<IProgress<PackageProgress>, CancellationToken, Task> action)
    {
        ErrorMessage = null;
        ResultMessage = null;
        ResultPath = null;
        ProgressPercent = 0;
        ProgressText = startText;
        IsBusy = true;
        _cancel = new CancellationTokenSource();
        var progress = new Progress<PackageProgress>(report =>
        {
            ProgressPercent = report.Fraction * 100;
            ProgressText = $"{report.Stage}: {Size(report.DoneBytes)} / {Size(report.TotalBytes)}";
        });
        try
        {
            await action(progress, _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "Đã hủy; không có file nào được tạo.";
        }
        catch (Exception exception) when (exception is PackageException or ProjectException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _cancel.Dispose();
            _cancel = null;
            IsBusy = false;
            ProgressText = null;
        }
    }
}
