using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.Shell;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Desktop.ViewModels.Export;

/// <summary>One finding of the validator, as the screen lists it.</summary>
public sealed record IssueRow(bool IsError, string Message)
{
    public string Text => (IsError ? "Lỗi: " : "Cảnh báo: ") + Message;
}

/// <summary>
/// Module 4: pick a version and a model profile, see what the validator finds, and write the dataset.
/// Validation runs on every change of the choice; errors block the export button, warnings do not.
/// </summary>
public sealed partial class ExportViewModel : ObservableObject, IActivatable
{
    private const int MinImgsz = 32;
    private const int MaxImgsz = 8192;

    private readonly ProjectService _projects;
    private readonly DatasetService _datasets;
    private readonly ExportService _export;
    private readonly IModelProfileCatalog _catalog;
    private readonly IFolderPicker _folderPicker;
    private readonly IFolderOpener _opener;

    private CancellationTokenSource? _validation;
    private CancellationTokenSource? _running;
    private string? _loadedRoot;
    private string? _outputParent;
    private bool _active;
    private bool _suspend;

    [ObservableProperty] private Option<DatasetVersion>? _selectedVersion;
    [ObservableProperty] private Option<ModelProfile>? _selectedProfile;
    [ObservableProperty] private Option<ExportImageFormat> _selectedImageFormat;
    [ObservableProperty] private string _imgszText = "640";
    [ObservableProperty] private bool _zip;
    [ObservableProperty] private bool _overwrite;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private bool _isValidating;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _resultMessage;
    [ObservableProperty] private string? _resultFolder;
    [ObservableProperty] private string _targetFolder = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _canExport;

    public ExportViewModel(
        ProjectService projects,
        DatasetService datasets,
        ExportService export,
        IModelProfileCatalog catalog,
        IFolderPicker folderPicker,
        IFolderOpener opener)
    {
        _projects = projects;
        _datasets = datasets;
        _export = export;
        _catalog = catalog;
        _folderPicker = folderPicker;
        _opener = opener;

        ImageFormats = new[]
        {
            new Option<ExportImageFormat>("JPEG chất lượng 95 (nhỏ, hợp ảnh chụp)", ExportImageFormat.Jpeg),
            new Option<ExportImageFormat>("PNG (không mất dữ liệu, nặng hơn nhiều)", ExportImageFormat.Png)
        };
        _selectedImageFormat = ImageFormats[0];

        _projects.CurrentChanged += OnProjectChanged;
    }

    public IReadOnlyList<Option<ExportImageFormat>> ImageFormats { get; }
    public ObservableCollection<Option<DatasetVersion>> Versions { get; } = new();
    public ObservableCollection<Option<ModelProfile>> Profiles { get; } = new();
    public ObservableCollection<IssueRow> Issues { get; } = new();

    public bool HasProject => _projects.Current is not null;
    public bool ShowNoVersions => HasProject && !IsLoading && LoadError is null && Versions.Count == 0;
    public bool ShowForm => HasProject && !IsLoading && LoadError is null && Versions.Count > 0;
    public bool HasIssues => Issues.Count > 0;
    public bool HasResult => ResultMessage is not null;
    public bool IsIdle => !IsBusy;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(IsLoading) or nameof(LoadError):
                OnPropertyChanged(nameof(ShowForm));
                OnPropertyChanged(nameof(ShowNoVersions));
                break;
            case nameof(IsBusy):
                OnPropertyChanged(nameof(IsIdle));
                UpdateCanExport();
                break;
            case nameof(ResultMessage):
                OnPropertyChanged(nameof(HasResult));
                break;
            case nameof(SelectedProfile):
                if (!_suspend && SelectedProfile is { } profile)
                    ImgszText = profile.Value.ImgszDefault.ToString(CultureInfo.InvariantCulture);
                RefreshTarget();
                _ = ValidateAsync();
                break;
            case nameof(SelectedVersion):
                RefreshTarget();
                _ = ValidateAsync();
                break;
            case nameof(ImgszText):
                _ = ValidateAsync();
                break;
        }
    }

    // ---- activation and loading ----

    public async Task ActivateAsync()
    {
        _active = true;
        OnPropertyChanged(nameof(HasProject));
        if (HasProject)
            await LoadAsync();
    }

    public void Deactivate()
    {
        _active = false;
        _validation?.Cancel();
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ShowForm));
        OnPropertyChanged(nameof(ShowNoVersions));

        var root = _projects.Current?.RootPath;
        if (root is null)
        {
            _validation?.Cancel();
            _loadedRoot = null;
            Versions.Clear();
            Issues.Clear();
            Summary = string.Empty;
            ResultMessage = null;
            ErrorMessage = null;
            UpdateCanExport();
        }
        else if (_active && root != _loadedRoot)
        {
            _ = LoadAsync();
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => HasProject ? LoadAsync() : Task.CompletedTask;

    private async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            var (versions, counts, root, profiles) = await Task.Run(() =>
            {
                var workspace = _datasets.Load();
                var perVersion = workspace.Versions.ToDictionary(
                    version => version.Id, version => _datasets.LoadAssignments(version.Id).Count);
                return (workspace.Versions, perVersion, workspace.ProjectRoot, _catalog.LoadAll().Where(_export.Supports).ToList());
            });

            _loadedRoot = root;
            ResultMessage = null;
            ErrorMessage = null;

            _suspend = true;
            try
            {
                var previousVersion = SelectedVersion?.Value.Id;
                var previousProfile = SelectedProfile?.Value.Id;

                Versions.Clear();
                foreach (var version in versions.OrderByDescending(version => version.CreatedAt))
                    Versions.Add(new Option<DatasetVersion>($"{version.Name} · {counts[version.Id]} ảnh · seed {version.Seed}", version));
                Profiles.Clear();
                foreach (var profile in profiles)
                    Profiles.Add(new Option<ModelProfile>($"{profile.Id} ({FormatName(profile)})", profile));

                SelectedVersion = Versions.FirstOrDefault(option => option.Value.Id == previousVersion) ?? Versions.FirstOrDefault();
                SelectedProfile = Profiles.FirstOrDefault(option => option.Value.Id == previousProfile)
                    ?? Profiles.FirstOrDefault(option => _projects.Current?.Tasks.Contains(option.Value.Task) == true)
                    ?? Profiles.FirstOrDefault();
                if (SelectedProfile is { } selected && previousProfile != selected.Value.Id)
                    ImgszText = selected.Value.ImgszDefault.ToString(CultureInfo.InvariantCulture);
            }
            finally
            {
                _suspend = false;
            }

            OnPropertyChanged(nameof(ShowForm));
            OnPropertyChanged(nameof(ShowNoVersions));
            RefreshTarget();
            await ValidateAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LoadError = exception.Message;
            Versions.Clear();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatName(ModelProfile profile) =>
        ExportKinds.For(profile) switch
        {
            ExportKind.CocoDetect or ExportKind.CocoSegment => "COCO json",
            ExportKind.YoloClassify => "thư mục theo class",
            _ => "YOLO txt"
        };

    // ---- validation ----

    private bool TryReadImgsz(out int imgsz) =>
        int.TryParse(ImgszText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out imgsz)
        && imgsz is >= MinImgsz and <= MaxImgsz;

    private void UpdateCanExport() =>
        CanExport = !IsBusy && !IsValidating && SelectedVersion is not null && SelectedProfile is not null
                    && TryReadImgsz(out _) && Issues.All(issue => !issue.IsError) && Summary.Length > 0;

    private async Task ValidateAsync()
    {
        // Not while LoadAsync is still filling the lists; it calls this itself when it is done.
        if (_suspend) return;

        _validation?.Cancel();
        ResultMessage = null;
        ErrorMessage = null;

        if (SelectedVersion is not { } version || SelectedProfile is not { } profile)
        {
            Issues.Clear();
            Summary = string.Empty;
            UpdateCanExport();
            return;
        }

        if (!TryReadImgsz(out var imgsz))
        {
            Issues.Clear();
            Issues.Add(new IssueRow(true, $"imgsz phải là số nguyên từ {MinImgsz} đến {MaxImgsz}."));
            Summary = "Không kiểm tra được";
            OnPropertyChanged(nameof(HasIssues));
            UpdateCanExport();
            return;
        }

        var source = _validation = new CancellationTokenSource();
        IsValidating = true;
        Summary = string.Empty;
        UpdateCanExport();
        try
        {
            var report = await Task.Run(() => _export.Validate(version.Value.Id, profile.Value, imgsz), source.Token);
            if (source.IsCancellationRequested) return;
            ShowReport(report);
        }
        catch (OperationCanceledException)
        {
            // A newer choice replaced this one.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (source.IsCancellationRequested) return;
            Issues.Clear();
            Issues.Add(new IssueRow(true, exception.Message));
            Summary = "Không kiểm tra được";
            OnPropertyChanged(nameof(HasIssues));
        }
        finally
        {
            if (ReferenceEquals(_validation, source))
            {
                IsValidating = false;
                UpdateCanExport();
            }
        }
    }

    private void ShowReport(ValidationReport report)
    {
        Issues.Clear();
        foreach (var issue in report.Issues.OrderByDescending(issue => issue.Severity))
            Issues.Add(new IssueRow(issue.Severity == IssueSeverity.Error, issue.Message));

        Summary = report.Issues.Count == 0
            ? "Hợp lệ, không có cảnh báo"
            : $"{report.ErrorCount} lỗi, {report.WarningCount} cảnh báo";
        OnPropertyChanged(nameof(HasIssues));
        UpdateCanExport();
    }

    private void RefreshTarget()
    {
        if (SelectedVersion is not { } version || SelectedProfile is not { } profile || !HasProject)
        {
            TargetFolder = string.Empty;
            return;
        }

        try
        {
            TargetFolder = _export.DefaultFolder(version.Value, profile.Value, _outputParent);
        }
        catch (ProjectException)
        {
            TargetFolder = string.Empty;
        }
    }

    // ---- commands ----

    [RelayCommand]
    private void ChooseFolder()
    {
        var picked = _folderPicker.Pick("Chọn thư mục chứa bản xuất", _outputParent);
        if (picked is null) return;
        _outputParent = picked;
        RefreshTarget();
    }

    [RelayCommand]
    private void UseDefaultFolder()
    {
        _outputParent = null;
        RefreshTarget();
    }

    [RelayCommand]
    private void OpenResult()
    {
        if (ResultFolder is { } folder) _opener.Open(folder);
    }

    [RelayCommand]
    private void Cancel() => _running?.Cancel();

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!CanExport || SelectedVersion is not { } version || SelectedProfile is not { } profile || !TryReadImgsz(out var imgsz))
            return;

        var request = new ExportRequest(
            version.Value.Id, profile.Value, imgsz, SelectedImageFormat.Value, 95, Zip, Overwrite, _outputParent);

        ResultMessage = null;
        ErrorMessage = null;
        ResultFolder = null;
        ProgressPercent = 0;
        ProgressText = "Đang kiểm tra...";
        IsBusy = true;
        _running = new CancellationTokenSource();
        var token = _running.Token;
        var progress = new Progress<ExportProgress>(Show);
        try
        {
            var result = await Task.Run(() => _export.Export(request, progress, token), token);
            ResultFolder = result.Folder;
            ResultMessage = Describe(result);
            ShowReport(result.Report);
        }
        catch (ExportBlockedException blocked)
        {
            ShowReport(blocked.Report);
            ErrorMessage = blocked.Message;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Đã hủy. Chưa có gì được ghi.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or ImageReadException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _running.Dispose();
            _running = null;
            IsBusy = false;
        }
    }

    private void Show(ExportProgress progress)
    {
        switch (progress.Phase)
        {
            case ExportPhase.Rendering:
                ProgressText = $"Đang dựng ảnh {progress.Done}/{progress.Total}";
                ProgressPercent = progress.Total == 0 ? 0 : progress.Done * 90.0 / progress.Total;
                break;
            case ExportPhase.WritingLabels:
                ProgressText = "Đang ghi nhãn và tài liệu...";
                ProgressPercent = 92;
                break;
            case ExportPhase.Packing:
                ProgressText = "Đang nén zip...";
                ProgressPercent = 96;
                break;
            default:
                ProgressText = "Đang kiểm tra...";
                break;
        }
    }

    private static string Describe(ExportResult result)
    {
        var images = result.ImagesPerSplit;
        var text = $"Đã xuất {images.Sum()} ảnh (train {images[0]}, val {images[1]}, test {images[2]}) và {result.LabelsPerSplit.Sum()} nhãn vào {result.Folder}.";
        if (result.SkippedLabels > 0)
            text += $" {result.SkippedLabels} nhãn không ghi được vào định dạng này nên bị bỏ.";
        if (result.ZipPath is { } zip)
            text += $" Đã nén thành {zip}.";
        return text;
    }
}
