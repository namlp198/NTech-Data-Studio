using Microsoft.Data.Sqlite;
using NTech.DataStudio.Storage.Sqlite.Migrations;

namespace NTech.DataStudio.Storage.Sqlite;

/// <summary>
/// Connections to one project.db. Pooling is off so that closing the last
/// connection checkpoints the WAL and releases the file: the project folder
/// stays self-contained and can be copied right after the app closes it.
/// </summary>
internal sealed class ProjectDb
{
    private readonly string _connectionString;

    public ProjectDb(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public int LatestSchemaVersion => ProjectMigrations.Scripts.Count;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            // WAL with FULL sync: a committed label survives a power cut.
            Execute(connection, "PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL;");
            return connection;
        }
        catch
        {
            // A file that is not a database fails here; do not leave it locked.
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Applies the missing scripts. Returns false when the file is newer than this app understands.</summary>
    public bool TryMigrate(SqliteConnection connection)
    {
        var current = ReadSchemaVersion(connection);
        if (current > LatestSchemaVersion) return false;

        for (var version = current; version < LatestSchemaVersion; version++)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, ProjectMigrations.Scripts[version], transaction);
            Execute(connection, $"PRAGMA user_version = {version + 1};", transaction);
            transaction.Commit();
        }

        return true;
    }

    public static int ReadSchemaVersion(SqliteConnection connection) =>
        Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"));

    public static int Count(SqliteConnection connection, string sql) =>
        Convert.ToInt32(Scalar(connection, sql));

    public static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
