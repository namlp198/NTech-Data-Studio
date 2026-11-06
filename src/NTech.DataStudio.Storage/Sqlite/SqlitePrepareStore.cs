using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Storage.Files;

namespace NTech.DataStudio.Storage.Sqlite;

/// <summary>One connection for one Prepare operation. A SqliteConnection is not thread-safe, so every call takes the lock.</summary>
internal sealed class SqlitePrepareStore : IPrepareStore
{
    private const string NormalizeConfigKey = "normalize_config";

    private static readonly Dictionary<RejectReason, string> ReasonNames = new()
    {
        [RejectReason.Corrupt] = "corrupt",
        [RejectReason.ExactDuplicate] = "exact_duplicate",
        [RejectReason.NearDuplicate] = "near_duplicate",
        [RejectReason.Blurry] = "blurry",
        [RejectReason.TooDark] = "too_dark",
        [RejectReason.TooBright] = "too_bright"
    };

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private readonly ProjectFolderLayout _layout;

    public SqlitePrepareStore(ProjectFolderLayout layout)
    {
        _layout = layout;
        _connection = new ProjectDb(layout.DatabasePath).Open();
    }

    public string ProjectRoot => _layout.RootPath;
    public string SourceFolder => _layout.SourcePath;
    public string MasterFolder => _layout.MasterPath;
    public string ThumbsFolder => _layout.ThumbsPath;
    public string RejectedFolder => _layout.RejectedPath;

    public IReadOnlyList<SourceImage> LoadSources()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, original_path, stored_path, is_copied, file_size, modified_ticks, format,
                       width, height, bit_depth, channels, group_key, sha256, phash, dhash,
                       blur_score, dark_fraction, bright_fraction, reject_reason, reject_detail, keep_override
                FROM source_images ORDER BY id;
                """;
            using var reader = command.ExecuteReader();
            var sources = new List<SourceImage>();
            while (reader.Read())
            {
                sources.Add(new SourceImage(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) != 0,
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.IsDBNull(13) ? null : unchecked((ulong)reader.GetInt64(13)),
                    reader.IsDBNull(14) ? null : unchecked((ulong)reader.GetInt64(14)),
                    reader.IsDBNull(15) ? null : reader.GetDouble(15),
                    reader.IsDBNull(16) ? null : reader.GetDouble(16),
                    reader.IsDBNull(17) ? null : reader.GetDouble(17),
                    reader.IsDBNull(18) ? null : ParseReason(reader.GetString(18)),
                    reader.IsDBNull(19) ? null : reader.GetString(19),
                    reader.GetInt64(20) != 0));
            }

            return sources;
        }
    }

    public long AddSource(SourceImage source)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO source_images
                    (original_path, stored_path, is_copied, file_size, modified_ticks, format,
                     width, height, bit_depth, channels, group_key)
                VALUES ($original, $stored, $copied, $size, $ticks, $format, $width, $height, $depth, $channels, $group);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$original", source.OriginalPath);
            command.Parameters.AddWithValue("$stored", source.StoredPath);
            command.Parameters.AddWithValue("$copied", source.IsCopied ? 1 : 0);
            command.Parameters.AddWithValue("$size", source.FileSize);
            command.Parameters.AddWithValue("$ticks", source.ModifiedTicks);
            command.Parameters.AddWithValue("$format", source.Format);
            command.Parameters.AddWithValue("$width", source.Width);
            command.Parameters.AddWithValue("$height", source.Height);
            command.Parameters.AddWithValue("$depth", source.BitDepth);
            command.Parameters.AddWithValue("$channels", source.Channels);
            command.Parameters.AddWithValue("$group", (object?)source.GroupKey ?? DBNull.Value);
            return (long)command.ExecuteScalar()!;
        }
    }

    public void SaveAnalysis(long sourceId, ImageAnalysis analysis)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE source_images
                SET sha256 = $sha, phash = $phash, dhash = $dhash, blur_score = $blur,
                    dark_fraction = $dark, bright_fraction = $bright
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", sourceId);
            command.Parameters.AddWithValue("$sha", analysis.Sha256);
            command.Parameters.AddWithValue("$phash", unchecked((long)analysis.PHash));
            command.Parameters.AddWithValue("$dhash", unchecked((long)analysis.DHash));
            command.Parameters.AddWithValue("$blur", analysis.BlurScore);
            command.Parameters.AddWithValue("$dark", analysis.DarkFraction);
            command.Parameters.AddWithValue("$bright", analysis.BrightFraction);
            command.ExecuteNonQuery();
        }
    }

    public void MarkCorrupt(long sourceId, string detail)
    {
        lock (_gate)
        {
            SetReject(sourceId, RejectReason.Corrupt, detail, null);
        }
    }

    public void ReplaceRejections(IReadOnlyList<CleanDecision> decisions)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            ProjectDb.Execute(_connection, """
                UPDATE source_images SET reject_reason = NULL, reject_detail = NULL
                WHERE keep_override = 0 AND (reject_reason IS NULL OR reject_reason <> 'corrupt');
                """, transaction);
            foreach (var decision in decisions)
                SetReject(decision.SourceId, decision.Reason, decision.Detail, transaction);
            transaction.Commit();
        }
    }

    public void Restore(long sourceId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                UPDATE source_images SET reject_reason = NULL, reject_detail = NULL, keep_override = 1
                WHERE id = $id AND (reject_reason IS NULL OR reject_reason <> 'corrupt');
                """;
            command.Parameters.AddWithValue("$id", sourceId);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlySet<long> SourcesWithMasters()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT source_image_id FROM images;";
            using var reader = command.ExecuteReader();
            var ids = new HashSet<long>();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }
    }

    public void AddMasters(long sourceId, IReadOnlyList<MasterFile> masters)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            foreach (var master in masters)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO images
                        (source_image_id, master_path, thumb_path, width, height, origin_x, origin_y, scale, file_size)
                    VALUES ($source, $master, $thumb, $width, $height, $x, $y, $scale, $size);
                    """;
                command.Parameters.AddWithValue("$source", sourceId);
                command.Parameters.AddWithValue("$master", Relative(_layout.MasterPath, master.FileName));
                command.Parameters.AddWithValue("$thumb", Relative(_layout.ThumbsPath, master.ThumbnailFileName));
                command.Parameters.AddWithValue("$width", master.Width);
                command.Parameters.AddWithValue("$height", master.Height);
                command.Parameters.AddWithValue("$x", master.OriginX);
                command.Parameters.AddWithValue("$y", master.OriginY);
                command.Parameters.AddWithValue("$scale", master.Scale);
                command.Parameters.AddWithValue("$size", master.FileSize);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public int CountMasters()
    {
        lock (_gate)
        {
            return ProjectDb.Count(_connection, "SELECT COUNT(*) FROM images;");
        }
    }

    public long SumMasterBytes()
    {
        lock (_gate)
        {
            return Convert.ToInt64(ProjectDb.Scalar(_connection, "SELECT COALESCE(SUM(file_size), 0) FROM images;"));
        }
    }

    public int CountAnnotations()
    {
        lock (_gate)
        {
            return ProjectDb.Count(_connection, """
                SELECT (SELECT COUNT(*) FROM annotations) + (SELECT COUNT(*) FROM image_tags);
                """);
        }
    }

    public string? ReadNormalizeConfig()
    {
        lock (_gate)
        {
            return ProjectDb.Scalar(_connection, $"SELECT value FROM project_meta WHERE key = '{NormalizeConfigKey}';") as string;
        }
    }

    public void WriteNormalizeConfig(string config)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO project_meta (key, value) VALUES ($key, $value);";
            command.Parameters.AddWithValue("$key", NormalizeConfigKey);
            command.Parameters.AddWithValue("$value", config);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<string> ClearMasters()
    {
        lock (_gate)
        {
            var paths = new List<string>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT master_path, thumb_path FROM images;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    paths.Add(reader.GetString(0));
                    paths.Add(reader.GetString(1));
                }
            }

            ProjectDb.Execute(_connection, "DELETE FROM images;");
            return paths;
        }
    }

    public void Dispose() => _connection.Dispose();

    private void SetReject(long sourceId, RejectReason reason, string detail, SqliteTransaction? transaction)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE source_images SET reject_reason = $reason, reject_detail = $detail WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sourceId);
        command.Parameters.AddWithValue("$reason", ReasonNames[reason]);
        command.Parameters.AddWithValue("$detail", detail);
        command.ExecuteNonQuery();
    }

    private string Relative(string folder, string fileName) =>
        Path.GetRelativePath(_layout.RootPath, Path.Combine(folder, fileName));

    private static RejectReason ParseReason(string name) =>
        ReasonNames.First(pair => pair.Value == name).Key;
}
