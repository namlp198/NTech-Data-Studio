using System.Numerics;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Prepare;

/// <summary>Each rule can be switched off. Blur is off by default: its threshold is relative to the batch, so turning it on always rejects some images.</summary>
public sealed record CleanOptions(
    bool RejectExactDuplicates = true,
    bool RejectNearDuplicates = true,
    int NearDuplicateMaxDistance = 5,
    bool RejectBlurry = false,
    double BlurThreshold = 0,
    bool RejectDark = true,
    double DarkMaxFraction = 0.9,
    bool RejectBright = true,
    double BrightMaxFraction = 0.5);

public sealed record CleanDecision(long SourceId, RejectReason Reason, string Detail);

public static class CleanRules
{
    /// <summary>The 5th percentile of the batch: a starting point for the user, not a verdict.</summary>
    public static double SuggestBlurThreshold(IEnumerable<SourceImage> sources)
    {
        var scores = sources.Where(source => source.BlurScore is not null).Select(source => source.BlurScore!.Value).Order().ToList();
        return scores.Count == 0 ? 0 : scores[(int)Math.Floor((scores.Count - 1) * 0.05)];
    }

    public static IReadOnlyList<CleanDecision> Evaluate(IReadOnlyCollection<SourceImage> sources, CleanOptions options)
    {
        var decisions = new List<CleanDecision>();
        var candidates = sources
            .Where(source => source.IsAnalyzed && source.Reject != RejectReason.Corrupt)
            .OrderBy(source => source.Id)
            .ToList();

        var survivors = new List<SourceImage>();
        var firstByHash = options.RejectExactDuplicates
            ? candidates
                .GroupBy(source => source.Sha256!)
                .ToDictionary(group => group.Key, group => group.FirstOrDefault(source => source.KeepOverride) ?? group.First())
            : null;

        foreach (var source in candidates)
        {
            var decision = source.KeepOverride ? null : SingleImageDecision(source, options, firstByHash);
            if (decision is null)
                survivors.Add(source);
            else
                decisions.Add(decision);
        }

        if (options.RejectNearDuplicates)
            decisions.AddRange(NearDuplicates(survivors, options.NearDuplicateMaxDistance));

        return decisions;
    }

    private static CleanDecision? SingleImageDecision(
        SourceImage source,
        CleanOptions options,
        IReadOnlyDictionary<string, SourceImage>? firstByHash)
    {
        if (firstByHash is not null && firstByHash[source.Sha256!] is var first && first.Id != source.Id)
            return new CleanDecision(source.Id, RejectReason.ExactDuplicate, $"Giống hệt {first.FileName}");

        if (options.RejectBlurry && source.BlurScore < options.BlurThreshold)
            return new CleanDecision(source.Id, RejectReason.Blurry, $"Độ nét {source.BlurScore:F1}, dưới ngưỡng {options.BlurThreshold:F1}");

        if (options.RejectDark && source.DarkFraction >= options.DarkMaxFraction)
            return new CleanDecision(source.Id, RejectReason.TooDark, $"{source.DarkFraction:P0} điểm ảnh quá tối");

        if (options.RejectBright && source.BrightFraction >= options.BrightMaxFraction)
            return new CleanDecision(source.Id, RejectReason.TooBright, $"{source.BrightFraction:P0} điểm ảnh cháy sáng");

        return null;
    }

    /// <summary>
    /// Keeps the sharpest image of each near-duplicate family. Either hash
    /// matching counts: missing a duplicate is worse here, because every
    /// rejection goes to a list the user reviews and can restore from.
    /// </summary>
    private static IEnumerable<CleanDecision> NearDuplicates(IReadOnlyList<SourceImage> sources, int maxDistance)
    {
        var kept = sources.Where(source => source.KeepOverride).ToList();
        var others = sources
            .Where(source => !source.KeepOverride)
            .OrderByDescending(source => source.BlurScore ?? 0)
            .ThenBy(source => source.Id);

        foreach (var source in others)
        {
            SourceImage? twin = null;
            var closest = int.MaxValue;
            foreach (var other in kept)
            {
                var distance = Distance(source, other);
                if (distance <= maxDistance && distance < closest)
                {
                    twin = other;
                    closest = distance;
                }
            }

            if (twin is null)
                kept.Add(source);
            else
                yield return new CleanDecision(source.Id, RejectReason.NearDuplicate, $"Gần giống {twin.FileName} (khoảng cách {closest})");
        }
    }
    private static int Distance(SourceImage first, SourceImage second)
    {
        var perceptual = BitOperations.PopCount(first.PHash!.Value ^ second.PHash!.Value);
        var difference = BitOperations.PopCount(first.DHash!.Value ^ second.DHash!.Value);
        return Math.Min(perceptual, difference);
    }
}
