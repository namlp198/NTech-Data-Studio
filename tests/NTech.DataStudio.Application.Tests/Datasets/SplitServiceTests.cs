using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Application.Tests.Datasets;

public class SplitServiceTests
{
    private static readonly HashSet<long> NoClasses = new();

    private static List<SplitItem> Singles(int count, Func<int, long[]>? classesOf = null) =>
        Enumerable.Range(1, count)
            .Select(id => new SplitItem(id, $"s:{id}", (classesOf?.Invoke(id) ?? Array.Empty<long>()).ToHashSet()))
            .ToList();

    private static int Count(SplitResult result, SplitKind kind) => result.Assignments.Values.Count(split => split == kind);

    private static HashSet<long> ClassesIn(SplitResult result, IEnumerable<SplitItem> items, SplitKind kind) =>
        items.Where(item => result.Assignments[item.ImageId] == kind).SelectMany(item => item.ClassIds).ToHashSet();

    [Fact]
    public void Assign_FollowsTheRequestedRatio()
    {
        var result = SplitService.Assign(Singles(300), new SplitConfig(), seed: 1);

        Assert.InRange(Count(result, SplitKind.Train), 207, 213);
        Assert.InRange(Count(result, SplitKind.Val), 57, 63);
        Assert.InRange(Count(result, SplitKind.Test), 27, 33);
        Assert.Equal(300, result.Assignments.Count);
    }

    [Fact]
    public void Assign_GivesTheSameResultWhateverTheInputOrder()
    {
        var items = Singles(120, id => new long[] { id % 3 + 1 });

        var forward = SplitService.Assign(items, new SplitConfig(), seed: 9);
        var reversed = SplitService.Assign(Enumerable.Reverse(items).ToList(), new SplitConfig(), seed: 9);

        Assert.Equal(forward.Assignments.OrderBy(pair => pair.Key), reversed.Assignments.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Assign_AnotherSeedGivesAnotherSplit()
    {
        var items = Singles(120, id => new long[] { id % 3 + 1 });

        var first = SplitService.Assign(items, new SplitConfig(), seed: 1);
        var second = SplitService.Assign(items, new SplitConfig(), seed: 2);

        Assert.NotEqual(first.Assignments.OrderBy(pair => pair.Key), second.Assignments.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void Assign_KeepsEveryGroupInOneSplit()
    {
        var items = Enumerable.Range(1, 200)
            .Select(id => new SplitItem(id, $"g:{(id - 1) / 5}", new HashSet<long> { id % 2 + 1 }))
            .ToList();

        var result = SplitService.Assign(items, new SplitConfig(), seed: 3);

        foreach (var group in items.GroupBy(item => item.UnitKey))
            Assert.Single(group.Select(item => result.Assignments[item.ImageId]).Distinct());
    }

    [Fact]
    public void Assign_PutsEveryClassInBothTrainAndVal()
    {
        // 200 images of class 1, 60 of class 2, 12 of class 3.
        var items = Singles(272, id => id <= 200 ? new long[] { 1 } : id <= 260 ? new long[] { 2 } : new long[] { 3 });

        var result = SplitService.Assign(items, new SplitConfig(), seed: 4);

        Assert.Empty(result.UnplacedClasses);
        foreach (var kind in new[] { SplitKind.Train, SplitKind.Val })
            Assert.Equal(new HashSet<long> { 1, 2, 3 }, ClassesIn(result, items, kind));
    }

    [Fact]
    public void Assign_KeepsEachClassCloseToTheRatioNotJustTheTotal()
    {
        var items = Singles(400, id => new long[] { id % 4 == 0 ? 2 : 1 });

        var result = SplitService.Assign(items, new SplitConfig(), seed: 5);

        foreach (var classId in new long[] { 1, 2 })
        {
            var ofClass = items.Where(item => item.ClassIds.Contains(classId)).ToList();
            var inTrain = ofClass.Count(item => result.Assignments[item.ImageId] == SplitKind.Train);
            Assert.InRange(100.0 * inTrain / ofClass.Count, 66, 74);
        }
    }

    [Fact]
    public void Assign_RepairsARareClassThatGreedyPlacingLeftOutOfVal()
    {
        // Three classes with two images each: plain proportions would send all of them to train.
        var items = Singles(6, id => new long[] { (id + 1) / 2 });

        var result = SplitService.Assign(items, new SplitConfig(), seed: 6);

        Assert.Empty(result.UnplacedClasses);
        Assert.Equal(new HashSet<long> { 1, 2, 3 }, ClassesIn(result, items, SplitKind.Val));
        Assert.Equal(new HashSet<long> { 1, 2, 3 }, ClassesIn(result, items, SplitKind.Train));
    }

    // Found by search: with these class lists, proportional placing alone leaves a class that has
    // several images out of val. Each string is one image: its classes, comma separated.
    [Theory]
    [InlineData("3|2|3,2|3|2|1")]
    [InlineData("1|2,1|3|1|2|1|2")]
    [InlineData("3|1|2|3,2|3|1,3|2|1|1,3|3")]
    public void Assign_RepairMovesAUnitWhenPlacingLeftAClassOutOfVal(string imageClasses)
    {
        var items = imageClasses.Split('|')
            .Select((classes, index) => new SplitItem(index + 1, $"s:{index + 1}", classes.Split(',').Select(long.Parse).ToHashSet()))
            .ToList();

        var result = SplitService.Assign(items, new SplitConfig(), seed: 1);

        var several = items.SelectMany(item => item.ClassIds).GroupBy(id => id).Where(group => group.Count() >= 2).Select(group => group.Key);
        foreach (var classId in several)
        {
            Assert.Contains(classId, ClassesIn(result, items, SplitKind.Val));
            Assert.Contains(classId, ClassesIn(result, items, SplitKind.Train));
        }

        Assert.DoesNotContain(result.UnplacedClasses, entry => several.Contains(entry.ClassId));
    }

    [Fact]
    public void Assign_ReportsAClassThatHasOnlyOneUnit()
    {
        var items = Singles(50, id => id == 1 ? new long[] { 9 } : new long[] { 1 });

        var result = SplitService.Assign(items, new SplitConfig(), seed: 7);

        Assert.Contains(result.UnplacedClasses, entry => entry.ClassId == 9 && entry.Missing == SplitKind.Val
                                                         || entry.ClassId == 9 && entry.Missing == SplitKind.Train);
    }

    [Fact]
    public void Assign_WithoutATestShareLeavesTestEmpty()
    {
        var config = new SplitConfig { TrainPercent = 80, ValPercent = 20, TestPercent = 0 };

        var result = SplitService.Assign(Singles(100), config, seed: 8);

        Assert.Equal(0, Count(result, SplitKind.Test));
        Assert.InRange(Count(result, SplitKind.Val), 17, 23);
    }

    [Fact]
    public void Assign_WithoutStratifyStillSplitsByTheRatio()
    {
        var config = new SplitConfig { Stratify = false };

        var result = SplitService.Assign(Singles(300, id => new long[] { id % 5 }), config, seed: 8);

        Assert.InRange(Count(result, SplitKind.Train), 205, 215);
        Assert.Empty(result.UnplacedClasses);
    }

    [Fact]
    public void Assign_ImagesWithoutClassesAreSpreadToo()
    {
        var items = Singles(100).Select(item => item with { ClassIds = NoClasses }).ToList();

        var result = SplitService.Assign(items, new SplitConfig(), seed: 2);

        Assert.InRange(Count(result, SplitKind.Val), 17, 23);
    }

    [Fact]
    public void Assign_OneBigGroupCannotBeSplitSoItStaysWhole()
    {
        var items = Enumerable.Range(1, 30).Select(id => new SplitItem(id, "g:all", NoClasses)).ToList();

        var result = SplitService.Assign(items, new SplitConfig(), seed: 2);

        Assert.Single(result.Assignments.Values.Distinct());
    }

    [Fact]
    public void Assign_RefusesAnInvalidConfig()
    {
        Assert.Throws<ArgumentException>(() =>
            SplitService.Assign(Singles(10), new SplitConfig { TrainPercent = 50 }, seed: 1));
    }

    [Fact]
    public void Assign_HandlesNoImages()
    {
        var result = SplitService.Assign(Array.Empty<SplitItem>(), new SplitConfig(), seed: 1);

        Assert.Empty(result.Assignments);
    }
}
