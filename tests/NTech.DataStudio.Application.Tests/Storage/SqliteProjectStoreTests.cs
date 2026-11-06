using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Application.Tests.Storage;

public class SqliteProjectStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly SqliteProjectStore _store = new();

    public void Dispose() => _temp.Dispose();

    private string ProjectPath => Path.Combine(_temp.Path, "p1");
    private string DatabasePath => Path.Combine(ProjectPath, "project.db");

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False;Foreign Keys=True");
        connection.Open();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task Create_MakesTheDocumentedFolderLayout()
    {
        await _store.CreateAsync(ProjectPath, "p1");

        Assert.True(File.Exists(DatabasePath));
        foreach (var folder in new[] { "source", "master", "thumbs", "rejected", "versions", "exports" })
            Assert.True(Directory.Exists(Path.Combine(ProjectPath, folder)), folder);
    }

    [Fact]
    public async Task Create_MakesEveryTable()
    {
        await _store.CreateAsync(ProjectPath, "p1");

        using var connection = Connect();
        foreach (var table in new[] { "project_meta", "source_images", "images", "classes", "annotations", "image_tags", "versions", "version_items" })
            Assert.Equal(1L, Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));
    }

    [Fact]
    public async Task Create_RefusesAFolderWithData()
    {
        Directory.CreateDirectory(ProjectPath);
        var existing = Path.Combine(ProjectPath, "keep.txt");
        await File.WriteAllTextAsync(existing, "x");

        await Assert.ThrowsAsync<ProjectException>(() => _store.CreateAsync(ProjectPath, "p1"));

        Assert.True(File.Exists(existing));
        Assert.False(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Create_AcceptsAnEmptyExistingFolder()
    {
        Directory.CreateDirectory(ProjectPath);

        var summary = await _store.CreateAsync(ProjectPath, "p1");

        Assert.Equal("p1", summary.Name);
    }

    [Fact]
    public async Task Open_ReturnsTheNameAndCounts()
    {
        await _store.CreateAsync(ProjectPath, "Bộ dây điện");
        using (var connection = Connect())
        {
            Execute(connection, """
                INSERT INTO classes (name, color, sort_order) VALUES ('connector', '#4A90E2', 0);
                INSERT INTO source_images (id, original_path, stored_path, is_copied, file_size, modified_ticks, format, width, height, bit_depth, channels)
                    VALUES (1, 'a', 'a', 0, 1, 1, 'PNG', 1, 1, 8, 3), (2, 'b', 'b', 0, 1, 1, 'PNG', 1, 1, 8, 3);
                UPDATE source_images SET reject_reason = 'blurry' WHERE id = 2;
                INSERT INTO images (source_image_id, master_path, thumb_path, width, height, origin_x, origin_y, scale, file_size, status) VALUES
                    (1, 'a', 'ta', 1, 1, 0, 0, 1, 1, 'new'),
                    (1, 'b', 'tb', 1, 1, 0, 0, 1, 1, 'labeled'),
                    (1, 'c', 'tc', 1, 1, 0, 0, 1, 1, 'reviewed'),
                    (1, 'd', 'td', 1, 1, 0, 0, 1, 1, 'null'),
                    (1, 'e', 'te', 1, 1, 0, 0, 1, 1, 'rejected'),
                    (2, 'f', 'tf', 1, 1, 0, 0, 1, 1, 'labeled');
                INSERT INTO versions (name, prep_json, split_json, augment_json, seed, created_at)
                    VALUES ('v001', '{}', '{}', '{}', 42, '2026-11-06');
                """);
        }

        var summary = await _store.OpenAsync(ProjectPath);

        Assert.Equal(new ProjectSummary("Bộ dây điện", Path.GetFullPath(ProjectPath), 4, 3, 1, 1), summary);
    }

    [Fact]
    public async Task Open_FolderCopiedElsewhereStillWorks()
    {
        await _store.CreateAsync(ProjectPath, "p1");
        var moved = Path.Combine(_temp.Path, "moved");
        Directory.Move(ProjectPath, moved);

        var summary = await _store.OpenAsync(moved);

        Assert.Equal("p1", summary.Name);
        Assert.Equal(Path.GetFullPath(moved), summary.RootPath);
    }

    [Fact]
    public async Task Close_LeavesNoSidecarFilesSoTheFolderCanBeCopied()
    {
        await _store.CreateAsync(ProjectPath, "p1");

        Assert.False(File.Exists(DatabasePath + "-wal"));
        Assert.False(File.Exists(DatabasePath + "-shm"));
    }

    [Fact]
    public async Task Open_RestoresFoldersLostInACopy()
    {
        await _store.CreateAsync(ProjectPath, "p1");
        Directory.Delete(Path.Combine(ProjectPath, "thumbs"));

        await _store.OpenAsync(ProjectPath);

        Assert.True(Directory.Exists(Path.Combine(ProjectPath, "thumbs")));
    }

    [Fact]
    public async Task Open_RefusesAFolderWithoutADatabase()
    {
        Directory.CreateDirectory(ProjectPath);

        await Assert.ThrowsAsync<ProjectException>(() => _store.OpenAsync(ProjectPath));

        Assert.Empty(Directory.EnumerateFileSystemEntries(ProjectPath));
    }

    [Fact]
    public async Task Open_ReportsAFileThatIsNotADatabase()
    {
        Directory.CreateDirectory(ProjectPath);
        await File.WriteAllTextAsync(DatabasePath, new string('x', 4096));

        await Assert.ThrowsAsync<ProjectException>(() => _store.OpenAsync(ProjectPath));
    }

    [Fact]
    public async Task Open_RefusesANewerSchemaAndLeavesItUntouched()
    {
        await _store.CreateAsync(ProjectPath, "p1");
        using (var connection = Connect())
            Execute(connection, "PRAGMA user_version = 999;");

        await Assert.ThrowsAsync<ProjectException>(() => _store.OpenAsync(ProjectPath));

        using var check = Connect();
        Assert.Equal(999L, Scalar(check, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task Open_TwiceDoesNotReapplyMigrations()
    {
        await _store.CreateAsync(ProjectPath, "p1");

        await _store.OpenAsync(ProjectPath);
        await _store.OpenAsync(ProjectPath);

        using var connection = Connect();
        Assert.Equal(1L, Scalar(connection, "PRAGMA user_version;"));
    }

    [Fact]
    public async Task Schema_DeletingAnImageRemovesItsLabelsButAClassInUseIsProtected()
    {
        await _store.CreateAsync(ProjectPath, "p1");
        using var connection = Connect();
        Execute(connection, """
            INSERT INTO classes (id, name, color, sort_order) VALUES (1, 'connector', '#4A90E2', 0);
            INSERT INTO source_images (id, original_path, stored_path, is_copied, file_size, modified_ticks, format, width, height, bit_depth, channels)
                VALUES (1, 'a', 'a', 0, 1, 1, 'PNG', 1, 1, 8, 3);
            INSERT INTO images (id, source_image_id, master_path, thumb_path, width, height, origin_x, origin_y, scale, file_size)
                VALUES (1, 1, 'a', 'ta', 1, 1, 0, 0, 1, 1);
            INSERT INTO annotations (id, image_id, class_id, kind, points_json, source, created_at, updated_at)
                VALUES ('x', 1, 1, 'box', '[]', 'manual', 't', 't');
            """);

        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM classes WHERE id = 1;"));

        Execute(connection, "DELETE FROM images WHERE id = 1;");
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM annotations;"));
    }
}
