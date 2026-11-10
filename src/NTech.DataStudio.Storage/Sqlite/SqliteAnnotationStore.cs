using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Storage.Files;

namespace NTech.DataStudio.Storage.Sqlite;

/// <summary>One connection for the whole time the annotation screen is open. A SqliteConnection is not thread-safe, so every call takes the lock.</summary>
internal sealed class SqliteAnnotationStore : IAnnotationStore
{
    private static readonly Dictionary<ShapeKind, string> KindNames = new()
    {
        [ShapeKind.Box] = "box",
        [ShapeKind.Polygon] = "polygon",
        [ShapeKind.RotatedBox] = "obb",
        [ShapeKind.Point] = "point"
    };

    private static readonly Dictionary<ImageStatus, string> StatusNames = new()
    {
        [ImageStatus.New] = "new",
        [ImageStatus.Labeled] = "labeled",
        [ImageStatus.Reviewed] = "reviewed",
        [ImageStatus.Rejected] = "rejected",
        [ImageStatus.Null] = "null"
    };

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;
    private readonly ProjectFolderLayout _layout;

    public SqliteAnnotationStore(ProjectFolderLayout layout)
    {
        _layout = layout;
        _connection = new ProjectDb(layout.DatabasePath).Open();
    }

    public string ProjectRoot => _layout.RootPath;

    public IReadOnlyList<AnnotateImage> LoadImages()
    {
        lock (_gate)
        {
            var classesByImage = new Dictionary<long, HashSet<long>>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT image_id, class_id FROM annotations
                    UNION
                    SELECT image_id, class_id FROM image_tags;
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var imageId = reader.GetInt64(0);
                    if (!classesByImage.TryGetValue(imageId, out var set))
                        classesByImage[imageId] = set = new HashSet<long>();
                    set.Add(reader.GetInt64(1));
                }
            }

            using var query = _connection.CreateCommand();
            query.CommandText = """
                SELECT i.id, i.master_path, i.thumb_path, i.width, i.height, i.status,
                       (SELECT COUNT(*) FROM annotations a WHERE a.image_id = i.id)
                     + (SELECT COUNT(*) FROM image_tags t WHERE t.image_id = i.id),
                       i.in_dataset,
                       EXISTS (SELECT 1 FROM annotations a WHERE a.image_id = i.id AND a.source = 'model')
                FROM active_images i ORDER BY i.id;
                """;
            using var rows = query.ExecuteReader();
            var images = new List<AnnotateImage>();
            while (rows.Read())
            {
                var id = rows.GetInt64(0);
                IReadOnlySet<long> classes = classesByImage.TryGetValue(id, out var set) ? set : new HashSet<long>();
                images.Add(new AnnotateImage(
                    id,
                    rows.GetString(1),
                    rows.GetString(2),
                    rows.GetInt32(3),
                    rows.GetInt32(4),
                    ParseStatus(rows.GetString(5)),
                    rows.GetInt32(6),
                    classes,
                    rows.GetInt32(7) != 0,
                    rows.GetInt32(8) != 0));
            }

            return images;
        }
    }

    public bool LoadSelectionOnly()
    {
        lock (_gate)
            return ProjectDb.Scalar(_connection, "SELECT value FROM project_meta WHERE key = 'dataset_selection_only';") as string == "1";
    }

    public void SetSelectionOnly(bool value)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO project_meta (key, value) VALUES ('dataset_selection_only', $value)
                ON CONFLICT (key) DO UPDATE SET value = excluded.value;
                """;
            command.Parameters.AddWithValue("$value", value ? "1" : "0");
            command.ExecuteNonQuery();
        }
    }

    public void SetInDataset(long imageId, bool value)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE images SET in_dataset = $value WHERE id = $id;";
            command.Parameters.AddWithValue("$value", value ? 1 : 0);
            command.Parameters.AddWithValue("$id", imageId);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("Ảnh không còn trong project.");
        }
    }

    public ImageContent LoadContent(long imageId)
    {
        lock (_gate)
        {
            var annotations = new List<Annotation>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT id, class_id, kind, points_json, source, created_at, updated_at
                    FROM annotations WHERE image_id = $image ORDER BY rowid;
                    """;
                command.Parameters.AddWithValue("$image", imageId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    annotations.Add(ReadAnnotation(reader, imageId));
            }

            var tags = new HashSet<long>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT class_id FROM image_tags WHERE image_id = $image;";
                command.Parameters.AddWithValue("$image", imageId);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    tags.Add(reader.GetInt64(0));
            }

            return new ImageContent(annotations, tags);
        }
    }

    public void Apply(StoreChange change)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();

            foreach (var id in change.Deletes)
                Execute(transaction, "DELETE FROM annotations WHERE id = $id;", ("$id", id.ToString("D")));

            foreach (var annotation in change.Upserts)
            {
                Execute(
                    transaction,
                    """
                    INSERT INTO annotations (id, image_id, class_id, kind, points_json, source, created_at, updated_at)
                    VALUES ($id, $image, $class, $kind, $points, $source, $created, $updated)
                    ON CONFLICT(id) DO UPDATE SET
                        class_id = excluded.class_id, kind = excluded.kind,
                        points_json = excluded.points_json, updated_at = excluded.updated_at;
                    """,
                    ("$id", annotation.Id.ToString("D")),
                    ("$image", change.ImageId),
                    ("$class", annotation.ClassId),
                    ("$kind", KindNames[annotation.Kind]),
                    ("$points", SerializePoints(annotation.Points)),
                    ("$source", annotation.Source == AnnotationSource.Model ? "model" : "manual"),
                    ("$created", annotation.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
                    ("$updated", annotation.UpdatedAt.ToString("O", CultureInfo.InvariantCulture)));
            }

            foreach (var classId in change.TagsRemoved)
                Execute(transaction, "DELETE FROM image_tags WHERE image_id = $image AND class_id = $class;",
                    ("$image", change.ImageId), ("$class", classId));
            foreach (var classId in change.TagsAdded)
                Execute(transaction, "INSERT OR IGNORE INTO image_tags (image_id, class_id) VALUES ($image, $class);",
                    ("$image", change.ImageId), ("$class", classId));

            var updated = Execute(
                transaction, "UPDATE images SET status = $status WHERE id = $image;",
                ("$status", StatusNames[change.Status]), ("$image", change.ImageId));
            if (updated == 0)
                throw new InvalidOperationException($"Ảnh {change.ImageId} không còn trong project.");

            transaction.Commit();
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

    public LabelClass AddClass(string name, string color, int? hotkey)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO classes (name, color, hotkey, sort_order)
                VALUES ($name, $color, $hotkey, (SELECT COALESCE(MAX(sort_order) + 1, 0) FROM classes));
                SELECT id, sort_order FROM classes WHERE id = last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$color", color);
            command.Parameters.AddWithValue("$hotkey", (object?)hotkey ?? DBNull.Value);
            using var reader = command.ExecuteReader();
            reader.Read();
            return new LabelClass(reader.GetInt64(0), name, color, hotkey, reader.GetInt32(1));
        }
    }

    public void UpdateClass(LabelClass labelClass)
    {
        lock (_gate)
        {
            var updated = Execute(
                null,
                "UPDATE classes SET name = $name, color = $color, hotkey = $hotkey, sort_order = $order WHERE id = $id;",
                ("$id", labelClass.Id),
                ("$name", labelClass.Name),
                ("$color", labelClass.Color),
                ("$hotkey", (object?)labelClass.Hotkey ?? DBNull.Value),
                ("$order", labelClass.Order));
            if (updated == 0)
                throw new InvalidOperationException("Class này không còn tồn tại.");
        }
    }

    public ClassUsage GetClassUsage(long classId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM annotations WHERE class_id = $class)
                     + (SELECT COUNT(*) FROM image_tags WHERE class_id = $class),
                       (SELECT COUNT(*) FROM (
                            SELECT image_id FROM annotations WHERE class_id = $class
                            UNION
                            SELECT image_id FROM image_tags WHERE class_id = $class));
                """;
            command.Parameters.AddWithValue("$class", classId);
            using var reader = command.ExecuteReader();
            reader.Read();
            return new ClassUsage(reader.GetInt32(0), reader.GetInt32(1));
        }
    }

    public IReadOnlyDictionary<long, int> LoadClassCounts()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT class_id, SUM(total) FROM (
                    SELECT class_id, COUNT(*) AS total FROM annotations GROUP BY class_id
                    UNION ALL
                    SELECT class_id, COUNT(*) AS total FROM image_tags GROUP BY class_id
                ) GROUP BY class_id;
                """;
            using var reader = command.ExecuteReader();
            var counts = new Dictionary<long, int>();
            while (reader.Read())
                counts[reader.GetInt64(0)] = reader.GetInt32(1);
            return counts;
        }
    }

    public void MergeClasses(long fromId, long intoId)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            Execute(transaction, "UPDATE annotations SET class_id = $into WHERE class_id = $from;", ("$into", intoId), ("$from", fromId));
            // The primary key is (image, class): an image already tagged with both keeps one tag.
            Execute(transaction, "INSERT OR IGNORE INTO image_tags (image_id, class_id) SELECT image_id, $into FROM image_tags WHERE class_id = $from;", ("$into", intoId), ("$from", fromId));
            Execute(transaction, "DELETE FROM image_tags WHERE class_id = $from;", ("$from", fromId));
            Execute(transaction, "DELETE FROM classes WHERE id = $from;", ("$from", fromId));
            transaction.Commit();
        }
    }

    public void DeleteClass(long classId)
    {
        lock (_gate)
        {
            using var transaction = _connection.BeginTransaction();
            Execute(transaction, """
                CREATE TEMP TABLE IF NOT EXISTS affected_images (id INTEGER PRIMARY KEY);
                DELETE FROM affected_images;
                """);
            Execute(transaction, """
                INSERT OR IGNORE INTO affected_images
                SELECT image_id FROM annotations WHERE class_id = $class
                UNION SELECT image_id FROM image_tags WHERE class_id = $class;
                """, ("$class", classId));
            Execute(transaction, "DELETE FROM annotations WHERE class_id = $class;", ("$class", classId));
            Execute(transaction, "DELETE FROM image_tags WHERE class_id = $class;", ("$class", classId));
            Execute(transaction, "DELETE FROM classes WHERE id = $class;", ("$class", classId));
            // Images whose last label just went away are not labeled any more.
            Execute(transaction, """
                UPDATE images SET status = 'new'
                WHERE id IN (SELECT id FROM affected_images)
                  AND status IN ('labeled', 'reviewed')
                  AND NOT EXISTS (SELECT 1 FROM annotations WHERE image_id = images.id)
                  AND NOT EXISTS (SELECT 1 FROM image_tags WHERE image_id = images.id);
                DROP TABLE affected_images;
                """);
            transaction.Commit();
        }
    }

    public void Dispose() => _connection.Dispose();

    private int Execute(SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteNonQuery();
    }

    private static Annotation ReadAnnotation(SqliteDataReader reader, long imageId)
    {
        var id = Guid.Parse(reader.GetString(0));
        var kind = KindNames.First(pair => pair.Value == reader.GetString(2)).Key;
        try
        {
            return new Annotation(
                id,
                imageId,
                reader.GetInt64(1),
                kind,
                DeserializePoints(reader.GetString(3)),
                reader.GetString(4) == "model" ? AnnotationSource.Model : AnnotationSource.Manual,
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            throw new InvalidDataException($"Nhãn {id} trong project bị hỏng: {exception.Message}", exception);
        }
    }

    private static string SerializePoints(IReadOnlyList<PixelPoint> points) =>
        JsonSerializer.Serialize(points.Select(point => new[] { point.X, point.Y }));

    private static IReadOnlyList<PixelPoint> DeserializePoints(string json)
    {
        var pairs = JsonSerializer.Deserialize<double[][]>(json) ?? throw new JsonException("Danh sách điểm rỗng.");
        return pairs.Select(pair => pair.Length == 2
                ? new PixelPoint(pair[0], pair[1])
                : throw new JsonException("Mỗi điểm phải có 2 số.")).ToList();
    }

    private static ImageStatus ParseStatus(string name) => StatusNames.First(pair => pair.Value == name).Key;
}
