using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Storage.Sqlite;
using NTech.DataStudio.Storage.Sqlite.Migrations;

namespace NTech.DataStudio.Storage.Files;

/// <summary>
/// A project as one .zip file (decision 0020). Layout: <c>ntech-package.json</c> first, then the project folder under
/// <c>project/</c>. The database goes in as a copy made with SQLite's online backup, so a project that is open (WAL
/// file and all) is packed in a consistent state without closing it.
/// </summary>
public sealed class ZipProjectPackager : IProjectPackager
{
    public const string ManifestName = "ntech-package.json";
    public const string ProjectPrefix = "project/";

    /// <summary>The version of the layout above. A reader that knows a lower number refuses the package.</summary>
    public const int FormatVersion = 1;

    private const int BufferSize = 1 << 20;
    private const long FreeSpaceMargin = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Pictures and archives are compressed already; squeezing them again costs time and gains nothing.</summary>
    private static readonly HashSet<string> Incompressible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".zip", ".heic", ".avif"
    };

    public PackageEstimate Estimate(string projectFolder)
    {
        var layout = new ProjectFolderLayout(projectFolder);
        var core = Measure(CoreFiles(layout).Select(file => file.Path));
        // The database travels as a copy of the same size.
        if (File.Exists(layout.DatabasePath)) core = (core.Bytes + new FileInfo(layout.DatabasePath).Length, core.Count + 1);
        var source = Measure(Files(layout.SourcePath));
        var exports = Measure(ExportFiles(layout).Select(file => file.Path));
        return new PackageEstimate(core.Bytes, core.Count, source.Bytes, source.Count, exports.Bytes, exports.Count);
    }

    public Task<PackageResult> PackAsync(
        string projectFolder, string destinationFolder, PackageOptions options, IProgress<PackageProgress>? progress, CancellationToken cancellationToken = default) =>
        Task.Run(() => Pack(projectFolder, destinationFolder, options, progress, cancellationToken), cancellationToken);

    public Task<PackageManifest> ReadManifestAsync(string zipPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadManifest(zipPath), cancellationToken);

    public Task<string> UnpackAsync(string zipPath, string targetFolder, IProgress<PackageProgress>? progress, CancellationToken cancellationToken = default) =>
        Task.Run(() => Unpack(zipPath, targetFolder, progress, cancellationToken), cancellationToken);

    // ---- what is in a package ----

    private sealed record Entry(string Name, string Path, long Size);

    /// <summary>The database is added by the packer as a copy; everything else that always travels is listed here.</summary>
    private static IEnumerable<Entry> CoreFiles(ProjectFolderLayout layout) =>
        new[] { layout.MasterPath, layout.ThumbsPath, layout.RejectedPath, layout.VersionsPath }.SelectMany(folder => Entries(layout, folder));

    private static IEnumerable<Entry> ExportFiles(ProjectFolderLayout layout) => Entries(layout, layout.ExportsPath);

    private static IEnumerable<Entry> Entries(ProjectFolderLayout layout, string folder) =>
        Files(folder).Select(file => new Entry(
            ProjectPrefix + System.IO.Path.GetRelativePath(layout.RootPath, file).Replace('\\', '/'), file, new FileInfo(file).Length));

    /// <summary>The files of a folder, without half-written ones (a <c>.partial</c> file or folder is an export or copy that did not finish).</summary>
    private static IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(file => !file.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    .Any(part => part.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)))
            : Enumerable.Empty<string>();

    private static (long Bytes, int Count) Measure(IEnumerable<string> files)
    {
        long bytes = 0;
        var count = 0;
        foreach (var file in files)
        {
            bytes += new FileInfo(file).Length;
            count++;
        }

        return (bytes, count);
    }

    // ---- packing ----

    private PackageResult Pack(string projectFolder, string destinationFolder, PackageOptions options, IProgress<PackageProgress>? progress, CancellationToken cancellationToken)
    {
        var layout = new ProjectFolderLayout(projectFolder);
        if (!File.Exists(layout.DatabasePath))
            throw new PackageException($"Thư mục \"{layout.RootPath}\" không phải project: thiếu {ProjectFolderLayout.DatabaseFileName}.");

        var database = System.IO.Path.GetTempFileName();
        string? partial = null;
        try
        {
            var (name, tasks, imageCount, schema, number) = CopyDatabase(layout, database);
            var entries = CoreFiles(layout)
                .Concat(options.IncludeSource ? Entries(layout, layout.SourcePath) : Enumerable.Empty<Entry>())
                .Concat(options.IncludeExports ? ExportFiles(layout) : Enumerable.Empty<Entry>())
                .Prepend(new Entry(ProjectPrefix + ProjectFolderLayout.DatabaseFileName, database, new FileInfo(database).Length))
                .ToList();
            var total = entries.Sum(entry => entry.Size);

            Directory.CreateDirectory(destinationFolder);
            number = FirstFreeNumber(destinationFolder, name, number);
            var final = System.IO.Path.Combine(destinationFolder, FileName(name, number));
            partial = final + ".partial";
            RequireRoom(destinationFolder, total);

            var manifest = new PackageManifest(
                FormatVersion, AppVersion(), schema, name, DateTimeOffset.UtcNow, number, tasks, imageCount,
                options.IncludeSource, options.IncludeExports, entries.Count, total);

            using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using (var writer = manifestEntry.Open())
                    JsonSerializer.Serialize(writer, manifest, Json);

                long done = 0;
                var buffer = new byte[BufferSize];
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var level = Incompressible.Contains(System.IO.Path.GetExtension(entry.Path)) ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
                    var zipEntry = archive.CreateEntry(entry.Name, level);
                    zipEntry.LastWriteTime = File.GetLastWriteTime(entry.Path);
                    using var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize);
                    using var output = zipEntry.Open();
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;
                        progress?.Report(new PackageProgress("Đang nén project", done, total));
                    }
                }
            }

            File.Move(partial, final);
            partial = null;
            RememberNumber(layout, number);
            return new PackageResult(final, manifest, new FileInfo(final).Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            throw new PackageException($"Không đóng gói được project: {exception.Message}", exception);
        }
        finally
        {
            DeleteQuietly(partial);
            DeleteQuietly(database);
        }
    }

    /// <summary>
    /// Copies the database with SQLite's online backup into <paramref name="target"/> (consistent while the project is open),
    /// sets the package number in the copy, and leaves the copy as a single plain file with no WAL beside it.
    /// </summary>
    private static (string Name, IReadOnlyList<string> Tasks, int ImageCount, int Schema, int Number) CopyDatabase(ProjectFolderLayout layout, string target)
    {
        var source = new SqliteConnectionStringBuilder { DataSource = layout.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        var copy = new SqliteConnectionStringBuilder { DataSource = target, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        using (var from = new SqliteConnection(source))
        using (var to = new SqliteConnection(copy))
        {
            from.Open();
            to.Open();
            from.BackupDatabase(to);
        }

        using var connection = new SqliteConnection(copy);
        connection.Open();
        var schema = ProjectDb.ReadSchemaVersion(connection);
        if (schema > ProjectMigrations.Scripts.Count)
            throw new PackageException("Project này được tạo bằng phiên bản NTech Data Studio mới hơn. Hãy cập nhật ứng dụng.");

        var name = ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'name';") as string;
        var tasks = TargetTasks.ParseSet(ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'target_tasks';") as string
                                         ?? ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'target_task';") as string)
            .Select(task => TargetTasks.Key(task)).ToList();
        var images = ProjectDb.Count(connection, "SELECT COUNT(*) FROM active_images;");
        var last = ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'package_seq';") is string text && int.TryParse(text, out var parsed) ? parsed : 0;
        var number = last + 1;

        ProjectDb.Execute(connection, $"INSERT INTO project_meta (key, value) VALUES ('package_seq', '{number}') ON CONFLICT (key) DO UPDATE SET value = excluded.value;");
        ProjectDb.Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode = DELETE;");
        return (string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(layout.RootPath) : name, tasks, images, schema, number);
    }

    /// <summary>The live project learns the number only once the package exists, so a package that failed leaves the count alone.</summary>
    private static void RememberNumber(ProjectFolderLayout layout, int number)
    {
        var connection = new SqliteConnectionStringBuilder { DataSource = layout.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        using var open = new SqliteConnection(connection);
        open.Open();
        using var command = open.CreateCommand();
        command.CommandText = "INSERT INTO project_meta (key, value) VALUES ('package_seq', $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$value", number.ToString());
        command.ExecuteNonQuery();
    }

    public static string FileName(string projectName, int number) =>
        $"{SafeName(projectName)}_v{number:000}.zip";

    private static string SafeName(string name)
    {
        var safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return safe.Length == 0 ? "project" : safe;
    }

    private static int FirstFreeNumber(string folder, string name, int number)
    {
        while (File.Exists(System.IO.Path.Combine(folder, FileName(name, number)))) number++;
        return number;
    }

    private static void RequireRoom(string folder, long bytes)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder));
            if (root is null) return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < bytes + FreeSpaceMargin)
                throw new PackageException($"Ổ đĩa đích chỉ còn {Gb(free)} trống, mà gói cần khoảng {Gb(bytes)}. Hãy chọn nơi khác hoặc bỏ bớt ảnh nguồn khỏi gói.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            // A path whose free space cannot be asked (for example a network share): go on and let the write say.
        }
    }

    private static string Gb(long bytes) => $"{bytes / 1073741824.0:0.0} GB";

    private static string AppVersion()
    {
        var text = typeof(ZipProjectPackager).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? typeof(ZipProjectPackager).Assembly.GetName().Version?.ToString() ?? "0";
        var plus = text.IndexOf('+');
        return plus < 0 ? text : text[..plus];
    }

    private static void DeleteQuietly(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp file we could not remove is not worth failing the operation for.
        }
    }

    // ---- reading and unpacking ----

    private static PackageManifest ReadManifest(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(ManifestName)
                        ?? throw new PackageException("File này không phải gói project của NTech Data Studio (thiếu " + ManifestName + ").");
            using var reader = entry.Open();
            var manifest = JsonSerializer.Deserialize<PackageManifest>(reader, Json)
                           ?? throw new PackageException("Không đọc được " + ManifestName + " trong gói.");
            if (manifest.FormatVersion > FormatVersion)
                throw new PackageException("Gói này được tạo bằng phiên bản NTech Data Studio mới hơn (định dạng gói " + manifest.FormatVersion + "). Hãy cập nhật ứng dụng.");
            if (manifest.SchemaVersion > ProjectMigrations.Scripts.Count)
                throw new PackageException("Project trong gói này được tạo bằng phiên bản NTech Data Studio mới hơn. Hãy cập nhật ứng dụng.");
            return manifest;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            throw new PackageException($"Không đọc được gói \"{zipPath}\": {exception.Message}", exception);
        }
    }

    private string Unpack(string zipPath, string targetFolder, IProgress<PackageProgress>? progress, CancellationToken cancellationToken)
    {
        var manifest = ReadManifest(zipPath);
        var target = System.IO.Path.GetFullPath(targetFolder);
        if (File.Exists(target) || (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()))
            throw new PackageException($"Thư mục \"{target}\" đã có dữ liệu. Hãy chọn thư mục trống hoặc nơi khác.");

        string? partial = target + ".partial";
        try
        {
            RequireRoom(System.IO.Path.GetDirectoryName(target) ?? target, manifest.TotalBytes);
            if (Directory.Exists(partial)) Directory.Delete(partial, true);
            Directory.CreateDirectory(partial);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                long done = 0;
                var buffer = new byte[BufferSize];
                foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(ProjectPrefix, StringComparison.Ordinal) && !entry.FullName.EndsWith('/')))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = entry.FullName[ProjectPrefix.Length..];
                    var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(partial, relative));
                    // A path that climbs out of the folder (zip slip) is a broken or hostile package: refuse it whole.
                    if (!destination.StartsWith(System.IO.Path.GetFullPath(partial) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new PackageException($"Gói chứa đường dẫn không hợp lệ \"{entry.FullName}\"; không giải nén.");

                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                    using var input = entry.Open();
                    using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;
                        progress?.Report(new PackageProgress("Đang giải nén", done, manifest.TotalBytes));
                    }
                }
            }

            if (!File.Exists(System.IO.Path.Combine(partial, ProjectFolderLayout.DatabaseFileName)))
                throw new PackageException("Gói không chứa " + ProjectFolderLayout.DatabaseFileName + ".");

            if (Directory.Exists(target)) Directory.Delete(target);
            Directory.Move(partial, target);
            partial = null;
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new PackageException($"Không giải nén được gói: {exception.Message}", exception);
        }
        finally
        {
            if (partial is not null && Directory.Exists(partial))
            {
                try { Directory.Delete(partial, true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* leave it; the next try starts over */ }
            }
        }
    }
}
