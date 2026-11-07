using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Storage.Files;

namespace NTech.DataStudio.Storage.Sqlite;

/// <summary>One connection for one dataset operation. A SqliteConnection is not thread-safe, so every call takes the lock.</summary>
internal sealed class SqliteDatasetStore : IDatasetStore
{
    private const int ConstraintViolation = 19;

    private static readonly Dictionary<string, ShapeKind> KindNames = new()
    {
        ["box"] = ShapeKind.Box,
        ["polygon"] = ShapeKind.Polygon,
        ["obb"] = ShapeKind.RotatedBox,
        ["point"] = ShapeKind.Point
    };

    private static readonly Dictionary<string, ImageStatus> StatusNames = new()
    {
        ["new"] = ImageStatus.New,
        ["labeled"] = ImageStatus.Labeled,
        ["reviewed"] = ImageStatus.Reviewed,
        ["rejected"] = ImageStatus.Rejected,
        ["null"] = ImageStatus.Null
    };

    private static readonly Dictionary<SplitKind, string> SplitNames = new()
    {
        [SplitKind.Train] = "train",
        [SplitKind.Val] = "val",
        [SplitKind.Test] = "test"
    };

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private readonly ProjectFolderLayout _layout;

    public SqliteDatasetStore(ProjectFolderLayout layout)
    {
        _layout = layout;
        _connection = new ProjectDb(layout.DatabasePath).Open();
    }

    public string ProjectRoot => _layout.RootPath;

    public IReadOnlyList<DatasetImage> LoadImages()
    {
        lock (_gate)
        {
            var labels = new Dictionary<long, Dictionary<long, int>>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT image_id, class_id, COUNT(*) FROM annotations GROUP BY image_id, class_id;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var imageId = reader.GetInt64(0);
                    if (!labels.TryGetValue(imageId, out var byClass))
                        labels[imageId] = byClass = new Dictionary<long, int>();
                    byClass[reader.GetInt64(1)] = reader.GetInt32(2);
                }
            }

            var tags = new Dictionary<long, HashSet<long>>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT image_id, class_id FROM image_tags;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var imageId = reader.GetInt64(0);
                    if (!tags.TryGetValue(imageId, out var set))
                        tags[imageId] = set = new HashSet<long>();
                    set.Add(reader.GetInt64(1));
                }
            }

            using var query = _connection.CreateCommand();
            query.CommandText = """
                SELECT i.id, i.source_image_id, s.group_key, i.master_path, i.width, i.height, i.status
                FROM active_images i JOIN source_images s ON s.id = i.source_image_id
                ORDER BY i.id;
                """;
            using var rows = query.ExecuteReader();
            var images = new List<DatasetImage>();
            while (rows.Read())
            {
                var id = rows.GetInt64(0);
                IReadOnlyDictionary<long, int> byClass = labels.TryGetValue(id, out var counts)
                    ? counts
                    : new Dictionary<long, int>();
                IReadOnlySet<long> tagSet = tags.TryGetValue(id, out var set) ? set : new HashSet<long>();
                images.Add(new DatasetImage(
                    id,
                    rows.GetInt64(1),
                    rows.IsDBNull(2) ? null : rows.GetString(2),
                    rows.GetString(3),
                    rows.GetInt32(4),
                    rows.GetInt32(5),
                    StatusNames[rows.GetString(6)],
                    byClass,
                    tagSet));
            }

            return images;
        }
    }

    public IReadOnlyList<LabelClass> LoadClasses()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id, name, color, hotkey, sort_order FROM classes ORDER BY sort_order, id;";
            using var reader = command.ExecuteReader();
            var classes = new List<LabelClass>();
            while (reader.Read())
            {
                classes.Add(new LabelClass(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.GetInt32(4)));
            }

            return classes;
        }
    }

    public IReadOnlyList<Annotation> LoadAnnotations(long imageId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT id, class_id, kind, points_json, source, created_at, updated_at
                FROM annotations WHERE image_id = $image ORDER BY rowid;
                """;
            command.Parameters.AddWithValue("$image", imageId);
            using var reader = command.ExecuteReader();
            var annotations = new List<Annotation>();
            while (reader.Read())
            {
                var id = Guid.Parse(reader.GetString(0));
                try
                {
                    var pairs = JsonSerializer.Deserialize<double[][]>(reader.GetString(3))
                                ?? throw new JsonException("Danh sách điểm rỗng.");
                    annotations.Add(new Annotation(
                        id,
                        imageId,
                        reader.GetInt64(1),
                        KindNames[reader.GetString(2)],
                        pairs.Select(pair => pair.Length == 2
                            ? new PixelPoint(pair[0], pair[1])
                            : throw new JsonException("Mỗi điểm phải có 2 số.")).ToList(),
                        reader.GetString(4) == "model" ? AnnotationSource.Model : AnnotationSource.Manual,
                        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)));
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
                {
                    throw new InvalidDataException($"Nhãn {id} trong project bị hỏng: {exception.Message}", exception);
                }
            }

            return annotations;
        }
    }

    public IReadOnlyList<DatasetVersion> LoadVersions()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id, name, prep_json, split_json, augment_json, seed, created_at FROM versions ORDER BY id;";
            using var reader = command.ExecuteReader();
            var versions = new List<DatasetVersion>();
            while (reader.Read())
            {
                versions.Add(new DatasetVersion(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)));
            }

            return versions;
        }
    }

    public IReadOnlyList<SplitAssignment> LoadAssignments(long versionId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT image_id, split FROM version_items WHERE version_id = $version ORDER BY image_id;";
            command.Parameters.AddWithValue("$version", versionId);
            using var reader = command.ExecuteReader();
            var items = new List<SplitAssignment>();
            while (reader.Read())
            {
                var split = SplitNames.First(pair => pair.Value == reader.GetString(1)).Key;
                items.Add(new SplitAssignment(reader.GetInt64(0), split));
            }

            return items;
        }
    }

    public DatasetVersion CreateVersion(string name, DatasetConfig config, IReadOnlyList<SplitAssignment> assignments)
    {
        lock (_gate)
        {
            var createdAt = DateTimeOffset.UtcNow;
            var prep = DatasetConfig.ToJson(config.Preprocess);
            var split = DatasetConfig.ToJson(config.Split);
            var augment = DatasetConfig.ToJson(config.Augment);

            using var transaction = _connection.BeginTransaction();
            try
            {
                long id;
                using (var insert = _connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO versions (name, prep_json, split_json, augment_json, seed, created_at)
                        VALUES ($name, $prep, $split, $augment, $seed, $created);
                        SELECT last_insert_rowid();
                        """;
                    insert.Parameters.AddWithValue("$name", name);
                    insert.Parameters.AddWithValue("$prep", prep);
                    insert.Parameters.AddWithValue("$split", split);
                    insert.Parameters.AddWithValue("$augment", augment);
                    insert.Parameters.AddWithValue("$seed", config.Seed);
                    insert.Parameters.AddWithValue("$created", createdAt.ToString("O", CultureInfo.InvariantCulture));
                    id = (long)insert.ExecuteScalar()!;
                }

                using var item = _connection.CreateCommand();
                item.Transaction = transaction;
                item.CommandText = "INSERT INTO version_items (version_id, image_id, split) VALUES ($version, $image, $split);";
                var versionParameter = item.Parameters.Add("$version", SqliteType.Integer);
                var imageParameter = item.Parameters.Add("$image", SqliteType.Integer);
                var splitParameter = item.Parameters.Add("$split", SqliteType.Text);
                foreach (var assignment in assignments)
                {
                    versionParameter.Value = id;
                    imageParameter.Value = assignment.ImageId;
                    splitParameter.Value = SplitNames[assignment.Split];
                    item.ExecuteNonQuery();
                }

                transaction.Commit();
                return new DatasetVersion(id, name, prep, split, augment, config.Seed, createdAt);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == ConstraintViolation)
            {
                // Dispose of the transaction rolls it back.
                throw new InvalidOperationException(
                    exception.Message.Contains("versions.name", StringComparison.OrdinalIgnoreCase)
                        ? $"Đã có version tên \"{name}\"."
                        : "Một ảnh trong version không còn trong project. Hãy tải lại màn hình Dataset.",
                    exception);
            }
        }
    }

    public void DeleteVersion(long versionId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM versions WHERE id = $id;";
            command.Parameters.AddWithValue("$id", versionId);
            command.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();
}
