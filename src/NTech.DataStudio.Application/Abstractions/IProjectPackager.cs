namespace NTech.DataStudio.Application.Abstractions;

/// <summary>What goes into a project package besides what is always there (the database, masters, thumbnails, rejected pictures, versions).</summary>
/// <param name="IncludeSource">The original pictures in <c>source</c>. Often most of the size; without them the Prepare steps cannot be run again from the package.</param>
/// <param name="IncludeExports">The datasets already exported to <c>exports</c>; they can be exported again from a version.</param>
public sealed record PackageOptions(bool IncludeSource = true, bool IncludeExports = false);

/// <summary>How much each part of a project weighs, so the user sees what a package will cost before making it.</summary>
public sealed record PackageEstimate(long CoreBytes, int CoreFiles, long SourceBytes, int SourceFiles, long ExportsBytes, int ExportFiles)
{
    public long TotalBytes(PackageOptions options) =>
        CoreBytes + (options.IncludeSource ? SourceBytes : 0) + (options.IncludeExports ? ExportsBytes : 0);

    public int TotalFiles(PackageOptions options) =>
        CoreFiles + (options.IncludeSource ? SourceFiles : 0) + (options.IncludeExports ? ExportFiles : 0);
}

/// <summary>
/// The first file of a package, <c>ntech-package.json</c>. It is what a reader checks before it unpacks anything: that this
/// is a package, that this app can read it, and what is in it.
/// </summary>
/// <param name="FormatVersion">Version of the package layout itself.</param>
/// <param name="AppVersion">Version of NTech Data Studio that made the package.</param>
/// <param name="SchemaVersion">Version of the project database inside; an app that knows fewer versions refuses the package.</param>
/// <param name="PackageNumber">1 for the first package of the project, then 2, 3 and so on; it is in the file name (<c>name_v003.zip</c>).</param>
/// <param name="Tasks">Keys of the target tasks of the project (decision 0019).</param>
public sealed record PackageManifest(
    int FormatVersion,
    string AppVersion,
    int SchemaVersion,
    string ProjectName,
    DateTimeOffset CreatedAt,
    int PackageNumber,
    IReadOnlyList<string> Tasks,
    int ImageCount,
    bool IncludesSource,
    bool IncludesExports,
    int FileCount,
    long TotalBytes)
{
    /// <summary>Two manifests are equal when everything is, the tasks compared by value (a list has no value equality of its own).</summary>
    public bool Equals(PackageManifest? other) =>
        other is not null && FormatVersion == other.FormatVersion && AppVersion == other.AppVersion && SchemaVersion == other.SchemaVersion
        && ProjectName == other.ProjectName && CreatedAt == other.CreatedAt && PackageNumber == other.PackageNumber
        && Tasks.SequenceEqual(other.Tasks) && ImageCount == other.ImageCount && IncludesSource == other.IncludesSource
        && IncludesExports == other.IncludesExports && FileCount == other.FileCount && TotalBytes == other.TotalBytes;

    public override int GetHashCode() => HashCode.Combine(FormatVersion, ProjectName, CreatedAt, PackageNumber, SchemaVersion, FileCount, TotalBytes);
}

/// <param name="Stage">Words for the step, for example "Đang nén ảnh".</param>
public sealed record PackageProgress(string Stage, long DoneBytes, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)DoneBytes / TotalBytes, 0, 1);
}

public sealed record PackageResult(string FilePath, PackageManifest Manifest, long ZipBytes);

/// <summary>
/// Puts a whole project into one .zip file and brings it back (decision 0020). Nothing in the project is changed except the
/// package number, which is counted up so each package has its own.
/// </summary>
public interface IProjectPackager
{
    /// <summary>The size of each part of the project folder.</summary>
    PackageEstimate Estimate(string projectFolder);

    /// <summary>Writes <c>&lt;name&gt;_v&lt;number&gt;.zip</c> into the folder. The file appears whole or not at all.</summary>
    /// <exception cref="Projects.PackageException">The folder is no project, there is no room, or the file cannot be written.</exception>
    /// <exception cref="OperationCanceledException">Stopped by the user; nothing is left behind.</exception>
    Task<PackageResult> PackAsync(string projectFolder, string destinationFolder, PackageOptions options, IProgress<PackageProgress>? progress, CancellationToken cancellationToken = default);

    /// <summary>Reads only the manifest.</summary>
    /// <exception cref="Projects.PackageException">The file is no package of this app, or was made by a newer one.</exception>
    Task<PackageManifest> ReadManifestAsync(string zipPath, CancellationToken cancellationToken = default);

    /// <summary>Unpacks into <paramref name="targetFolder"/>, which must not exist or be empty. The folder appears whole or not at all.</summary>
    /// <returns>The folder of the project.</returns>
    /// <exception cref="Projects.PackageException">The package cannot be read, is newer than this app, or the folder is taken.</exception>
    /// <exception cref="OperationCanceledException">Stopped by the user; nothing is left behind.</exception>
    Task<string> UnpackAsync(string zipPath, string targetFolder, IProgress<PackageProgress>? progress, CancellationToken cancellationToken = default);
}
