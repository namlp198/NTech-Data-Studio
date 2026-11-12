using System.IO.Compression;
using System.Security.Cryptography;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Import;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Application.Tests.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Exporters;
using NTech.DataStudio.Imaging.Augment;
using NTech.DataStudio.Storage.Sqlite;
using OpenCvSharp;

namespace NTech.DataStudio.Application.Tests.Export;

/// <summary>The whole export on a real project folder, real masters on disk and the real OpenCV engine.</summary>
public sealed class ExportServiceTests : IDisposable
{
    private const int Width = AnnotateFixture.Width;
    private const int Height = AnnotateFixture.Height;
    private const int ImageCount = 10;

    private static readonly ModelProfile DetectProfile = new("yolo26-detect", "ultralytics-yolo", TaskKind.Detect, 640, new[] { 640 }, 32, 12, new[] { "yolo" });

    private readonly AnnotateFixture _fixture = new(ImageCount);
    private readonly ProjectService _projects = new(new SqliteProjectStore());
    private readonly ExportService _export;
    private readonly DatasetService _datasets;
    private readonly long _red;
    private readonly long _blue;

    public ExportServiceTests()
    {
        _projects.OpenAsync(_fixture.Root).GetAwaiter().GetResult();
        var engine = new OpenCvAugmentationEngine();
        _export = new ExportService(_projects, engine, DatasetExporters.All());
        _datasets = new DatasetService(_projects, engine);

        // A dark master with a white block exactly where the label sits, so a label that drifts shows up in the pixels.
        Directory.CreateDirectory(Path.Combine(_fixture.Root, "master"));
        for (var id = 1; id <= ImageCount; id++)
        {
            using var image = new Mat(Height, Width, MatType.CV_8UC3, Scalar.All(30));
            Cv2.Rectangle(image, new Rect(60, 30, 40, 30), Scalar.All(255), -1);
            File.WriteAllBytes(Path.Combine(_fixture.Root, "master", $"m{id}.jpg"), Encode(image));
        }

        // The fixture gives every master the same source, and masters of one source always share a split.
        // Give each its own so the split has train, val and test to work with.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_fixture.DatabasePath};Pooling=False;Foreign Keys=True"))
        {
            connection.Open();
            for (var id = 2; id <= ImageCount; id++)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO source_images (id, original_path, stored_path, is_copied, file_size, modified_ticks, format, width, height, bit_depth, channels)
                    VALUES ({id}, 'a{id}', 'a{id}', 0, 1, 1, 'PNG', 400, 200, 8, 3);
                    UPDATE images SET source_image_id = {id} WHERE id = {id};
                    """;
                command.ExecuteNonQuery();
            }
        }

        using var store = _fixture.OpenStore();
        _red =store.AddClass("red", "#E25D5D", 1).Id;
        _blue = store.AddClass("blue", "#4A90E2", 2).Id;
        var now = DateTimeOffset.UtcNow;
        for (var id = 1; id <= ImageCount; id++)
        {
            var box = new Annotation(Guid.NewGuid(), id, id % 2 == 0 ? _red : _blue, ShapeKind.Box,
                new PixelRect(60, 30, 40, 30).Corners(), AnnotationSource.Manual, now, now);
            store.Apply(new StoreChange(id, Array.Empty<Guid>(), new[] { box }, new[] { id % 2 == 0 ? _red : _blue },
                Array.Empty<long>(), ImageStatus.Labeled));
        }
    }

    public void Dispose() => _fixture.Dispose();

    private static byte[] Encode(Mat image)
    {
        Cv2.ImEncode(".jpg", image, out var bytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
        return bytes;
    }

    private long CreateVersion(DatasetConfig config, string name = "v001")
    {
        var workspace = _datasets.Load();
        var plan = VersionBuilder.Build(workspace.Images, workspace.Classes, config);
        return _datasets.CreateVersion(name, config, plan).Id;
    }

    private static DatasetConfig Config(AugmentConfig? augment = null, PreprocessConfig? preprocess = null) => new()
    {
        Split = new SplitConfig { GroupByFolder = false, TrainPercent = 60, ValPercent = 20, TestPercent = 20 },
        Preprocess = preprocess ?? new PreprocessConfig(),
        Augment = augment ?? new AugmentConfig()
    };

    private ExportRequest Request(long versionId, string? folder = null, ModelProfile? profile = null, bool overwrite = false, bool zip = false) =>
        new(versionId, profile ?? DetectProfile, 640, ExportImageFormat.Jpeg, 95, OutputFolder: folder ?? Path.Combine(_fixture.Root, "out"), Overwrite: overwrite, Zip: zip);

    private static string[] Files(string folder, string pattern) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, pattern, SearchOption.AllDirectories) : Array.Empty<string>();

    private static string Fingerprint(string folder)
    {
        using var all = SHA256.Create();
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetRelativePath(folder, file).Replace('\\', '/');
            // version.json holds the creation time of the version, not of the export, so it is stable too.
            all.TransformBlock(System.Text.Encoding.UTF8.GetBytes(name), 0, System.Text.Encoding.UTF8.GetByteCount(name), null, 0);
            var bytes = File.ReadAllBytes(file);
            all.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        all.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(all.Hash!);
    }

    [Fact]
    public void YoloDetect_WritesImagesLabelsYamlAndTheDescribingFiles()
    {
        var versionId = CreateVersion(Config());
        var assignments = _datasets.LoadAssignments(versionId);

        var result = _export.Export(Request(versionId));

        var folder = result.Folder;
        Assert.EndsWith("v001_yolo26_detect", folder);
        foreach (var split in new[] { SplitKind.Train, SplitKind.Val, SplitKind.Test })
        {
            var expected = assignments.Count(item => item.Split == split);
            var name = split.ToString().ToLowerInvariant();
            Assert.Equal(expected, Files(Path.Combine(folder, "images", name), "*.jpg").Length);
            Assert.Equal(expected, Files(Path.Combine(folder, "labels", name), "*.txt").Length);
        }

        Assert.True(File.Exists(Path.Combine(folder, "data.yaml")));
        Assert.True(File.Exists(Path.Combine(folder, "version.json")));
        Assert.True(File.Exists(Path.Combine(folder, "README.dataset.md")));
        Assert.Contains("yolo detect train", File.ReadAllText(Path.Combine(folder, "README.dataset.md")));
        Assert.Equal(ImageCount, result.ImagesPerSplit.Sum());
        Assert.Equal(ImageCount, result.LabelsPerSplit.Sum());
        Assert.False(Directory.Exists(folder + ".partial"));

        // Every label file has exactly the one box of its image, and the class index is in range.
        foreach (var file in Files(Path.Combine(folder, "labels"), "*.txt"))
        {
            var label = Assert.Single(YoloLabelParser.Parse(File.ReadAllText(file), YoloLabelKind.Detect, Width, Height));
            Assert.InRange(label.ClassKey, 0, 1);
        }
    }

    [Fact]
    public void WithoutPreprocessingOrAugmentation_PicturesAreTheMastersThemselves()
    {
        var versionId = CreateVersion(Config());

        var result = _export.Export(Request(versionId));

        var masters = Enumerable.Range(1, ImageCount).Select(id => File.ReadAllBytes(Path.Combine(_fixture.Root, "master", $"m{id}.jpg"))).ToList();
        foreach (var picture in Files(Path.Combine(result.Folder, "images"), "*.jpg"))
            Assert.Contains(masters, master => master.AsSpan().SequenceEqual(File.ReadAllBytes(picture)));
    }

    [Fact]
    public void Augmentation_MultipliesOnlyTrain_AndEachPictureHasItsOwnLabelFile()
    {
        var versionId = CreateVersion(Config(new AugmentConfig { Multiplier = 3, FlipHorizontal = true, BrightnessPercent = 20 }));
        var assignments = _datasets.LoadAssignments(versionId);
        var train = assignments.Count(item => item.Split == SplitKind.Train);

        var result = _export.Export(Request(versionId));

        Assert.Equal(train * 3, result.ImagesPerSplit[(int)SplitKind.Train]);
        Assert.Equal(assignments.Count(item => item.Split == SplitKind.Val), result.ImagesPerSplit[(int)SplitKind.Val]);
        var pictures = Files(Path.Combine(result.Folder, "images", "train"), "*.jpg").Select(Path.GetFileNameWithoutExtension).Order().ToList();
        var labels = Files(Path.Combine(result.Folder, "labels", "train"), "*.txt").Select(Path.GetFileNameWithoutExtension).Order().ToList();
        Assert.Equal(pictures, labels);
        Assert.Equal(pictures.Count, pictures.Distinct().Count());
    }

    [Fact]
    public void ExportedLabelsStayOnTheirObject_AfterFlipRotateScaleAndShift()
    {
        var versionId = CreateVersion(Config(new AugmentConfig
        {
            Multiplier = 6, FlipHorizontal = true, FlipVertical = true, Rotate90Clockwise = true,
            RotateDegrees = 15, ScalePercent = 15, TranslatePercent = 10
        }));

        var result = _export.Export(Request(versionId));

        var checkedPictures = 0;
        foreach (var picturePath in Files(Path.Combine(result.Folder, "images", "train"), "*.jpg"))
        {
            var labelPath = Path.Combine(result.Folder, "labels", "train", Path.GetFileNameWithoutExtension(picturePath) + ".txt");
            using var image = Cv2.ImDecode(File.ReadAllBytes(picturePath), ImreadModes.Grayscale);
            var labels = YoloLabelParser.Parse(File.ReadAllText(labelPath), YoloLabelKind.Detect, image.Width, image.Height);
            if (labels.Count == 0) continue; // the shift took it out of the picture

            using var mask = image.Threshold(160, 255, ThresholdTypes.Binary);
            var moments = Cv2.Moments(mask, binaryImage: true);
            if (moments.M00 < 20) continue;

            var box = PolygonMath.BoundingBox(labels[0].Points);
            // The block can be cut by the border, which moves its centre of mass but not by much for a block this size.
            Assert.InRange(moments.M10 / moments.M00 + 0.5, box.X + box.Width / 2 - 3, box.X + box.Width / 2 + 3);
            Assert.InRange(moments.M01 / moments.M00 + 0.5, box.Y + box.Height / 2 - 3, box.Y + box.Height / 2 + 3);
            checkedPictures++;
        }

        Assert.True(checkedPictures >= 20, $"only {checkedPictures} pictures could be checked");
    }

    [Fact]
    public void TheSameVersionExportsTheSameBytesTwice()
    {
        var versionId = CreateVersion(Config(new AugmentConfig
        {
            Multiplier = 4, FlipHorizontal = true, RotateDegrees = 10, BrightnessPercent = 15, NoisePercent = 4, BlurPx = 1.5, JpegQualityMin = 70
        }));

        var first = _export.Export(Request(versionId, Path.Combine(_fixture.Root, "a")));
        var second = _export.Export(Request(versionId, Path.Combine(_fixture.Root, "b")));

        Assert.Equal(Fingerprint(first.Folder), Fingerprint(second.Folder));
    }

    [Fact]
    public void ExportingWithAResizeWritesThePreprocessedPictures_AndLabelsFollow()
    {
        var versionId = CreateVersion(Config(preprocess: new PreprocessConfig { Resize = ResizeMode.Fit, Width = 320, Height = 320 }));

        var result = _export.Export(Request(versionId));

        var picture = Files(Path.Combine(result.Folder, "images", "train"), "*.jpg").First();
        using var image = Cv2.ImDecode(File.ReadAllBytes(picture), ImreadModes.Color);
        Assert.Equal((320, 320), (image.Width, image.Height));
        var label = YoloLabelParser.Parse(
            File.ReadAllText(Path.Combine(result.Folder, "labels", "train", Path.GetFileNameWithoutExtension(picture) + ".txt")),
            YoloLabelKind.Detect, 320, 320).Single();
        // 200x100 fitted into 320x320 scales by 1.6 and sits 80 px down: the box (60,30)-(100,60) becomes (96,128)-(160,176).
        var box = PolygonMath.BoundingBox(label.Points);
        Assert.Equal((96, 128, 64, 48), (Math.Round(box.X), Math.Round(box.Y), Math.Round(box.Width), Math.Round(box.Height)));
    }

    [Fact]
    public void ClassMapRenumbersClassesWithoutGaps()
    {
        var config = Config(preprocess: new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(_red, null) } });
        var versionId = CreateVersion(config);

        var result = _export.Export(Request(versionId));

        var yaml = File.ReadAllText(Path.Combine(result.Folder, "data.yaml"));
        var names = YoloLabelParser.ParseNames(yaml);
        Assert.Equal(new[] { "blue" }, names.Values);
        // Red is dropped, so only blue boxes remain, and blue is class 0.
        foreach (var file in Files(Path.Combine(result.Folder, "labels"), "*.txt"))
        {
            foreach (var label in YoloLabelParser.Parse(File.ReadAllText(file), YoloLabelKind.Detect, Width, Height))
                Assert.Equal(0, label.ClassKey);
        }
    }

    [Fact]
    public void Errors_BlockTheExport_AndNothingIsWritten()
    {
        var versionId = CreateVersion(Config());
        var segment = DetectProfile with { Id = "yolo26-segment", Task = TaskKind.Segment };

        var error = Assert.Throws<ExportBlockedException>(() => _export.Export(Request(versionId, profile: segment)));

        Assert.True(error.Report.HasErrors);
        Assert.False(Directory.Exists(Path.Combine(_fixture.Root, "out")));
    }

    [Fact]
    public void Validate_ReportsWithoutWriting()
    {
        var versionId = CreateVersion(Config());

        var report = _export.Validate(versionId, DetectProfile, 640);

        Assert.False(report.HasErrors);
        Assert.False(Directory.Exists(Path.Combine(_fixture.Root, "out")));
    }

    [Fact]
    public void AMissingMasterIsAnErrorNotACrashHalfwayThrough()
    {
        var versionId = CreateVersion(Config());
        File.Delete(Path.Combine(_fixture.Root, "master", "m3.jpg"));

        var error = Assert.Throws<ExportBlockedException>(() => _export.Export(Request(versionId)));

        Assert.Contains(error.Report.Issues, issue => issue.Message.Contains("không còn trên đĩa"));
        Assert.False(Directory.Exists(Path.Combine(_fixture.Root, "out", "v001_yolo26_detect.partial")));
    }

    [Fact]
    public void ACorruptMasterStopsTheExport_AndLeavesNoPartialFolder()
    {
        var versionId = CreateVersion(Config(new AugmentConfig { Multiplier = 2, FlipHorizontal = true }));
        File.WriteAllText(Path.Combine(_fixture.Root, "master", "m3.jpg"), "not a picture");

        Assert.ThrowsAny<Exception>(() => _export.Export(Request(versionId)));

        var folder = Path.Combine(_fixture.Root, "out");
        Assert.Empty(Directory.Exists(folder) ? Directory.GetDirectories(folder) : Array.Empty<string>());
    }

    [Fact]
    public void CancellingLeavesNothingBehind()
    {
        var versionId = CreateVersion(Config(new AugmentConfig { Multiplier = 5, FlipHorizontal = true }));
        using var cancel = new CancellationTokenSource();
        var progress = new Progress<ExportProgress>(_ => { });
        var seen = 0;
        var inline = new SyncProgress(report =>
        {
            if (report.Phase == ExportPhase.Rendering && ++seen == 5) cancel.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => _export.Export(Request(versionId), inline, cancel.Token));

        var folder = Path.Combine(_fixture.Root, "out");
        Assert.Empty(Directory.Exists(folder) ? Directory.GetDirectories(folder) : Array.Empty<string>());
        GC.KeepAlive(progress);
    }

    private sealed class SyncProgress : IProgress<ExportProgress>
    {
        private readonly Action<ExportProgress> _handler;
        public SyncProgress(Action<ExportProgress> handler) => _handler = handler;
        public void Report(ExportProgress value) => _handler(value);
    }

    [Fact]
    public void AnExistingFolderIsNotReplacedUnlessItIsAnEarlierExportAndOverwriteIsOn()
    {
        var versionId = CreateVersion(Config());
        var folder = Path.Combine(_fixture.Root, "out");
        var target = Path.Combine(folder, "v001_yolo26_detect");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "mine.txt"), "keep");

        // Someone else's folder: refused even with overwrite, and left alone.
        Assert.Throws<InvalidOperationException>(() => _export.Export(Request(versionId, overwrite: true)));
        Assert.True(File.Exists(Path.Combine(target, "mine.txt")));

        Directory.Delete(target, true);
        _export.Export(Request(versionId));

        // Our own earlier export: refused without overwrite, replaced with it.
        Assert.Throws<InvalidOperationException>(() => _export.Export(Request(versionId)));
        File.WriteAllText(Path.Combine(target, "stale.txt"), "old");
        _export.Export(Request(versionId, overwrite: true));
        Assert.False(File.Exists(Path.Combine(target, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(target, "data.yaml")));
    }

    [Fact]
    public void Zip_HoldsTheDataset()
    {
        var versionId = CreateVersion(Config());

        var result = _export.Export(Request(versionId, zip: true));

        Assert.NotNull(result.ZipPath);
        using var archive = ZipFile.OpenRead(result.ZipPath);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("data.yaml"));
        Assert.Contains(archive.Entries, entry => entry.FullName.Contains("images/train/") || entry.FullName.Contains("images\\train\\"));
    }

    [Fact]
    public void Coco_WritesOneAnnotationFilePerSplitNextToThePictures()
    {
        var versionId = CreateVersion(Config());
        var coco = DetectProfile with { Id = "rf-detr-detect", Family = "rf-detr", ExportFormats = new[] { "coco" } };

        var result = _export.Export(Request(versionId, profile: coco));

        Assert.EndsWith("v001_coco_detect", result.Folder);
        foreach (var split in new[] { "train", "valid", "test" })
            Assert.True(File.Exists(Path.Combine(result.Folder, split, "_annotations.coco.json")), split);
        var parsed = CocoLabelParser.Parse(File.ReadAllText(Path.Combine(result.Folder, "train", "_annotations.coco.json")));
        foreach (var image in parsed.Images)
            Assert.True(File.Exists(Path.Combine(result.Folder, "train", image.FileName)));
    }

    [Fact]
    public void MaskRcnnProfile_ExportsCocoSegmentationWithItsOwnTrainNote()
    {
        var versionId = CreateVersion(Config());
        var polygon = DetectProfile with { Id = "maskrcnn-segment", Family = "mask-rcnn", Task = TaskKind.Segment, ExportFormats = new[] { "coco" } };

        // The fixture only drew boxes, and segmentation needs polygons: blocked, as for any segmentation profile.
        Assert.Throws<ExportBlockedException>(() => _export.Export(Request(versionId, profile: polygon)));

        // Replace every box by a polygon of the same outline.
        using (var store = _fixture.OpenStore())
        {
            var service = _fixture.Service(store);
            for (var id = 1; id <= ImageCount; id++)
            {
                var document = _fixture.OpenImage(service, id);
                var box = document.Annotations.Single();
                var triangle = new Annotation(Guid.NewGuid(), id, box.ClassId, ShapeKind.Polygon,
                    new[] { new PixelPoint(60, 30), new PixelPoint(100, 30), new PixelPoint(80, 60) },
                    AnnotationSource.Manual, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                store.Apply(new StoreChange(id, new[] { box.Id }, new[] { triangle }, Array.Empty<long>(), Array.Empty<long>(), ImageStatus.Labeled));
            }
        }

        var result = _export.Export(Request(versionId, profile: polygon));

        Assert.EndsWith("v001_coco_segment", result.Folder);
        var coco = CocoLabelParser.Parse(File.ReadAllText(Path.Combine(result.Folder, "train", "_annotations.coco.json")));
        Assert.All(coco.Images.SelectMany(image => image.Labels), label => Assert.Equal(ShapeKind.Polygon, label.Kind));
        var readme = File.ReadAllText(Path.Combine(result.Folder, "README.dataset.md"));
        Assert.Contains("Mask R-CNN", readme);
        Assert.DoesNotContain("RFDETR", readme);
    }

    [Fact]
    public void Classify_SortsPicturesByTheirTag()
    {
        var versionId = CreateVersion(Config());
        var classify = DetectProfile with { Id = "yolo26-classify", Task = TaskKind.Classify, MinObjectPx = 0 };

        var result = _export.Export(Request(versionId, profile: classify));

        Assert.Equal(ImageCount, Files(result.Folder, "*.jpg").Length);
        Assert.Empty(Files(result.Folder, "*.txt"));
        // Odd ids are tagged blue, even ids red.
        Assert.NotEmpty(Files(Path.Combine(result.Folder, "train", "red"), "*.jpg").Concat(Files(Path.Combine(result.Folder, "train", "blue"), "*.jpg")));
    }

    [Fact]
    public async Task VersionJson_NamesEveryTargetTaskOfTheProject_AndKeepsTheFirstUnderTheOldKey()
    {
        await _projects.SetTargetTasksAsync(new[] { NTech.DataStudio.Domain.Profiles.TaskKind.Segment, NTech.DataStudio.Domain.Profiles.TaskKind.Detect });
        var versionId = CreateVersion(Config(new AugmentConfig { Multiplier = 1 }));

        var result = _export.Export(Request(versionId));

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Folder, "version.json")));
        var root = json.RootElement;
        Assert.Equal("detect", root.GetProperty("targetTask").GetString());
        Assert.Equal(new[] { "detect", "segment" }, root.GetProperty("targetTasks").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void VersionJson_HoldsTheConfigAsSaved_SoTheExportCanBeRebuilt()
    {
        var versionId = CreateVersion(Config(new AugmentConfig { Multiplier = 3, FlipHorizontal = true }));

        var result = _export.Export(Request(versionId));

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(result.Folder, "version.json")));
        var root = json.RootElement;
        Assert.Equal("yolo26_detect", root.GetProperty("format").GetString());
        Assert.Equal(42, root.GetProperty("version").GetProperty("seed").GetInt32());
        Assert.Equal(3, root.GetProperty("augment").GetProperty("Multiplier").GetInt32());
        Assert.Equal(2, root.GetProperty("classes").GetArrayLength());
    }
}
