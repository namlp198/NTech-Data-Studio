using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Application.Tests.Annotate;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Images;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Application.Tests.Datasets;

public class DatasetStoreAndServiceTests : IDisposable
{
    private readonly AnnotateFixture _fixture = new(imageCount: 6);
    private readonly ProjectService _projects = new(new SqliteProjectStore());

    public DatasetStoreAndServiceTests()
    {
        _projects.OpenAsync(_fixture.Root).GetAwaiter().GetResult();

        using var store = _fixture.OpenStore();
        var red = store.AddClass("red", "#E25D5D", 1);
        var blue = store.AddClass("blue", "#4A90E2", 2);
        var now = DateTimeOffset.UtcNow;

        // Image 1: two red boxes; image 2: a blue box; image 3: tagged blue; image 4: Null; images 5 and 6 stay New.
        store.Apply(Change(1, ImageStatus.Labeled, upserts: new[] { Box(1, red.Id, 10, 10, 50, 40, now), Box(1, red.Id, 80, 20, 120, 60, now) }));
        store.Apply(Change(2, ImageStatus.Labeled, upserts: new[] { Box(2, blue.Id, 30, 30, 70, 70, now) }));
        store.Apply(Change(3, ImageStatus.Labeled, tags: new[] { blue.Id }));
        store.Apply(Change(4, ImageStatus.Null));
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static StoreChange Change(long imageId, ImageStatus status, Annotation[]? upserts = null, long[]? tags = null) =>
        new(imageId, Array.Empty<Guid>(), upserts ?? Array.Empty<Annotation>(), tags ?? Array.Empty<long>(), Array.Empty<long>(), status);

    private static Annotation Box(long imageId, long classId, double left, double top, double right, double bottom, DateTimeOffset now) =>
        new(Guid.NewGuid(), imageId, classId, ShapeKind.Box, new PixelRect(left, top, right - left, bottom - top).Corners(),
            AnnotationSource.Manual, now, now);

    [Fact]
    public void LoadImages_CarriesLabelCountsTagsStatusAndGroup()
    {
        using var store = _projects.OpenDatasetStore();

        var images = store.LoadImages().ToDictionary(image => image.Id);

        Assert.Equal(6, images.Count);
        Assert.Equal(2, images[1].LabelsByClass.Values.Sum());
        Assert.Single(images[2].LabelsByClass);
        Assert.Empty(images[3].LabelsByClass);
        Assert.Single(images[3].TagClassIds);
        Assert.Equal(ImageStatus.Null, images[4].Status);
        Assert.Equal(ImageStatus.New, images[5].Status);
        Assert.All(images.Values, image => Assert.Equal(1, image.SourceImageId));
    }

    [Fact]
    public void CreateVersion_StoresTheConfigAndEveryAssignment()
    {
        var config = new DatasetConfig
        {
            Seed = 11,
            Preprocess = new PreprocessConfig { Resize = ResizeMode.Fit, Width = 320, Height = 320 },
            Augment = new AugmentConfig { Multiplier = 3, FlipHorizontal = true }
        };
        var assignments = new[]
        {
            new SplitAssignment(1, SplitKind.Train), new SplitAssignment(2, SplitKind.Val), new SplitAssignment(3, SplitKind.Test)
        };

        using var store = _projects.OpenDatasetStore();
        var created = store.CreateVersion("v001", config, assignments);

        var loaded = Assert.Single(store.LoadVersions());
        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal("v001", loaded.Name);
        var restored = DatasetService.ConfigOf(loaded);
        Assert.Equal(11, restored.Seed);
        Assert.Equal(ResizeMode.Fit, restored.Preprocess.Resize);
        Assert.Equal(config.Augment, restored.Augment);
        Assert.Equal(assignments, store.LoadAssignments(created.Id));
    }

    [Fact]
    public void CreateVersion_ANameCanOnlyBeUsedOnce()
    {
        using var store = _projects.OpenDatasetStore();
        store.CreateVersion("v001", new DatasetConfig(), new[] { new SplitAssignment(1, SplitKind.Train) });

        var error = Assert.Throws<InvalidOperationException>(() =>
            store.CreateVersion("v001", new DatasetConfig(), new[] { new SplitAssignment(2, SplitKind.Train) }));

        Assert.Contains("v001", error.Message);
        Assert.Single(store.LoadVersions());
    }

    [Fact]
    public void CreateVersion_AnImageThatIsGoneLeavesNothingBehind()
    {
        using var store = _projects.OpenDatasetStore();

        Assert.Throws<InvalidOperationException>(() =>
            store.CreateVersion("v001", new DatasetConfig(), new[] { new SplitAssignment(1, SplitKind.Train), new SplitAssignment(999, SplitKind.Val) }));

        Assert.Empty(store.LoadVersions());
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM version_items;"));
    }

    [Fact]
    public void DeleteVersion_TakesItsAssignmentsWithIt()
    {
        using var store = _projects.OpenDatasetStore();
        var created = store.CreateVersion("v001", new DatasetConfig(), new[] { new SplitAssignment(1, SplitKind.Train) });

        store.DeleteVersion(created.Id);

        Assert.Empty(store.LoadVersions());
        Assert.Equal(0, _fixture.Scalar("SELECT COUNT(*) FROM version_items;"));
    }

    [Fact]
    public async Task ProjectSummary_CountsVersionsAfterRefresh()
    {
        new DatasetService(_projects, new FakeEngine()).CreateVersion(
            "v001",
            new DatasetConfig(),
            new VersionPlan(new[] { new SplitAssignment(1, SplitKind.Train) }, Array.Empty<ClassSplitCount>(),
                new[] { 1, 0, 0 }, new[] { 0, 0, 0 }, 0, 0, 0, 1, 1, Array.Empty<string>()));

        await _projects.RefreshAsync();

        Assert.Equal(1, _projects.Current!.VersionCount);
    }

    [Fact]
    public void CreateVersion_RefusesAnEmptyNameOrAnEmptyPlan()
    {
        var service = new DatasetService(_projects, new FakeEngine());
        var empty = new VersionPlan(Array.Empty<SplitAssignment>(), Array.Empty<ClassSplitCount>(),
            new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, 0, 0, 0, 0, 0, Array.Empty<string>());
        var one = empty with { Assignments = new[] { new SplitAssignment(1, SplitKind.Train) } };

        Assert.Throws<InvalidOperationException>(() => service.CreateVersion("  ", new DatasetConfig(), one));
        Assert.Throws<InvalidOperationException>(() => service.CreateVersion("v001", new DatasetConfig(), empty));
    }

    [Fact]
    public void RenderPreview_GivesTheRequestedNumberOfCellsEvenWithFewImages()
    {
        var service = new DatasetService(_projects, new FakeEngine());
        var workspace = service.Load();
        var config = new DatasetConfig { Split = new SplitConfig { GroupByFolder = false }, Augment = new AugmentConfig { Multiplier = 4, FlipHorizontal = true } };
        var plan = VersionBuilder.Build(workspace.Images, workspace.Classes, config);

        var tiles = service.RenderPreview(workspace, config, plan, 9, shuffle: 0);

        Assert.Equal(9, tiles.Count);
        Assert.All(tiles, tile => Assert.NotNull(tile.Picture));
        Assert.All(tiles, tile => Assert.InRange(tile.CopyIndex, 1, 3));
    }

    [Fact]
    public void RenderPreview_LabelsAreTransformedWithThePictureAndTheClassMap()
    {
        var service = new DatasetService(_projects, new FakeEngine());
        var workspace = service.Load();
        var red = workspace.Classes.Single(c => c.Name == "red");
        var blue = workspace.Classes.Single(c => c.Name == "blue");
        var config = new DatasetConfig
        {
            // Everything in train, so image 2 is certainly among the cells.
            Split = new SplitConfig { GroupByFolder = false, TrainPercent = 100, ValPercent = 0, TestPercent = 0 },
            Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(blue.Id, red.Id) } }
        };
        var plan = VersionBuilder.Build(workspace.Images, workspace.Classes, config);

        var tiles = service.RenderPreview(workspace, config, plan, 6, shuffle: 0);

        // No augmentation: copy 0, labels untouched in position, blue renamed to red.
        Assert.All(tiles, tile => Assert.Equal(0, tile.CopyIndex));
        var imageTwo = tiles.First(tile => tile.Image.Id == 2);
        var label = Assert.Single(imageTwo.Labels);
        Assert.Equal(red.Id, label.ClassId);
        Assert.Equal(30, PolygonMath.BoundingBox(label.Points).X, 6);
    }

    [Fact]
    public void RenderPreview_SameInputsGiveTheSameCells_AndShuffleChangesThem()
    {
        var service = new DatasetService(_projects, new FakeEngine());
        var workspace = service.Load();
        var config = new DatasetConfig { Split = new SplitConfig { GroupByFolder = false }, Augment = new AugmentConfig { Multiplier = 5, RotateDegrees = 10 } };
        var plan = VersionBuilder.Build(workspace.Images, workspace.Classes, config);

        var first = service.RenderPreview(workspace, config, plan, 4, shuffle: 0);
        var again = service.RenderPreview(workspace, config, plan, 4, shuffle: 0);
        var other = service.RenderPreview(workspace, config, plan, 4, shuffle: 1);

        Assert.Equal(first.Select(t => (t.Image.Id, t.CopyIndex)), again.Select(t => (t.Image.Id, t.CopyIndex)));
        Assert.NotEqual(first.Select(t => (t.Image.Id, t.CopyIndex)), other.Select(t => (t.Image.Id, t.CopyIndex)));
    }

    [Fact]
    public void RenderPreview_AnUnreadableMasterBecomesAnErrorCellNotACrash()
    {
        var service = new DatasetService(_projects, new FakeEngine { Fail = true });
        var workspace = service.Load();
        var config = new DatasetConfig { Split = new SplitConfig { GroupByFolder = false } };
        var plan = VersionBuilder.Build(workspace.Images, workspace.Classes, config);

        var tiles = service.RenderPreview(workspace, config, plan, 3, shuffle: 0);

        Assert.All(tiles, tile =>
        {
            Assert.Null(tile.Picture);
            Assert.NotNull(tile.Error);
        });
    }

    [Fact]
    public void RenderPreview_WithoutImagesReturnsNoCells()
    {
        var service = new DatasetService(_projects, new FakeEngine());
        var workspace = service.Load();
        var plan = VersionBuilder.Build(Array.Empty<DatasetImage>(), workspace.Classes, new DatasetConfig());

        Assert.Empty(service.RenderPreview(workspace, new DatasetConfig(), plan, 9, shuffle: 0));
    }

    private sealed class FakeEngine : IAugmentationEngine
    {
        public bool Fail { get; init; }

        public RenderedImage Render(string masterPath, RenderPlan plan, RenderFormat format, int jpegQuality = 95) =>
            Fail
                ? throw new ImageReadException("không đọc được")
                : new RenderedImage(new byte[] { 1, 2, 3 }, plan.OutputWidth, plan.OutputHeight);
    }
}

