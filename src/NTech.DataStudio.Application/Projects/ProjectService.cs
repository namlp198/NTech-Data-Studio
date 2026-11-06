using NTech.DataStudio.Application.Abstractions;

namespace NTech.DataStudio.Application.Projects;

/// <summary>Creates, opens and closes the one project the app works on.</summary>
public sealed class ProjectService
{
    private readonly IProjectStore _store;

    public ProjectService(IProjectStore store)
    {
        _store = store;
    }

    public ProjectSummary? Current { get; private set; }

    public event EventHandler? CurrentChanged;

    public async Task<ProjectSummary> CreateAsync(string parentFolder, string name, CancellationToken cancellationToken = default)
    {
        var trimmedName = (name ?? string.Empty).Trim();
        if (trimmedName.Length == 0)
            throw new ProjectException("Tên project không được để trống.");
        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmedName.EndsWith('.'))
            throw new ProjectException($"Tên project \"{trimmedName}\" có ký tự không dùng được cho tên thư mục.");
        if (string.IsNullOrWhiteSpace(parentFolder))
            throw new ProjectException("Chưa chọn thư mục đặt project.");

        var summary = await _store.CreateAsync(Path.Combine(parentFolder, trimmedName), trimmedName, cancellationToken);
        SetCurrent(summary);
        return summary;
    }

    public async Task<ProjectSummary> OpenAsync(string folder, CancellationToken cancellationToken = default)
    {
        var summary = await _store.OpenAsync(folder, cancellationToken);
        SetCurrent(summary);
        return summary;
    }

    public void Close() => SetCurrent(null);

    /// <summary>Updates the counts shown in the top bar when the caller already knows them, without reopening the project.</summary>
    public void UpdateCounts(int imageCount, int annotatedImageCount)
    {
        if (Current is { } project
            && (project.ImageCount != imageCount || project.AnnotatedImageCount != annotatedImageCount))
            SetCurrent(project with { ImageCount = imageCount, AnnotatedImageCount = annotatedImageCount });
    }

    /// <summary>Re-reads the counts of the open project after work changed them.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Current is { } project)
            SetCurrent(await _store.OpenAsync(project.RootPath, cancellationToken));
    }

    /// <exception cref="ProjectException">No project is open.</exception>
    public IAnnotationStore OpenAnnotationStore() =>
        Current is { } project
            ? _store.OpenAnnotationStore(project.RootPath)
            : throw new ProjectException("Chưa mở project.");

    /// <exception cref="ProjectException">No project is open.</exception>
    public IPrepareStore OpenPrepareStore() =>
        Current is { } project
            ? _store.OpenPrepareStore(project.RootPath)
            : throw new ProjectException("Chưa mở project.");

    private void SetCurrent(ProjectSummary? summary)
    {
        Current = summary;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
