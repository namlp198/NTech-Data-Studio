using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Storage.Files;

namespace NTech.DataStudio.Storage.Sqlite;

public sealed class SqliteProjectStore : IProjectStore
{
    public Task<ProjectSummary> CreateAsync(string folder, string name, IReadOnlyList<TaskKind>? targetTasks = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Guarded(() => Create(folder, name, targetTasks), folder), cancellationToken);

    public Task<ProjectSummary> SetTargetTasksAsync(string folder, IReadOnlyList<TaskKind> targetTasks, CancellationToken cancellationToken = default) =>
        Task.Run(() => Guarded(() => SetTargetTasks(folder, targetTasks), folder), cancellationToken);

    public Task<ProjectSummary> OpenAsync(string folder, CancellationToken cancellationToken = default) =>
        Task.Run(() => Guarded(() => Open(folder), folder), cancellationToken);

    public IPrepareStore OpenPrepareStore(string folder) => new SqlitePrepareStore(new ProjectFolderLayout(folder));

    public IAnnotationStore OpenAnnotationStore(string folder) => new SqliteAnnotationStore(new ProjectFolderLayout(folder));

    public IDatasetStore OpenDatasetStore(string folder) => new SqliteDatasetStore(new ProjectFolderLayout(folder));

    private static ProjectSummary Create(string folder, string name, IReadOnlyList<TaskKind>? targetTasks)
    {
        var layout = new ProjectFolderLayout(folder);
        if (Directory.Exists(layout.RootPath) && Directory.EnumerateFileSystemEntries(layout.RootPath).Any())
            throw new ProjectException($"Thư mục \"{layout.RootPath}\" đã có dữ liệu. Hãy chọn thư mục trống hoặc đặt tên khác.");

        layout.EnsureDirectories();

        var db = new ProjectDb(layout.DatabasePath);
        using var connection = db.Open();
        db.TryMigrate(connection);

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO project_meta (key, value) VALUES ('name', $name), ('created_at', $createdAt);";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        WriteTargetTasks(connection, targetTasks ?? Array.Empty<TaskKind>());

        return ReadSummary(connection, layout);
    }

    private static ProjectSummary SetTargetTasks(string folder, IReadOnlyList<TaskKind> targetTasks)
    {
        var layout = new ProjectFolderLayout(folder);
        if (!File.Exists(layout.DatabasePath))
            throw new ProjectException($"Thư mục \"{layout.RootPath}\" không phải project: thiếu {ProjectFolderLayout.DatabaseFileName}.");

        var db = new ProjectDb(layout.DatabasePath);
        using var connection = db.Open();
        if (!db.TryMigrate(connection))
            throw new ProjectException("Project này được tạo bằng phiên bản NTech Data Studio mới hơn. Hãy cập nhật ứng dụng.");

        WriteTargetTasks(connection, targetTasks);
        return ReadSummary(connection, layout);
    }

    /// <summary>
    /// Writes <c>target_tasks</c> (the keys joined by commas). The first task is also kept in the old <c>target_task</c> key so
    /// an older build still shows one task; a project written before this (only the old key) reads as that one task.
    /// </summary>
    private static void WriteTargetTasks(SqliteConnection connection, IReadOnlyList<TaskKind> targetTasks)
    {
        var tasks = TargetTasks.Normalize(targetTasks);
        Write(connection, "target_tasks", tasks.Count == 0 ? null : TargetTasks.Key(tasks));
        Write(connection, "target_task", tasks.Count == 0 ? null : TargetTasks.Key(tasks[0]));
    }

    private static void Write(SqliteConnection connection, string key, string? value)
    {
        using var command = connection.CreateCommand();
        if (value is not null)
        {
            command.CommandText = "INSERT INTO project_meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
        }
        else
        {
            command.CommandText = "DELETE FROM project_meta WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
        }

        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<TaskKind> ReadTargetTasks(SqliteConnection connection)
    {
        if (ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'target_tasks';") is string stored)
            return TargetTasks.ParseSet(stored);

        return TargetTasks.Parse(ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'target_task';") as string) is { } single
            ? new[] { single }
            : Array.Empty<TaskKind>();
    }

    private static ProjectSummary Open(string folder)
    {
        var layout = new ProjectFolderLayout(folder);
        if (!File.Exists(layout.DatabasePath))
            throw new ProjectException($"Thư mục \"{layout.RootPath}\" không phải project: thiếu {ProjectFolderLayout.DatabaseFileName}.");

        var db = new ProjectDb(layout.DatabasePath);
        using var connection = db.Open();
        if (!db.TryMigrate(connection))
            throw new ProjectException("Project này được tạo bằng phiên bản NTech Data Studio mới hơn. Hãy cập nhật ứng dụng.");

        layout.EnsureDirectories();
        return ReadSummary(connection, layout);
    }

    private static ProjectSummary ReadSummary(SqliteConnection connection, ProjectFolderLayout layout)
    {
        var name = ProjectDb.Scalar(connection, "SELECT value FROM project_meta WHERE key = 'name';") as string;
        return new ProjectSummary(
            string.IsNullOrEmpty(name) ? Path.GetFileName(layout.RootPath) : name,
            layout.RootPath,
            ProjectDb.Count(connection, "SELECT COUNT(*) FROM active_images;"),
            ProjectDb.Count(connection, "SELECT COUNT(*) FROM active_images WHERE status IN ('labeled', 'reviewed', 'null');"),
            ProjectDb.Count(connection, "SELECT COUNT(*) FROM classes;"),
            ProjectDb.Count(connection, "SELECT COUNT(*) FROM versions;"),
            ReadTargetTasks(connection));
    }

    private static ProjectSummary Guarded(Func<ProjectSummary> action, string folder)
    {
        try
        {
            return action();
        }
        catch (SqliteException exception)
        {
            throw new ProjectException($"Không đọc được dữ liệu project tại \"{folder}\": {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ProjectException($"Không truy cập được thư mục \"{folder}\": {exception.Message}", exception);
        }
    }
}
