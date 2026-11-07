using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Datasets;

public class VersionBuilderTests
{
    private static readonly IReadOnlyList<LabelClass> Classes = new[]
    {
        new LabelClass(1, "connector_red", "#E25D5D", 1, 0),
        new LabelClass(2, "connector_blue", "#4A90E2", 2, 1),
        new LabelClass(3, "scratch", "#FF9F43", 3, 2)
    };

    private static DatasetImage Image(
        long id,
        long classId,
        int labels = 1,
        ImageStatus status = ImageStatus.Labeled,
        string? group = null,
        long? source = null) =>
        new(
            id,
            source ?? id,
            group ?? $"folder{id}",
            $"master/m{id}.jpg",
            640,
            480,
            status,
            classId == 0 ? new Dictionary<long, int>() : new Dictionary<long, int> { [classId] = labels },
            new HashSet<long>());

    private static List<DatasetImage> Many(int count, Func<int, long> classOf, Func<int, ImageStatus>? status = null) =>
        Enumerable.Range(1, count).Select(id => Image(id, classOf(id), status: status?.Invoke(id) ?? ImageStatus.Labeled)).ToList();

    [Fact]
    public void Build_SkipsImagesStillNewAndCountsThem()
    {
        var images = Many(100, id => 1, id => id <= 10 ? ImageStatus.New : ImageStatus.Labeled);

        var plan = VersionBuilder.Build(images, Classes, new DatasetConfig());

        Assert.Equal(90, plan.TotalImages);
        Assert.Equal(10, plan.ExcludedUnlabeled);
    }

    [Fact]
    public void Build_CanIncludeUnlabeledImages()
    {
        var images = Many(40, id => 0, id => ImageStatus.New);
        var config = new DatasetConfig { Split = new SplitConfig { IncludeUnlabeled = true } };

        var plan = VersionBuilder.Build(images, Classes, config);

        Assert.Equal(40, plan.TotalImages);
        Assert.Equal(40, plan.EmptyImages);
    }

    [Fact]
    public void Build_NullImagesAreInTheVersionAsBackground()
    {
        var images = Many(60, id => id <= 50 ? 1 : 0, id => id <= 50 ? ImageStatus.Labeled : ImageStatus.Null);

        var plan = VersionBuilder.Build(images, Classes, new DatasetConfig());

        Assert.Equal(60, plan.TotalImages);
        Assert.Equal(10, plan.EmptyImages);
    }

    [Fact]
    public void Build_RejectedImagesNeverEnter()
    {
        var images = Many(30, id => 1);
        images.Add(Image(99, 1, status: ImageStatus.Rejected));

        var plan = VersionBuilder.Build(images, Classes, new DatasetConfig());

        Assert.Equal(30, plan.TotalImages);
        Assert.DoesNotContain(plan.Assignments, assignment => assignment.ImageId == 99);
    }

    [Fact]
    public void Build_DropNullRemovesTheRequestedShareOfEmptyImagesOnly()
    {
        var images = Many(120, id => id <= 80 ? 1 : 0, id => id <= 80 ? ImageStatus.Labeled : ImageStatus.Null);
        var config = new DatasetConfig { Preprocess = new PreprocessConfig { DropNullPercent = 50 } };

        var plan = VersionBuilder.Build(images, Classes, config);

        Assert.Equal(20, plan.DroppedNull);
        Assert.Equal(100, plan.TotalImages);
        Assert.Equal(80, plan.Assignments.Count(assignment => assignment.ImageId <= 80));
    }

    [Fact]
    public void Build_DropNullPicksTheSameImagesEveryTime()
    {
        var images = Many(100, id => 0, id => ImageStatus.Null);
        var config = new DatasetConfig { Preprocess = new PreprocessConfig { DropNullPercent = 30 } };

        var first = VersionBuilder.Build(images, Classes, config).Assignments.Select(a => a.ImageId);
        var second = VersionBuilder.Build(images, Classes, config).Assignments.Select(a => a.ImageId);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_MergedClassesCountAsTheTargetAndLoseTheirOwnRow()
    {
        var images = Many(100, id => id % 2 == 0 ? 1 : 2);
        var config = new DatasetConfig
        {
            Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(2, 1) } }
        };

        var plan = VersionBuilder.Build(images, Classes, config);

        Assert.DoesNotContain(plan.Classes, row => row.ClassId == 2);
        Assert.Equal(100, plan.Classes.Single(row => row.ClassId == 1).TotalLabels);
    }

    [Fact]
    public void Build_DroppedClassLeavesNoLabelsAndMayEmptyAnImage()
    {
        var images = Many(60, id => id <= 40 ? 1 : 3);
        var config = new DatasetConfig
        {
            Preprocess = new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(3, null) } }
        };

        var plan = VersionBuilder.Build(images, Classes, config);

        Assert.DoesNotContain(plan.Classes, row => row.ClassId == 3);
        Assert.Equal(20, plan.EmptyImages);
        Assert.Equal(40, plan.LabelsPerSplit.Sum());
    }

    [Fact]
    public void Build_ClassesWithoutAnyLabelStillGetARowAndAWarning()
    {
        var plan = VersionBuilder.Build(Many(50, id => 1), Classes, new DatasetConfig());

        var unused = plan.Classes.Single(row => row.ClassId == 3);
        Assert.Equal(0, unused.TotalLabels);
        Assert.Contains(plan.Warnings, warning => warning.Contains("scratch"));
    }

    [Fact]
    public void Build_ListsClassesInTheOrderOfTheClassPanel()
    {
        var plan = VersionBuilder.Build(Many(50, id => 1), Classes, new DatasetConfig());

        Assert.Equal(new long[] { 1, 2, 3 }, plan.Classes.Select(row => row.ClassId));
    }

    [Fact]
    public void Build_WarnsWhenAClassHasTooFewTrainLabels()
    {
        var images = Many(100, id => id <= 6 ? 3 : 1);

        var plan = VersionBuilder.Build(images, Classes, new DatasetConfig());

        Assert.Contains(plan.Warnings, warning => warning.Contains("scratch") && warning.Contains("train"));
    }

    [Fact]
    public void Build_FallsBackToPerImageWhenThereAreTooFewFolders()
    {
        var images = Enumerable.Range(1, 60).Select(id => Image(id, 1, group: "all")).ToList();

        var plan = VersionBuilder.Build(images, Classes, new DatasetConfig());

        Assert.InRange(plan.ImagesPerSplit[(int)SplitKind.Val], 10, 14);
        Assert.Contains(plan.Warnings, warning => warning.Contains("thư mục"));
    }

    [Fact]
    public void Build_UsesFoldersAsGroupsWhenThereAreEnough()
    {
        var images = Enumerable.Range(1, 60).Select(id => Image(id, 1, group: $"day{(id - 1) / 6}")).ToList();

        var plan = VersionBuilder.Build(images, Classes, new DatasetConfig());

        var splitOf = plan.Assignments.ToDictionary(a => a.ImageId, a => a.Split);
        foreach (var day in images.GroupBy(image => image.GroupKey))
            Assert.Single(day.Select(image => splitOf[image.Id]).Distinct());
        Assert.Equal(10, plan.UnitCount);
    }

    [Fact]
    public void Build_TilesOfOneSourceAlwaysShareASplit()
    {
        // Ten sources, each cut into four tiles, no folder grouping.
        var images = Enumerable.Range(1, 40)
            .Select(id => Image(id, 1, group: null, source: (id - 1) / 4))
            .Select(image => image with { GroupKey = null })
            .ToList();
        var config = new DatasetConfig { Split = new SplitConfig { GroupByFolder = false } };

        var plan = VersionBuilder.Build(images, Classes, config);

        var splitOf = plan.Assignments.ToDictionary(a => a.ImageId, a => a.Split);
        foreach (var source in images.GroupBy(image => image.SourceImageId))
            Assert.Single(source.Select(image => splitOf[image.Id]).Distinct());
    }

    [Fact]
    public void Build_TrainOutputIsTheTrainCountTimesTheMultiplier()
    {
        // The design's own example: 300 images, 70/20/10, x3 on train.
        var config = new DatasetConfig
        {
            Split = new SplitConfig { GroupByFolder = false },
            Augment = new AugmentConfig { Multiplier = 3 }
        };

        var plan = VersionBuilder.Build(Many(300, id => id % 2 + 1), Classes, config);

        var train = plan.ImagesPerSplit[(int)SplitKind.Train];
        Assert.Equal(train * 3, plan.ExpectedTrainOutput);
        Assert.Equal(train * 3 + plan.ImagesPerSplit[1] + plan.ImagesPerSplit[2], plan.ExpectedTotalOutput);
        Assert.InRange(plan.ExpectedTotalOutput, 715, 725);
    }

    [Fact]
    public void Build_AugmentationOffLeavesTheTrainCountAlone()
    {
        var plan = VersionBuilder.Build(Many(100, id => 1), Classes, new DatasetConfig());

        Assert.Equal(plan.ImagesPerSplit[0], plan.ExpectedTrainOutput);
    }

    [Fact]
    public void Build_WarnsWhenThereIsNothingToBuild()
    {
        var plan = VersionBuilder.Build(Many(5, id => 1, id => ImageStatus.New), Classes, new DatasetConfig());

        Assert.Empty(plan.Assignments);
        Assert.Single(plan.Warnings);
    }

    [Fact]
    public void Build_LabelCountsAddAnnotationsAndTags()
    {
        var image = Image(1, 1, labels: 4) with { TagClassIds = new HashSet<long> { 2 } };

        var plan = VersionBuilder.Build(new[] { image }, Classes, new DatasetConfig());

        Assert.Equal(4, plan.Classes.Single(row => row.ClassId == 1).TotalLabels);
        Assert.Equal(1, plan.Classes.Single(row => row.ClassId == 2).TotalLabels);
    }

    [Fact]
    public void Build_RefusesAnInvalidConfig()
    {
        Assert.Throws<ArgumentException>(() =>
            VersionBuilder.Build(Many(10, id => 1), Classes, new DatasetConfig { Split = new SplitConfig { TrainPercent = 10 } }));
    }
}
