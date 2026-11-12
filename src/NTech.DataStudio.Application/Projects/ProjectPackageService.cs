using NTech.DataStudio.Application.Abstractions;

namespace NTech.DataStudio.Application.Projects;

/// <summary>A package could not be made or read; <see cref="Exception.Message"/> is shown to the user.</summary>
public sealed class PackageException : Exception
{
    public PackageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Packs the open project into a .zip and opens a project from one (decision 0020).</summary>
public sealed class ProjectPackageService
{
    private readonly ProjectService _projects;
    private readonly IProjectPackager _packager;

    public ProjectPackageService(ProjectService projects, IProjectPackager packager)
    {
        _projects = projects;
        _packager = packager;
    }

    /// <exception cref="PackageException">No project is open.</exception>
    public PackageEstimate Estimate() => _packager.Estimate(Current().RootPath);

    /// <exception cref="PackageException">No project is open, there is no room, or the file cannot be written.</exception>
    public Task<PackageResult> PackAsync(string destinationFolder, PackageOptions options, IProgress<PackageProgress>? progress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new PackageException("Chưa chọn thư mục lưu file .zip.");

        return _packager.PackAsync(Current().RootPath, destinationFolder, options, progress, cancellationToken);
    }

    public Task<PackageManifest> ReadManifestAsync(string zipPath, CancellationToken cancellationToken = default) =>
        _packager.ReadManifestAsync(zipPath, cancellationToken);

    /// <summary>The folder a package is unpacked into: <paramref name="parentFolder"/> and the name of the project in it.</summary>
    public static string TargetFolder(string parentFolder, PackageManifest manifest)
    {
        var name = string.Concat(manifest.ProjectName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return Path.Combine(parentFolder, name.Length == 0 ? "project" : name);
    }

    /// <summary>Unpacks the package and opens the project in it.</summary>
    /// <exception cref="PackageException">The package cannot be read or the folder is taken.</exception>
    /// <exception cref="ProjectException">The project inside cannot be opened.</exception>
    public async Task<ProjectSummary> UnpackAndOpenAsync(string zipPath, string targetFolder, IProgress<PackageProgress>? progress, CancellationToken cancellationToken = default)
    {
        var folder = await _packager.UnpackAsync(zipPath, targetFolder, progress, cancellationToken);
        return await _projects.OpenAsync(folder, cancellationToken);
    }

    private ProjectSummary Current() => _projects.Current ?? throw new PackageException("Chưa mở project.");
}
