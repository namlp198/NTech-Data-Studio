using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Imaging.Codecs;
using NTech.DataStudio.Storage.Sqlite;
using OpenCvSharp;

namespace NTech.DataStudio.Application.Tests.Prepare;

/// <summary>The whole of Module 1 with the real codec and real SQLite, on a folder with planted problems.</summary>
public class PreparePipelineTests : IDisposable
{
    private static readonly NormalizeOptions Options = new(
        new GeometryPlan(GeometryMethod.Downscale, MaxLongEdge: 320), MasterFormat.Jpeg95, MonoMode.KeepOneChannel);

    private readonly TempFolder _temp = new();
    private readonly SqliteProjectStore _projects = new();
    private readonly OpenCvImageCodec _codec = new();
    private readonly string _images;
    private readonly string _root;

    public PreparePipelineTests()
    {
        _images = Path.Combine(_temp.Path, "ảnh thô");
        _root = Path.Combine(_temp.Path, "Dự án");
        _projects.CreateAsync(_root, "Dự án").GetAwaiter().GetResult();
    }

    public void Dispose() => _temp.Dispose();

    private IPrepareStore Store() => _projects.OpenPrepareStore(_root);

    private static Mat Scene(int seed, int width = 640, int height = 480)
    {
        var random = new Random(seed);
        var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(random.Next(80, 170), random.Next(80, 170), random.Next(80, 170)));
        for (var index = 0; index < 40; index++)
        {
            var color = new Scalar(random.Next(256), random.Next(256), random.Next(256));
            var center = new OpenCvSharp.Point(random.Next(width), random.Next(height));
            if (index % 2 == 0)
                Cv2.Circle(image, center, random.Next(5, 80), color, -1);
            else
                Cv2.Rectangle(image, new Rect(center.X, center.Y, random.Next(10, 160), random.Next(10, 80)), color, -1);
        }

        return image;
    }

    private string Write(string relative, Mat image)
    {
        var path = Path.Combine(_images, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Cv2.ImEncode(Path.GetExtension(path), image, out var bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>13 files: 6 good ones, 1 exact copy, 2 near copies, 1 dark, 1 burnt out, 2 corrupt, and a text file to ignore.</summary>
    private void PlantFolder()
    {
        for (var seed = 1; seed <= 6; seed++)
        {
            using var scene = Scene(seed * 100);
            Write($"phiên {seed % 2}/tốt {seed}.bmp", scene);
        }

        using var original = Scene(100);
        Write("trùng hệt/bản sao.bmp", original);

        Cv2.ImEncode(".jpg", original, out var recompressed, new ImageEncodingParam(ImwriteFlags.JpegQuality, 70));
        Directory.CreateDirectory(Path.Combine(_images, "gần trùng"));
        File.WriteAllBytes(Path.Combine(_images, "gần trùng", "nén lại.jpg"), recompressed);

        using var other = Scene(200);
        using var resized = new Mat();
        Cv2.Resize(other, resized, new OpenCvSharp.Size(), 0.8, 0.8, InterpolationFlags.Area);
        using var shifted = new Mat();
        resized.ConvertTo(shifted, -1, 1.0, 10);
        Write("gần trùng/đổi cỡ.png", shifted);

        using var dark = new Mat(480, 640, MatType.CV_8UC3, new Scalar(5, 5, 5));
        Write("xấu/tối.png", dark);

        using var bright = new Mat(480, 640, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Write("xấu/cháy.png", bright);

        var truncated = Path.Combine(_images, "xấu", "cụt.jpg");
        using var broken = Scene(900);
        Cv2.ImEncode(".jpg", broken, out var whole);
        File.WriteAllBytes(truncated, whole.Take(whole.Length / 2).ToArray());

        File.WriteAllText(Path.Combine(_images, "xấu", "giả.png"), new string('x', 500));
        File.WriteAllText(Path.Combine(_images, "ghi chú.txt"), "không phải ảnh");
    }

    private static void RunThrough(IPrepareStore store, ImportService import, AnalyzeService analyze, string folder)
    {
        import.Import(store, folder, ImportMode.ReferenceInPlace);
        analyze.Analyze(store);
        CleanService.Apply(store, new CleanOptions());
    }

    [Fact]
    public void PlantedProblems_AreAllFoundAndGoodImagesAreKept()
    {
        PlantFolder();
        using var store = Store();

        var imported = new ImportService(_codec).Import(store, _images, ImportMode.ReferenceInPlace);
        Assert.Equal(13, imported.Added);
        Assert.Equal(2, imported.Corrupt);

        new AnalyzeService(_codec).Analyze(store);
        var sources = CleanService.Apply(store, new CleanOptions());

        var reasons = sources.Where(source => source.IsRejected).ToDictionary(source => source.FileName, source => source.Reject);
        Assert.Equal(RejectReason.Corrupt, reasons["cụt.jpg"]);
        Assert.Equal(RejectReason.Corrupt, reasons["giả.png"]);
        Assert.Equal(RejectReason.TooDark, reasons["tối.png"]);
        Assert.Equal(RejectReason.TooBright, reasons["cháy.png"]);
        Assert.Equal(RejectReason.NearDuplicate, reasons["đổi cỡ.png"]);
        Assert.Equal(RejectReason.NearDuplicate, reasons["nén lại.jpg"]);

        // Of the two identical files exactly one goes, and it is the later one by import order.
        Assert.Contains(sources, source => source.FileName == "bản sao.bmp" && source.Reject == RejectReason.ExactDuplicate);
        Assert.DoesNotContain(sources, source => source.FileName == "tốt 1.bmp" && source.IsRejected);

        Assert.Equal(6, sources.Count(source => !source.IsRejected));
    }

    [Fact]
    public void Import_SkipsFilesAlreadyImportedAndPicksUpNewOnes()
    {
        PlantFolder();
        using var store = Store();
        var import = new ImportService(_codec);
        import.Import(store, _images, ImportMode.ReferenceInPlace);

        var again = import.Import(store, _images, ImportMode.ReferenceInPlace);
        Assert.Equal((0, 13), (again.Added, again.AlreadyImported));

        using var scene = Scene(777);
        Write("tốt mới.png", scene);
        var third = import.Import(store, _images, ImportMode.ReferenceInPlace);
        Assert.Equal((1, 13), (third.Added, third.AlreadyImported));
    }

    [Fact]
    public void Import_CopyModeLeavesOriginalsAndCopiesNameCollisionsApart()
    {
        using var first = Scene(1);
        using var second = Scene(2);
        Write("a/cùng tên.png", first);
        Write("b/cùng tên.png", second);
        using var store = Store();

        new ImportService(_codec).Import(store, _images, ImportMode.CopyIntoProject);

        var sources = store.LoadSources();
        Assert.All(sources, source =>
        {
            Assert.True(source.IsCopied);
            Assert.False(Path.IsPathRooted(source.StoredPath));
            Assert.True(File.Exists(Path.Combine(_root, source.StoredPath)));
            Assert.True(File.Exists(source.OriginalPath));
        });
        Assert.Equal(2, sources.Select(source => source.StoredPath).Distinct().Count());
        Assert.Equal(new[] { "a", "b" }, sources.Select(source => source.GroupKey));
        Assert.Empty(Directory.GetFiles(store.SourceFolder, "*.partial"));
    }

    [Fact]
    public void Import_IgnoresThePictureFilesOfTheProjectItself()
    {
        using var scene = Scene(1);
        Directory.CreateDirectory(_images);
        Cv2.ImEncode(".png", scene, out var bytes);
        File.WriteAllBytes(Path.Combine(_root, "master", "m.png"), bytes);
        using var store = Store();

        var result = new ImportService(_codec).Import(store, _temp.Path, ImportMode.ReferenceInPlace);

        Assert.Equal(0, result.Added);
    }

    [Fact]
    public void Import_CanBeCancelledAndResumed()
    {
        PlantFolder();
        using var store = Store();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new ImportService(_codec).Import(store, _images, ImportMode.ReferenceInPlace, null, cancellation.Token));

        Assert.Equal(13, new ImportService(_codec).Import(store, _images, ImportMode.ReferenceInPlace).Added);
    }

    [Fact]
    public void Analyze_ResumesWithoutRedoingFinishedImages()
    {
        PlantFolder();
        using var store = Store();
        new ImportService(_codec).Import(store, _images, ImportMode.ReferenceInPlace);
        var analyze = new AnalyzeService(_codec);

        var first = analyze.Analyze(store);
        var second = analyze.Analyze(store);

        Assert.Equal(11, first.Analyzed);
        Assert.Equal((0, 11), (second.Analyzed, second.AlreadyAnalyzed));
    }

    [Fact]
    public void Clean_NeverDeletesOrMovesFilesAndWritesAReport()
    {
        PlantFolder();
        var before = Directory.GetFiles(_images, "*", SearchOption.AllDirectories).Order().ToArray();
        using var store = Store();

        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);

        Assert.Equal(before, Directory.GetFiles(_images, "*", SearchOption.AllDirectories).Order());
        var report = File.ReadAllText(Path.Combine(store.RejectedFolder, CleanService.ReportFileName));
        Assert.Contains("tối.png", report);
        Assert.Contains("TooDark", report);
    }

    [Fact]
    public void Restore_SurvivesAnotherCleanRun()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var dark = store.LoadSources().Single(source => source.FileName == "tối.png");

        CleanService.Restore(store, dark.Id);
        var sources = CleanService.Apply(store, new CleanOptions());

        Assert.False(sources.Single(source => source.Id == dark.Id).IsRejected);
    }

    [Fact]
    public void Restore_CannotBringBackACorruptFile()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var corrupt = store.LoadSources().First(source => source.Reject == RejectReason.Corrupt);

        var sources = CleanService.Restore(store, corrupt.Id);

        Assert.Equal(RejectReason.Corrupt, sources.Single(source => source.Id == corrupt.Id).Reject);
    }

    [Fact]
    public async Task Normalize_MakesMastersForKeptImagesOnlyAndShowsInTheProjectCounts()
    {
        PlantFolder();
        using (var store = Store())
        {
            RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);

            var result = new NormalizeService(_codec).Normalize(store, Options);

            var kept = store.LoadSources().Count(source => !source.IsRejected);
            Assert.Equal(kept, result.Processed);
            Assert.Equal(kept, result.MasterCount);
            Assert.Empty(result.Failures);
            Assert.True(result.MasterBytes < result.SourceBytes / 10, $"{result.MasterBytes} vs {result.SourceBytes}");
            Assert.Equal(kept, Directory.GetFiles(store.MasterFolder).Length);
            Assert.Empty(Directory.GetFiles(store.MasterFolder, "*.partial"));
            Assert.All(Directory.GetFiles(store.MasterFolder), file => Assert.EndsWith(".jpg", file));
        }

        var summary = await _projects.OpenAsync(_root);
        Assert.Equal(6, summary.ImageCount);
    }

    [Fact]
    public void Normalize_RerunWithSameSettingsSkipsFinishedImages()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var service = new NormalizeService(_codec);

        var first = service.Normalize(store, Options);
        var writes = Directory.GetFiles(store.MasterFolder).ToDictionary(file => file, File.GetLastWriteTimeUtc);
        var second = service.Normalize(store, Options);

        Assert.Equal((0, first.Processed), (second.Processed, second.AlreadyDone));
        Assert.All(writes, pair => Assert.Equal(pair.Value, File.GetLastWriteTimeUtc(pair.Key)));
    }

    [Fact]
    public void Normalize_NewImagesJoinWithTheSameSettings()
    {
        PlantFolder();
        using var store = Store();
        var import = new ImportService(_codec);
        var analyze = new AnalyzeService(_codec);
        RunThrough(store, import, analyze, _images);
        var service = new NormalizeService(_codec);
        var first = service.Normalize(store, Options);

        using var scene = Scene(4242);
        Write("tốt mới.png", scene);
        RunThrough(store, import, analyze, _images);
        var second = service.Normalize(store, Options);

        Assert.Equal(1, second.Processed);
        Assert.Equal(first.MasterCount + 1, second.MasterCount);
    }

    [Fact]
    public void Normalize_ChangedSettingsRebuildMastersWhenThereAreNoLabels()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var service = new NormalizeService(_codec);
        service.Normalize(store, Options);
        var sourcesBefore = Directory.GetFiles(_images, "*", SearchOption.AllDirectories).Length;

        var changed = Options with { Format = MasterFormat.Png };
        Assert.Equal(ConfigChange.MastersWillBeRebuilt, NormalizeService.CheckConfigChange(store, changed));
        var result = service.Normalize(store, changed);

        Assert.Equal(6, result.MasterCount);
        Assert.All(Directory.GetFiles(store.MasterFolder), file => Assert.EndsWith(".png", file));
        Assert.Equal(sourcesBefore, Directory.GetFiles(_images, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Normalize_RefusesToChangeSettingsOnceLabelsExist()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var service = new NormalizeService(_codec);
        service.Normalize(store, Options);
        var masters = Directory.GetFiles(store.MasterFolder).Order().ToArray();
        AddLabel();

        var changed = Options with { Format = MasterFormat.Png };
        Assert.Equal(ConfigChange.BlockedByAnnotations, NormalizeService.CheckConfigChange(store, changed));
        Assert.Throws<ProjectException>(() => service.Normalize(store, changed));

        Assert.Equal(masters, Directory.GetFiles(store.MasterFolder).Order());
        Assert.Equal(ConfigChange.None, NormalizeService.CheckConfigChange(store, Options));
    }

    [Fact]
    public async Task RejectingASourceLater_TakesItsMastersOutOfTheCounts()
    {
        PlantFolder();
        using (var store = Store())
        {
            RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
            new NormalizeService(_codec).Normalize(store, Options);
            var good = store.LoadSources().First(source => !source.IsRejected);
            store.ReplaceRejections(new[] { new CleanDecision(good.Id, RejectReason.Blurry, "x") });
        }

        Assert.Equal(5, (await _projects.OpenAsync(_root)).ImageCount);
    }

    [Fact]
    public void Normalize_MissingSourceFileIsReportedAndOthersContinue()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var victim = store.LoadSources().First(source => !source.IsRejected);
        File.Delete(victim.OriginalPath);

        var result = new NormalizeService(_codec).Normalize(store, Options);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(victim.FileName, failure.FileName);
        Assert.Equal(5, result.MasterCount);
    }

    [Fact]
    public void Normalize_CancelledRunKeepsWhatIsDoneAndResumes()
    {
        PlantFolder();
        using var store = Store();
        RunThrough(store, new ImportService(_codec), new AnalyzeService(_codec), _images);
        var service = new NormalizeService(_codec);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => service.Normalize(store, Options, null, cancellation.Token));

        Assert.Equal(6, service.Normalize(store, Options).MasterCount);
    }

    private void AddLabel()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "project.db")};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO classes (id, name, color, sort_order) VALUES (1, 'x', '#4A90E2', 0);
            INSERT INTO annotations (id, image_id, class_id, kind, points_json, source, created_at, updated_at)
                VALUES ('a', (SELECT MIN(id) FROM images), 1, 'box', '[]', 'manual', 't', 't');
            """;
        command.ExecuteNonQuery();
    }
}
