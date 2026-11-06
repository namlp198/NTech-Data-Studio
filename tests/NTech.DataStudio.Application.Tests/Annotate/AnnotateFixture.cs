using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Application.Tests.Annotate;

/// <summary>A real project folder with a few masters already in it, without running Prepare.</summary>
internal sealed class AnnotateFixture : IDisposable
{
    public const int Width = 200;
    public const int Height = 100;

    private readonly TempFolder _temp = new();
    private readonly SqliteProjectStore _projects = new();
    private readonly List<IDisposable> _open = new();

    public AnnotateFixture(int imageCount = 3)
    {
        Root = Path.Combine(_temp.Path, "Dự án");
        _projects.CreateAsync(Root, "Dự án").GetAwaiter().GetResult();

        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False;Foreign Keys=True");
        connection.Open();
        Run(connection, """
            INSERT INTO source_images (id, original_path, stored_path, is_copied, file_size, modified_ticks, format, width, height, bit_depth, channels)
            VALUES (1, 'a', 'a', 0, 1, 1, 'PNG', 400, 200, 8, 3);
            """);
        for (var index = 1; index <= imageCount; index++)
        {
            Run(connection, $"""
                INSERT INTO images (id, source_image_id, master_path, thumb_path, width, height, origin_x, origin_y, scale, file_size)
                VALUES ({index}, 1, 'master/m{index}.jpg', 'thumbs/m{index}.jpg', {Width}, {Height}, 0, 0, 0.5, 1);
                """);
        }
    }

    public string Root { get; }

    public string DatabasePath => Path.Combine(Root, "project.db");

    public IAnnotationStore OpenStore()
    {
        var store = _projects.OpenAnnotationStore(Root);
        _open.Add(store);
        return store;
    }

    /// <summary>A service with a fixed clock, so timestamps are predictable.</summary>
    public AnnotateService Service(IAnnotationStore store) =>
        new(store, () => new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    public ImageDocument OpenImage(AnnotateService service, int imageId) =>
        service.Open(service.LoadImages().Single(image => image.Id == imageId));

    public long Scalar(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public string Text(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        foreach (var item in _open) item.Dispose();
        _temp.Dispose();
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
