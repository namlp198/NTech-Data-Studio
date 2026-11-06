using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Prepare;

public class CleanRulesTests
{
    // Hashes far apart from each other unless a test sets them close.
    private static SourceImage Image(
        long id,
        string? sha = null,
        ulong? hash = null,
        double blur = 100,
        double dark = 0,
        double bright = 0,
        bool keep = false,
        RejectReason? reject = null) =>
        new(
            id, $"{id}.png", $"{id}.png", false, 1, 1, "PNG", 10, 10, 8, 3, null,
            sha ?? $"sha{id}",
            hash ?? Spread(id),
            hash ?? Spread(id),
            blur, dark, bright, reject, null, keep);

    private static ulong Spread(long id) => unchecked((ulong)id * 0x9E3779B97F4A7C15UL);

    [Fact]
    public void CleanImages_ProduceNoDecisions()
    {
        Assert.Empty(CleanRules.Evaluate(new[] { Image(1), Image(2), Image(3) }, new CleanOptions()));
    }

    [Fact]
    public void ExactDuplicates_KeepTheFirst()
    {
        var decisions = CleanRules.Evaluate(new[] { Image(1, "x"), Image(2, "x"), Image(3, "x") }, new CleanOptions());

        Assert.Equal(new long[] { 2, 3 }, decisions.Select(decision => decision.SourceId));
        Assert.All(decisions, decision => Assert.Equal(RejectReason.ExactDuplicate, decision.Reason));
        Assert.Contains("1.png", decisions[0].Detail);
    }

    [Fact]
    public void ExactDuplicates_KeepTheOneTheUserRestored()
    {
        var decisions = CleanRules.Evaluate(new[] { Image(1, "x"), Image(2, "x", keep: true) }, new CleanOptions());

        Assert.Equal(1, Assert.Single(decisions).SourceId);
    }

    [Fact]
    public void NearDuplicates_KeepTheSharpest()
    {
        var decisions = CleanRules.Evaluate(
            new[] { Image(1, hash: 0b1111, blur: 50), Image(2, hash: 0b0111, blur: 90), Image(3, hash: 0b0011, blur: 70) },
            new CleanOptions());

        Assert.Equal(new long[] { 3, 1 }, decisions.Select(decision => decision.SourceId));
        Assert.All(decisions, decision => Assert.Equal(RejectReason.NearDuplicate, decision.Reason));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, false)]
    public void NearDuplicates_UseTheThresholdInclusively(int threshold, bool rejected)
    {
        // Five bits apart.
        var decisions = CleanRules.Evaluate(
            new[] { Image(1, hash: 0b00000), Image(2, hash: 0b11111) },
            new CleanOptions(NearDuplicateMaxDistance: threshold));

        Assert.Equal(rejected, decisions.Count == 1);
    }

    [Fact]
    public void NearDuplicates_MatchOnEitherHash()
    {
        var first = Image(1) with { PHash = 0, DHash = 0 };
        var second = Image(2) with { PHash = ulong.MaxValue, DHash = 1 };

        Assert.Single(CleanRules.Evaluate(new[] { first, second }, new CleanOptions()));
    }

    [Fact]
    public void Blur_IsOffByDefaultAndRejectsBelowTheThresholdWhenOn()
    {
        var images = new[] { Image(1, blur: 10), Image(2, blur: 30) };

        Assert.Empty(CleanRules.Evaluate(images, new CleanOptions(BlurThreshold: 20)));

        var decision = Assert.Single(CleanRules.Evaluate(images, new CleanOptions(RejectBlurry: true, BlurThreshold: 20)));
        Assert.Equal((1, RejectReason.Blurry), (decision.SourceId, decision.Reason));
    }

    [Fact]
    public void Exposure_RejectsTooDarkAndTooBright()
    {
        var decisions = CleanRules.Evaluate(new[] { Image(1, dark: 0.95), Image(2, bright: 0.6), Image(3, dark: 0.5, bright: 0.2) }, new CleanOptions());

        Assert.Equal(
            new[] { (1L, RejectReason.TooDark), (2L, RejectReason.TooBright) },
            decisions.Select(decision => (decision.SourceId, decision.Reason)));
    }

    [Fact]
    public void RejectedImages_AreNotUsedAsTheOriginalOfANearDuplicate()
    {
        var dark = Image(1, hash: 0, dark: 0.99, blur: 500);
        var fine = Image(2, hash: 1, blur: 10);

        var decision = Assert.Single(CleanRules.Evaluate(new[] { dark, fine }, new CleanOptions()));

        Assert.Equal(1, decision.SourceId);
    }

    [Fact]
    public void RestoredImages_AreNeverRejectedAgain()
    {
        var restored = Image(1, dark: 0.99, blur: 1, keep: true);

        Assert.Empty(CleanRules.Evaluate(new[] { restored }, new CleanOptions(RejectBlurry: true, BlurThreshold: 50)));
    }

    [Fact]
    public void SkipsCorruptAndUnanalyzedImages()
    {
        var corrupt = Image(1, reject: RejectReason.Corrupt) with { Sha256 = "x" };
        var unanalyzed = Image(2) with { Sha256 = null, PHash = null, DHash = null };

        Assert.Empty(CleanRules.Evaluate(new[] { corrupt, unanalyzed, Image(3, "x") }, new CleanOptions()));
    }

    [Fact]
    public void AllRules_CanBeSwitchedOff()
    {
        var images = new[] { Image(1, "x", hash: 0, dark: 1, bright: 1), Image(2, "x", hash: 0) };
        var off = new CleanOptions(RejectExactDuplicates: false, RejectNearDuplicates: false, RejectDark: false, RejectBright: false);

        Assert.Empty(CleanRules.Evaluate(images, off));
    }

    [Fact]
    public void SuggestBlurThreshold_IsTheFifthPercentile()
    {
        var images = Enumerable.Range(1, 100).Select(score => Image(score, blur: score));

        Assert.Equal(5, CleanRules.SuggestBlurThreshold(images));
        Assert.Equal(0, CleanRules.SuggestBlurThreshold(Array.Empty<SourceImage>()));
    }
}
