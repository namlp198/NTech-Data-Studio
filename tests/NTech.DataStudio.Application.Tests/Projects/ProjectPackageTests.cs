using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Storage.Files;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Application.Tests.Projects;

/// <summary>Decision 0020: a project as one .zip file with a version, packed and unpacked.</summary>
public class ProjectPackageTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly SqliteProjectStore _store = new();
    private readonly ZipProjectPackager _packager = new();
    private readonly ProjectService _projects;
    private readonly ProjectPackageService _service;

    public ProjectPackageTests()
    {
        _projects = new ProjectService(_store);
        _service = new ProjectPackageService(_projects, _packager);
    }

    public void Dispose() => _temp.Dispose();

    private string Out => Path.Combine(_temp.Path, "out");

    private async Task<ProjectFolderLayout> NewProject(string name = "Bộ dây điện", params TaskKind[] tasks)
    {
        var summary = await _projects.CreateAsync(_temp.Path, name, tasks);
        var layout = new ProjectFolderLayout(summary.RootPath);
        Put(layout.MasterPath, "a.jpg", 300);
        Put(layout.MasterPath, "b.jpg", 200);
        Put(layout.ThumbsPath, "a.jpg", 20);
        Put(layout.SourcePath, "big_a.png", 5000);
        Put(layout.SourcePath, "big_b.png", 4000);
        Put(layout.ExportsPath, Path.Combine("v001_yolo", "labels.txt"), 50);
        // An export that did not finish must never travel.
        Put(layout.ExportsPath, Path.Combine("v002_yolo.partial", "half.txt"), 50);
        return layout;
    }

    private static void Put(string folder, string relative, int size)
    {
        var path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
    }

    private static string[] Names(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ---- packing ----

    [Fact]
    public async Task Pack_WritesANumberedZipWithAManifestFirst()
    {
        var layout = await NewProject("Bộ dây điện", TaskKind.Detect, TaskKind.Segment);

        var result = await _service.PackAsync(Out, new PackageOptions(), null);

        Assert.Equal(Path.Combine(Out, "Bộ dây điện_v001.zip"), result.FilePath);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(ZipProjectPackager.ManifestName, ZipFirst(result.FilePath));
        var manifest = await _service.ReadManifestAsync(result.FilePath);
        Assert.Equal(ZipProjectPackager.FormatVersion, manifest.FormatVersion);
        Assert.Equal("Bộ dây điện", manifest.ProjectName);
        Assert.Equal(1, manifest.PackageNumber);
        Assert.Equal(new[] { "detect", "segment" }, manifest.Tasks);
        Assert.True(manifest.IncludesSource);
        Assert.False(manifest.IncludesExports);
        Assert.False(string.IsNullOrWhiteSpace(manifest.AppVersion));
        Assert.True(manifest.SchemaVersion >= 1);
        Assert.Equal(result.Manifest, manifest);
        _ = layout;
    }

    private static string ZipFirst(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries[0].FullName;
    }

    [Fact]
    public async Task Pack_TakesWhatTheOptionsSay_AndNeverAnUnfinishedExport()
    {
        await NewProject();

        var full = await _service.PackAsync(Out, new PackageOptions(IncludeSource: true, IncludeExports: true), null);
        var lean = await _service.PackAsync(Out, new PackageOptions(IncludeSource: false, IncludeExports: false), null);

        var all = Names(full.FilePath);
        Assert.Contains("project/project.db", all);
        Assert.Contains("project/master/a.jpg", all);
        Assert.Contains("project/thumbs/a.jpg", all);
        Assert.Contains("project/source/big_a.png", all);
        Assert.Contains("project/exports/v001_yolo/labels.txt", all);
        Assert.DoesNotContain(all, name => name.Contains(".partial"));
        Assert.DoesNotContain(all, name => name.EndsWith("-wal") || name.EndsWith("-shm"));

        var small = Names(lean.FilePath);
        Assert.Contains("project/master/b.jpg", small);
        Assert.DoesNotContain(small, name => name.StartsWith("project/source/"));
        Assert.DoesNotContain(small, name => name.StartsWith("project/exports/"));
        Assert.True(new FileInfo(lean.FilePath).Length < new FileInfo(full.FilePath).Length);
    }

    [Fact]
    public async Task Pack_CountsUpEachTime_AndTheCountSurvivesInTheProject()
    {
        await NewProject();

        var first = await _service.PackAsync(Out, new PackageOptions(), null);
        var second = await _service.PackAsync(Out, new PackageOptions(), null);

        Assert.EndsWith("_v001.zip", first.FilePath);
        Assert.EndsWith("_v002.zip", second.FilePath);
        Assert.Equal(2, (await _service.ReadManifestAsync(second.FilePath)).PackageNumber);
    }

    [Fact]
    public async Task Pack_NeverOverwritesAFileThatIsThere()
    {
        await NewProject();
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "Bộ dây điện_v001.zip"), "someone else's file");

        var result = await _service.PackAsync(Out, new PackageOptions(), null);

        Assert.EndsWith("_v002.zip", result.FilePath);
        Assert.Equal("someone else's file", File.ReadAllText(Path.Combine(Out, "Bộ dây điện_v001.zip")));
    }

    [Fact]
    public async Task Pack_Cancelled_LeavesNothingBehind_AndTheCountAlone()
    {
        await NewProject();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.PackAsync(Out, new PackageOptions(), null, cancel.Token));

        Assert.Empty(Directory.Exists(Out) ? Directory.GetFiles(Out) : Array.Empty<string>());
        var again = await _service.PackAsync(Out, new PackageOptions(), null);
        Assert.EndsWith("_v001.zip", again.FilePath);
    }

    [Fact]
    public async Task Pack_ReportsProgressUpToTheWholeSize()
    {
        await NewProject();
        var reports = new List<PackageProgress>();

        await _service.PackAsync(Out, new PackageOptions(), new Progress<PackageProgress>(reports.Add));
        await Task.Delay(100);

        Assert.NotEmpty(reports);
        Assert.All(reports, report => Assert.InRange(report.Fraction, 0, 1));
    }

    [Fact]
    public async Task Estimate_SplitsTheProjectIntoWhatAlwaysTravelsAndWhatIsOptional()
    {
        await NewProject();

        var estimate = _service.Estimate();

        Assert.Equal(5000 + 4000, estimate.SourceBytes);
        Assert.Equal(2, estimate.SourceFiles);
        // The unfinished export is not counted.
        Assert.Equal(50, estimate.ExportsBytes);
        Assert.Equal(1, estimate.ExportFiles);
        Assert.True(estimate.CoreBytes >= 300 + 200 + 20);
        Assert.Equal(estimate.CoreBytes, estimate.TotalBytes(new PackageOptions(false, false)));
        Assert.Equal(estimate.CoreBytes + 9000 + 50, estimate.TotalBytes(new PackageOptions(true, true)));
    }

    [Fact]
    public async Task Pack_WithoutAProject_SaysSo()
    {
        await Assert.ThrowsAsync<PackageException>(() => _service.PackAsync(Out, new PackageOptions(), null));
        Assert.Throws<PackageException>(() => _service.Estimate());
    }

    // ---- unpacking ----

    [Fact]
    public async Task Unpack_GivesBackTheSameProject_WithItsFilesByteForByte()
    {
        var layout = await NewProject("Bộ dây điện", TaskKind.Segment);
        var packed = await _service.PackAsync(Out, new PackageOptions(IncludeSource: true, IncludeExports: true), null);
        _projects.Close();

        var target = Path.Combine(_temp.Path, "elsewhere", "Bộ dây điện");
        var opened = await _service.UnpackAndOpenAsync(packed.FilePath, target, null);

        Assert.Equal(target, opened.RootPath);
        Assert.Equal("Bộ dây điện", opened.Name);
        Assert.Equal(new[] { TaskKind.Segment }, opened.Tasks);
        Assert.Same(opened, _projects.Current);
        foreach (var relative in new[] { "master/a.jpg", "master/b.jpg", "thumbs/a.jpg", "source/big_a.png", "source/big_b.png", "exports/v001_yolo/labels.txt" })
            Assert.Equal(Hash(Path.Combine(layout.RootPath, relative)), Hash(Path.Combine(target, relative)));
        Assert.False(Directory.Exists(target + ".partial"));
        Assert.False(File.Exists(Path.Combine(target, "exports", "v002_yolo.partial", "half.txt")));
    }

    [Fact]
    public async Task Pack_OfAProjectThatIsOpenAndBeingWrittenTo_HoldsTheLatestRows()
    {
        var layout = await NewProject();
        // Another connection writes a row and stays open, as the Annotate screen does.
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = layout.DatabasePath, Pooling = false }.ToString());
        writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL; INSERT INTO classes (name, color, sort_order) VALUES ('connector', '#4A90E2', 0), ('tape', '#E25D5D', 1);";
            command.ExecuteNonQuery();
        }

        var packed = await _service.PackAsync(Out, new PackageOptions(), null);
        _projects.Close();
        var opened = await _service.UnpackAndOpenAsync(packed.FilePath, Path.Combine(_temp.Path, "copy"), null);

        Assert.Equal(2, opened.ClassCount);
    }

    [Fact]
    public async Task Unpack_RefusesAFolderThatHasThings()
    {
        await NewProject();
        var packed = await _service.PackAsync(Out, new PackageOptions(), null);
        var target = Path.Combine(_temp.Path, "taken");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "x.txt"), "x");

        var exception = await Assert.ThrowsAsync<PackageException>(() => _service.UnpackAndOpenAsync(packed.FilePath, target, null));

        Assert.Contains("đã có dữ liệu", exception.Message);
        Assert.Equal("x", File.ReadAllText(Path.Combine(target, "x.txt")));
    }

    [Fact]
    public async Task Unpack_AnEmptyFolderIsFine()
    {
        await NewProject();
        var packed = await _service.PackAsync(Out, new PackageOptions(), null);
        var target = Path.Combine(_temp.Path, "empty");
        Directory.CreateDirectory(target);

        var opened = await _service.UnpackAndOpenAsync(packed.FilePath, target, null);

        Assert.Equal(target, opened.RootPath);
    }

    [Fact]
    public async Task Unpack_Cancelled_LeavesNothingAtAll()
    {
        await NewProject();
        var packed = await _service.PackAsync(Out, new PackageOptions(), null);
        var target = Path.Combine(_temp.Path, "never");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.UnpackAndOpenAsync(packed.FilePath, target, null, cancel.Token));

        Assert.False(Directory.Exists(target));
        Assert.False(Directory.Exists(target + ".partial"));
    }

    // ---- refusing what is not right ----

    private string MakeZip(string name, Action<ZipArchive> fill)
    {
        var path = Path.Combine(_temp.Path, name);
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        fill(archive);
        return path;
    }

    private static void AddText(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Manifest(int format = 1, int schema = 1) => JsonSerializer.Serialize(new PackageManifest(
        format, "0.1.0", schema, "p", DateTimeOffset.UtcNow, 1, Array.Empty<string>(), 0, true, false, 1, 10),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact]
    public async Task AFileThatIsNotAPackage_IsRefusedWithAReason()
    {
        var notZip = Path.Combine(_temp.Path, "x.zip");
        File.WriteAllText(notZip, "this is not a zip");
        var plain = MakeZip("plain.zip", archive => AddText(archive, "readme.txt", "hello"));

        await Assert.ThrowsAsync<PackageException>(() => _service.ReadManifestAsync(notZip));
        var exception = await Assert.ThrowsAsync<PackageException>(() => _service.ReadManifestAsync(plain));
        Assert.Contains(ZipProjectPackager.ManifestName, exception.Message);
    }

    [Fact]
    public async Task APackageOfANewerAppIsRefused_ByItsFormatAndByItsDatabaseVersion()
    {
        var newerFormat = MakeZip("f.zip", archive => AddText(archive, ZipProjectPackager.ManifestName, Manifest(format: 99)));
        var newerSchema = MakeZip("s.zip", archive => AddText(archive, ZipProjectPackager.ManifestName, Manifest(schema: 999)));

        var first = await Assert.ThrowsAsync<PackageException>(() => _service.ReadManifestAsync(newerFormat));
        var second = await Assert.ThrowsAsync<PackageException>(() => _service.ReadManifestAsync(newerSchema));

        Assert.Contains("mới hơn", first.Message);
        Assert.Contains("mới hơn", second.Message);
    }

    [Fact]
    public async Task APathThatClimbsOutOfTheFolder_IsRefusedAndNothingIsWritten()
    {
        var evil = MakeZip("evil.zip", archive =>
        {
            AddText(archive, ZipProjectPackager.ManifestName, Manifest());
            AddText(archive, "project/project.db", "x");
            AddText(archive, "project/../../escaped.txt", "gotcha");
        });
        var target = Path.Combine(_temp.Path, "inner", "target");

        var exception = await Assert.ThrowsAsync<PackageException>(() => _service.UnpackAndOpenAsync(evil, target, null));

        Assert.Contains("không hợp lệ", exception.Message);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "escaped.txt")));
        Assert.False(Directory.Exists(target));
        Assert.False(Directory.Exists(target + ".partial"));
    }

    [Fact]
    public async Task APackageWithoutADatabase_IsRefused()
    {
        var empty = MakeZip("nodb.zip", archive => AddText(archive, ZipProjectPackager.ManifestName, Manifest()));
        var target = Path.Combine(_temp.Path, "nodb");

        await Assert.ThrowsAsync<PackageException>(() => _service.UnpackAndOpenAsync(empty, target, null));

        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void TheTargetFolderIsNamedAfterTheProject_WithoutCharactersAFolderCannotHave()
    {
        var manifest = new PackageManifest(1, "0.1.0", 1, "Bộ: dây/điện?", DateTimeOffset.UtcNow, 1, Array.Empty<string>(), 0, true, false, 1, 1);

        Assert.Equal(Path.Combine(@"D:\data", "Bộ_ dây_điện_"), ProjectPackageService.TargetFolder(@"D:\data", manifest));
        Assert.Equal("Bộ dây điện_v007.zip", ZipProjectPackager.FileName("Bộ dây điện", 7));
    }

    /// <summary>
    /// Needs a real project, so it returns early without one: set NTECH_PACKAGE_PROJECT to a COPY of a project folder (packing
    /// counts the package number up inside it). The package is made lean and full, unpacked, opened, and the counts compared.
    /// NTECH_PACKAGE_OUT, if set, receives the numbers as text.
    /// </summary>
    [Fact]
    public async Task RealProject_PacksAndUnpacksToTheSameCounts()
    {
        var folder = Environment.GetEnvironmentVariable("NTECH_PACKAGE_PROJECT");
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "project.db"))) return;

        var original = await _projects.OpenAsync(folder);
        var estimate = _service.Estimate();
        var report = new StringBuilder();
        report.AppendLine($"{original.Name}: {original.ImageCount} images, {original.AnnotatedImageCount} annotated, {original.ClassCount} classes, {original.VersionCount} versions, tasks {string.Join("+", original.Tasks)}");
        report.AppendLine($"core {estimate.CoreBytes / 1048576.0:0.0} MB ({estimate.CoreFiles} files), source {estimate.SourceBytes / 1048576.0:0.0} MB ({estimate.SourceFiles}), exports {estimate.ExportsBytes / 1048576.0:0.0} MB ({estimate.ExportFiles})");

        foreach (var options in new[] { new PackageOptions(IncludeSource: false, IncludeExports: false), new PackageOptions(IncludeSource: false, IncludeExports: true) })
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var packed = await _service.PackAsync(Out, options, null);
            var packTime = clock.Elapsed.TotalSeconds;
            clock.Restart();
            var target = Path.Combine(_temp.Path, "unpacked_" + packed.Manifest.PackageNumber, "p");
            var opened = await _service.UnpackAndOpenAsync(packed.FilePath, target, null);
            var unpackTime = clock.Elapsed.TotalSeconds;

            Assert.Equal(original.ImageCount, opened.ImageCount);
            Assert.Equal(original.AnnotatedImageCount, opened.AnnotatedImageCount);
            Assert.Equal(original.ClassCount, opened.ClassCount);
            Assert.Equal(original.VersionCount, opened.VersionCount);
            Assert.Equal(original.Tasks, opened.Tasks);
            foreach (var master in Directory.EnumerateFiles(Path.Combine(folder, "master")).Take(20))
                Assert.Equal(Hash(master), Hash(Path.Combine(target, "master", Path.GetFileName(master))));
            report.AppendLine($"{Path.GetFileName(packed.FilePath)} (exports {options.IncludeExports}): {packed.ZipBytes / 1048576.0:0.0} MB, {packed.Manifest.FileCount} files, packed in {packTime:0.0} s, unpacked in {unpackTime:0.0} s; counts match");
            await _projects.OpenAsync(folder);
        }

        if (Environment.GetEnvironmentVariable("NTECH_PACKAGE_OUT") is { Length: > 0 } outPath) File.WriteAllText(outPath, report.ToString());
    }
}
