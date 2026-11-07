using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Datasets;

/// <summary>Applies the class remap of a <see cref="PreprocessConfig"/>.</summary>
public sealed class ClassMapper
{
    private readonly Dictionary<long, long?> _map;

    public ClassMapper(IEnumerable<ClassMapEntry> entries)
    {
        _map = entries.ToDictionary(entry => entry.ClassId, entry => entry.TargetClassId);
    }

    public bool HasChanges => _map.Count > 0;

    /// <summary>The class a label ends up in, or null when its class is dropped.</summary>
    public long? Resolve(long classId) => _map.TryGetValue(classId, out var target) ? target : classId;
}

/// <summary>Works out which images a version holds and in which split, and what the result looks like.</summary>
public static class VersionBuilder
{
    /// <summary>Fewer labels than this in train is too little for a model to learn the class from.</summary>
    public const int MinTrainLabels = 10;

    private const int MinGroups = 3;
    private const int RatioToleranceReportedFrom = 10;
    private const double RatioTolerancePoints = 10;

    public static VersionPlan Build(IReadOnlyList<DatasetImage> images, IReadOnlyList<LabelClass> classes, DatasetConfig config)
    {
        if (config.Validate() is { } error)
            throw new ArgumentException(error, nameof(config));

        var warnings = new List<string>();
        var mapper = new ClassMapper(config.Preprocess.ClassMap);

        var eligible = new List<DatasetImage>();
        var excluded = 0;
        foreach (var image in images)
        {
            if (image.Status == ImageStatus.Rejected) continue;
            if (image.Status == ImageStatus.New && !config.Split.IncludeUnlabeled)
                excluded++;
            else
                eligible.Add(image);
        }

        var effective = eligible.ToDictionary(image => image.Id, image => Remap(image, mapper));
        var kept = DropNull(eligible, effective, config, out var dropped);

        var items = BuildItems(kept, effective, config.Split, warnings);
        var split = SplitService.Assign(items, config.Split, config.Seed);

        var assignments = kept
            .OrderBy(image => image.Id)
            .Select(image => new SplitAssignment(image.Id, split.Assignments[image.Id]))
            .ToList();

        var counts = CountClasses(kept, effective, split.Assignments, classes, mapper);
        var imagesPerSplit = new int[3];
        var labelsPerSplit = new int[3];
        var empty = 0;
        foreach (var image in kept)
        {
            var kind = (int)split.Assignments[image.Id];
            imagesPerSplit[kind]++;
            labelsPerSplit[kind] += effective[image.Id].Values.Sum();
            if (effective[image.Id].Count == 0) empty++;
        }

        AddWarnings(warnings, config, counts, split, classes, imagesPerSplit, kept.Count);

        var trainOutput = imagesPerSplit[(int)SplitKind.Train] * (config.Augment.IsEnabled ? config.Augment.Multiplier : 1);
        return new VersionPlan(
            assignments,
            counts,
            imagesPerSplit,
            labelsPerSplit,
            excluded,
            dropped,
            empty,
            items.Select(item => item.UnitKey).Distinct().Count(),
            trainOutput,
            warnings);
    }

    /// <summary>Label count per class after remapping; annotations and tags both count, as in the class panel.</summary>
    private static Dictionary<long, int> Remap(DatasetImage image, ClassMapper mapper)
    {
        var counts = new Dictionary<long, int>();
        foreach (var (classId, count) in image.LabelsByClass)
        {
            if (mapper.Resolve(classId) is { } target)
                counts[target] = counts.GetValueOrDefault(target) + count;
        }

        foreach (var classId in image.TagClassIds)
        {
            if (mapper.Resolve(classId) is { } target)
                counts[target] = counts.GetValueOrDefault(target) + 1;
        }

        return counts;
    }

    private static List<DatasetImage> DropNull(
        List<DatasetImage> eligible,
        Dictionary<long, Dictionary<long, int>> effective,
        DatasetConfig config,
        out int dropped)
    {
        dropped = 0;
        var percent = config.Preprocess.DropNullPercent;
        if (percent <= 0) return eligible;

        var empties = eligible
            .Where(image => effective[image.Id].Count == 0)
            .OrderBy(image => DeterministicRandom.From(config.Seed, image.Id, 1).NextUInt64())
            .ThenBy(image => image.Id)
            .ToList();
        var toDrop = (int)Math.Round(empties.Count * percent / 100.0, MidpointRounding.AwayFromZero);
        var removed = empties.Take(toDrop).Select(image => image.Id).ToHashSet();
        dropped = removed.Count;
        return eligible.Where(image => !removed.Contains(image.Id)).ToList();
    }

    private static List<SplitItem> BuildItems(
        List<DatasetImage> kept,
        Dictionary<long, Dictionary<long, int>> effective,
        SplitConfig config,
        List<string> warnings)
    {
        var useGroups = config.GroupByFolder;
        if (useGroups)
        {
            var groupCount = kept
                .Select(image => image.GroupKey is { } key ? "g:" + key : "s:" + image.SourceImageId)
                .Distinct()
                .Count();
            if (groupCount < MinGroups)
            {
                useGroups = false;
                if (kept.Count >= MinGroups)
                    warnings.Add($"Chỉ có {groupCount} nhóm thư mục nên không chia theo nhóm được; đã chia theo ảnh gốc. Ảnh cắt từ cùng một ảnh gốc vẫn nằm chung một tập.");
            }
        }

        // Tiles of one source always stay together: split apart, a val tile would be a near copy of a train tile.
        return kept
            .Select(image => new SplitItem(
                image.Id,
                useGroups && image.GroupKey is { } key ? "g:" + key : "s:" + image.SourceImageId,
                effective[image.Id].Keys.ToHashSet()))
            .ToList();
    }

    private static List<ClassSplitCount> CountClasses(
        List<DatasetImage> kept,
        Dictionary<long, Dictionary<long, int>> effective,
        IReadOnlyDictionary<long, SplitKind> assignments,
        IReadOnlyList<LabelClass> classes,
        ClassMapper mapper)
    {
        var byId = classes.ToDictionary(labelClass => labelClass.Id);
        var counts = new Dictionary<long, ClassSplitCount>();
        foreach (var image in kept)
        {
            var kind = (int)assignments[image.Id];
            foreach (var (classId, count) in effective[image.Id])
            {
                if (!counts.TryGetValue(classId, out var row))
                {
                    var name = byId.TryGetValue(classId, out var labelClass) ? labelClass.Name : $"#{classId}";
                    counts[classId] = row = new ClassSplitCount(classId, name, new int[3], new int[3]);
                }

                row.Images[kind]++;
                row.Labels[kind] += count;
            }
        }

        // Classes that exist but have no label still get a row, so the table shows what is missing.
        foreach (var labelClass in classes.OrderBy(labelClass => labelClass.Order).ThenBy(labelClass => labelClass.Id))
        {
            // A class that is merged into another or dropped is gone from this version, so it gets no row.
            if (!counts.ContainsKey(labelClass.Id) && mapper.Resolve(labelClass.Id) == labelClass.Id)
                counts[labelClass.Id] = new ClassSplitCount(labelClass.Id, labelClass.Name, new int[3], new int[3]);
        }

        var order = classes.ToDictionary(labelClass => labelClass.Id, labelClass => (labelClass.Order, labelClass.Id));
        return counts.Values
            .OrderBy(row => order.TryGetValue(row.ClassId, out var key) ? key.Order : int.MaxValue)
            .ThenBy(row => row.ClassId)
            .ToList();
    }

    private static void AddWarnings(
        List<string> warnings,
        DatasetConfig config,
        List<ClassSplitCount> counts,
        SplitResult split,
        IReadOnlyList<LabelClass> classes,
        int[] imagesPerSplit,
        int total)
    {
        if (total == 0)
        {
            warnings.Add("Chưa có ảnh nào đủ điều kiện. Hãy gán nhãn (hoặc đánh dấu Null) trước khi tạo version.");
            return;
        }

        var names = classes.ToDictionary(labelClass => labelClass.Id, labelClass => labelClass.Name);
        foreach (var (classId, missing) in split.UnplacedClasses)
        {
            var name = names.GetValueOrDefault(classId, $"#{classId}");
            warnings.Add(missing == SplitKind.Val
                ? $"Class \"{name}\" không có trong tập val: quá ít ảnh hay nhóm để chia vào cả train và val."
                : $"Class \"{name}\" không có trong tập train.");
        }

        foreach (var row in counts)
        {
            var train = row.Labels[(int)SplitKind.Train];
            if (row.TotalLabels > 0 && train < MinTrainLabels)
                warnings.Add($"Class \"{row.Name}\" chỉ có {train} nhãn trong train (nên từ {MinTrainLabels} trở lên).");
            else if (row.TotalLabels == 0)
                warnings.Add($"Class \"{row.Name}\" chưa có nhãn nào trong version này.");
        }

        if (total < RatioToleranceReportedFrom) return;

        var targets = new[] { config.Split.TrainPercent, config.Split.ValPercent, config.Split.TestPercent };
        var labels = new[] { "train", "val", "test" };
        for (var index = 0; index < 3; index++)
        {
            var actual = 100.0 * imagesPerSplit[index] / total;
            if (Math.Abs(actual - targets[index]) > RatioTolerancePoints)
                warnings.Add($"Tập {labels[index]} thực tế chiếm {actual:0}% (mục tiêu {targets[index]}%): các nhóm ảnh không chia đều được hơn.");
        }
    }
}
