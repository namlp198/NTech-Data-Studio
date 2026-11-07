using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Application.Datasets;

/// <summary>
/// Divides images into train, val and test. The result depends only on the items,
/// the config and the seed, never on the order the items arrive in.
/// </summary>
public static class SplitService
{
    private static readonly SplitKind[] Kinds = { SplitKind.Train, SplitKind.Val, SplitKind.Test };

    private sealed class Unit
    {
        public required string Key { get; init; }
        public required List<long> ImageIds { get; init; }
        public required HashSet<long> Classes { get; init; }
        public int Split { get; set; }
    }

    public static SplitResult Assign(IReadOnlyList<SplitItem> items, SplitConfig config, long seed)
    {
        if (config.Validate() is { } error)
            throw new ArgumentException(error, nameof(config));

        var shares = new[] { config.TrainPercent, config.ValPercent, config.TestPercent }
            .Select(percent => percent / 100.0)
            .ToArray();

        var units = items
            .GroupBy(item => item.UnitKey, StringComparer.Ordinal)
            .Select(group => new Unit
            {
                Key = group.Key,
                ImageIds = group.Select(item => item.ImageId).OrderBy(id => id).ToList(),
                Classes = group.SelectMany(item => item.ClassIds).ToHashSet()
            })
            .OrderBy(unit => unit.Key, StringComparer.Ordinal)
            .ToList();

        var unitsPerClass = new Dictionary<long, int>();
        foreach (var unit in units)
        {
            foreach (var classId in unit.Classes)
                unitsPerClass[classId] = unitsPerClass.GetValueOrDefault(classId) + 1;
        }

        GreedyAssign(units, unitsPerClass, shares, config.Stratify, seed);
        if (config.Stratify)
            Repair(units, unitsPerClass, shares, seed);

        var assignments = new Dictionary<long, SplitKind>();
        foreach (var unit in units)
        {
            foreach (var imageId in unit.ImageIds)
                assignments[imageId] = Kinds[unit.Split];
        }

        return new SplitResult(assignments, config.Stratify ? FindUnplaced(units, unitsPerClass, shares) : Array.Empty<(long, SplitKind)>());
    }

    private static void GreedyAssign(
        List<Unit> units,
        Dictionary<long, int> unitsPerClass,
        double[] shares,
        bool stratify,
        long seed)
    {
        // A unit belongs to the stratum of its rarest class, so rare classes are spread first and best.
        var strata = units
            .GroupBy(unit => stratify ? StratumOf(unit, unitsPerClass) : "all", StringComparer.Ordinal)
            .OrderBy(group => group.Key == "none" ? 1 : 0)
            .ThenBy(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var globalCounts = new double[3];
        var globalTotal = 0.0;
        foreach (var stratum in strata)
        {
            var members = stratum.ToList();
            DeterministicRandom.From(seed, DeterministicRandom.Hash(stratum.Key)).Shuffle(members);

            var stratumCounts = new double[3];
            var stratumTotal = 0.0;
            foreach (var unit in members)
            {
                var best = -1;
                var bestScore = double.NegativeInfinity;
                for (var split = 0; split < 3; split++)
                {
                    if (shares[split] <= 0) continue;

                    // Distance below the target share inside this stratum, plus a light pull toward the global target.
                    var score = shares[split] * stratumTotal - stratumCounts[split]
                                + 0.05 * (shares[split] * globalTotal - globalCounts[split])
                                + shares[split] * 1e-6;
                    if (score <= bestScore) continue;
                    best = split;
                    bestScore = score;
                }

                unit.Split = best;
                var size = unit.ImageIds.Count;
                stratumCounts[best] += size;
                stratumTotal += size;
                globalCounts[best] += size;
                globalTotal += size;
            }
        }
    }

    private static string StratumOf(Unit unit, Dictionary<long, int> unitsPerClass)
    {
        if (unit.Classes.Count == 0) return "none";
        var rarest = unit.Classes
            .OrderBy(classId => unitsPerClass[classId])
            .ThenBy(classId => classId)
            .First();
        return $"class:{rarest}";
    }

    /// <summary>
    /// Greedy placing can still leave a class out of val or train. Move the smallest unit
    /// that fixes it without emptying another class from the split it leaves.
    /// </summary>
    private static void Repair(List<Unit> units, Dictionary<long, int> unitsPerClass, double[] shares, long seed)
    {
        var presence = new Dictionary<long, int>[3];
        for (var split = 0; split < 3; split++)
            presence[split] = new Dictionary<long, int>();
        foreach (var unit in units)
        {
            foreach (var classId in unit.Classes)
                presence[unit.Split][classId] = presence[unit.Split].GetValueOrDefault(classId) + 1;
        }

        var required = new List<int>();
        if (shares[(int)SplitKind.Val] > 0) required.Add((int)SplitKind.Val);
        required.Add((int)SplitKind.Train);

        foreach (var classId in unitsPerClass.Keys.OrderBy(id => unitsPerClass[id]).ThenBy(id => id))
        {
            if (unitsPerClass[classId] < 2) continue;

            foreach (var target in required)
            {
                if (presence[target].GetValueOrDefault(classId) > 0) continue;

                var candidate = units
                    .Where(unit => unit.Split != target
                                   && unit.Classes.Contains(classId)
                                   && shares[unit.Split] > 0
                                   && unit.Classes.All(other =>
                                       unitsPerClass[other] < 2 || presence[unit.Split].GetValueOrDefault(other) >= 2))
                    .OrderBy(unit => unit.Split == (int)SplitKind.Test ? 1 : 0)
                    .ThenBy(unit => unit.ImageIds.Count)
                    .ThenBy(unit => DeterministicRandom.Hash(unit.Key) ^ seed)
                    .FirstOrDefault();
                if (candidate is null) continue;

                foreach (var other in candidate.Classes)
                {
                    presence[candidate.Split][other]--;
                    presence[target][other] = presence[target].GetValueOrDefault(other) + 1;
                }

                candidate.Split = target;
            }
        }
    }

    private static List<(long ClassId, SplitKind Missing)> FindUnplaced(
        List<Unit> units,
        Dictionary<long, int> unitsPerClass,
        double[] shares)
    {
        var inSplit = new HashSet<long>[3];
        for (var split = 0; split < 3; split++)
            inSplit[split] = units.Where(unit => unit.Split == split).SelectMany(unit => unit.Classes).ToHashSet();

        var unplaced = new List<(long, SplitKind)>();
        foreach (var classId in unitsPerClass.Keys.OrderBy(id => id))
        {
            if (!inSplit[(int)SplitKind.Train].Contains(classId))
                unplaced.Add((classId, SplitKind.Train));
            if (shares[(int)SplitKind.Val] > 0 && !inSplit[(int)SplitKind.Val].Contains(classId))
                unplaced.Add((classId, SplitKind.Val));
        }

        return unplaced;
    }
}





