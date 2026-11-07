using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Domain.Tests.Augment;

public class SamplerAndPlannerTests
{
    private static void AssertPoint(double x, double y, NTech.DataStudio.Domain.Geometry.PixelPoint actual)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
    }

    private static AugmentConfig Everything() => new()
    {
        Multiplier = 5,
        FlipHorizontal = true,
        FlipVertical = true,
        Rotate90Clockwise = true,
        Rotate180 = true,
        RotateDegrees = 15,
        ScalePercent = 20,
        TranslatePercent = 10,
        ShearDegrees = 8,
        BrightnessPercent = 20,
        ExposurePercent = 30,
        ContrastPercent = 25,
        GammaPercent = 20,
        HueDegrees = 20,
        SaturationPercent = 30,
        GrayscalePercent = 30,
        BlurPx = 2,
        MotionBlurPx = 9,
        NoisePercent = 4,
        JpegQualityMin = 60,
        CutoutCount = 2,
        CutoutSizePercent = 12
    };

    [Fact]
    public void Sample_TheOriginalCopyIsNeverTouched()
    {
        Assert.Same(AugmentParams.None, AugmentSampler.Sample(Everything(), 42, 7, 0));
    }

    [Fact]
    public void Sample_DoesNothingWhenTheMultiplierIsOne()
    {
        Assert.Same(AugmentParams.None, AugmentSampler.Sample(Everything() with { Multiplier = 1 }, 42, 7, 1));
    }

    [Fact]
    public void Sample_SameSeedImageAndCopyAlwaysGiveTheSameDice()
    {
        var first = AugmentSampler.Sample(Everything(), 42, 7, 2);
        var second = AugmentSampler.Sample(Everything(), 42, 7, 2);

        Assert.Equal(first.RotateDegrees, second.RotateDegrees);
        Assert.Equal(first.Brightness, second.Brightness);
        Assert.Equal(first.NoiseSeed, second.NoiseSeed);
        Assert.Equal(first.Cutouts, second.Cutouts);
    }

    [Fact]
    public void Sample_PinnedValuesDoNotDriftBetweenRuntimeVersions()
    {
        // Guards the random stream itself: if this changes, every saved version would regenerate differently.
        var sampled = AugmentSampler.Sample(Everything(), 42, 7, 1);

        Assert.Equal(2.006765, sampled.RotateDegrees, 6);
    }

    [Fact]
    public void Sample_DifferentCopiesImagesAndSeedsGiveDifferentDice()
    {
        var baseline = AugmentSampler.Sample(Everything(), 42, 7, 1).RotateDegrees;

        Assert.NotEqual(baseline, AugmentSampler.Sample(Everything(), 42, 7, 2).RotateDegrees);
        Assert.NotEqual(baseline, AugmentSampler.Sample(Everything(), 42, 8, 1).RotateDegrees);
        Assert.NotEqual(baseline, AugmentSampler.Sample(Everything(), 43, 7, 1).RotateDegrees);
    }

    [Fact]
    public void Sample_SwitchingOneOptionDoesNotChangeWhatTheOthersRolled()
    {
        var withBlur = AugmentSampler.Sample(Everything(), 42, 7, 1);
        var withoutBlur = AugmentSampler.Sample(Everything() with { BlurPx = 0 }, 42, 7, 1);

        Assert.Equal(withBlur.RotateDegrees, withoutBlur.RotateDegrees);
        Assert.Equal(withBlur.Brightness, withoutBlur.Brightness);
        Assert.Equal(withBlur.NoiseSigma, withoutBlur.NoiseSigma);
        Assert.Equal(0, withoutBlur.BlurSigma);
    }

    [Fact]
    public void Sample_StaysInsideEveryConfiguredRange()
    {
        var config = Everything();
        for (var copy = 1; copy <= 300; copy++)
        {
            var sampled = AugmentSampler.Sample(config, 1, copy, copy);

            Assert.InRange(sampled.RotateDegrees, -15, 15);
            Assert.InRange(sampled.Scale, 0.8, 1.2);
            Assert.InRange(sampled.TranslateX, -0.1, 0.1);
            Assert.InRange(sampled.ShearXDegrees, -8, 8);
            Assert.InRange(sampled.Brightness, -51, 51);
            Assert.InRange(sampled.Exposure, 0.7, 1.3);
            Assert.InRange(sampled.Contrast, 0.75, 1.25);
            Assert.InRange(sampled.Gamma, 0.8, 1.2);
            Assert.InRange(sampled.HueShiftDegrees, -20, 20);
            Assert.InRange(sampled.SaturationScale, 0.7, 1.3);
            Assert.InRange(sampled.BlurSigma, 0, 2);
            Assert.InRange(sampled.MotionBlurLength, 0, 9);
            Assert.InRange(sampled.NoiseSigma, 0, 4 / 100.0 * 255);
            Assert.InRange(sampled.JpegQuality!.Value, 60, 100);
            Assert.Equal(2, sampled.Cutouts.Count);
        }
    }

    [Fact]
    public void Sample_OnlyAppliesTheOptionsThatAreOn()
    {
        var config = new AugmentConfig { Multiplier = 4, FlipHorizontal = true };
        var flips = new List<bool>();
        for (var copy = 1; copy <= 200; copy++)
        {
            var sampled = AugmentSampler.Sample(config, 5, copy, copy);
            flips.Add(sampled.FlipHorizontal);

            Assert.False(sampled.FlipVertical);
            Assert.Equal(0, sampled.QuarterTurns);
            Assert.Equal(0, sampled.RotateDegrees);
            Assert.Equal(1, sampled.Scale);
            Assert.Equal(1, sampled.Exposure);
            Assert.False(sampled.Grayscale);
            Assert.Null(sampled.JpegQuality);
            Assert.Empty(sampled.Cutouts);
        }

        Assert.Contains(true, flips);
        Assert.Contains(false, flips);
    }

    [Fact]
    public void Sample_QuarterTurnsOnlyComeFromTheEnabledOptionsOrNone()
    {
        var config = new AugmentConfig { Multiplier = 4, Rotate180 = true };
        var turns = Enumerable.Range(1, 200)
            .Select(copy => AugmentSampler.Sample(config, 5, copy, copy).QuarterTurns)
            .ToHashSet();

        Assert.Equal(new HashSet<int> { 0, 2 }, turns);
    }

    [Fact]
    public void Planner_NoneKeepsTheMasterAsItIs()
    {
        var plan = RenderPlanner.Plan(400, 200, new PreprocessConfig(), null);

        Assert.True(plan.IsIdentity);
        Assert.Equal((400, 200), (plan.OutputWidth, plan.OutputHeight));
    }

    [Fact]
    public void Planner_StretchFillsTheTargetIgnoringAspect()
    {
        var plan = RenderPlanner.Plan(400, 200, new PreprocessConfig { Resize = ResizeMode.Stretch, Width = 640, Height = 640 }, null);

        AssertPoint(640, 640, plan.Transform.Apply(new(400, 200)));
    }

    [Fact]
    public void Planner_FitScalesToTheShorterRatioAndCentres()
    {
        var plan = RenderPlanner.Plan(400, 200, new PreprocessConfig { Resize = ResizeMode.Fit, Width = 640, Height = 640 }, null);

        AssertPoint(0, 160, plan.Transform.Apply(new(0, 0)));
        AssertPoint(640, 480, plan.Transform.Apply(new(400, 200)));
    }

    [Fact]
    public void Planner_FillScalesToCoverAndCropsTheOverhang()
    {
        var plan = RenderPlanner.Plan(400, 200, new PreprocessConfig { Resize = ResizeMode.Fill, Width = 640, Height = 640 }, null);

        AssertPoint(-320, 0, plan.Transform.Apply(new(0, 0)));
    }

    [Fact]
    public void Planner_AugmentationRunsOnThePreprocessedFrame()
    {
        // Fit to 640 x 640, then a half turn: the picture's centre stays the centre of the 640 frame.
        var plan = RenderPlanner.Plan(
            400, 200,
            new PreprocessConfig { Resize = ResizeMode.Fit, Width = 640, Height = 640 },
            new AugmentParams { QuarterTurns = 2 });

        AssertPoint(320, 320, plan.Transform.Apply(new(200, 100)));
        AssertPoint(640, 480, plan.Transform.Apply(new(0, 0)));
    }

    [Fact]
    public void Planner_GrayscaleComesFromEitherSource()
    {
        Assert.True(RenderPlanner.Plan(10, 10, new PreprocessConfig { Grayscale = true }, null).Grayscale);
        Assert.True(RenderPlanner.Plan(10, 10, new PreprocessConfig(), new AugmentParams { Grayscale = true }).Grayscale);
        Assert.False(RenderPlanner.Plan(10, 10, new PreprocessConfig(), null).Grayscale);
    }

    [Fact]
    public void ScaledTo_ShrinksTheOutputAndTheMatrixTogether()
    {
        var plan = RenderPlanner.Plan(4000, 2000, new PreprocessConfig(), null).ScaledTo(640);

        Assert.Equal((640, 320), (plan.OutputWidth, plan.OutputHeight));
        var corner = plan.Transform.Apply(new(4000, 2000));
        Assert.Equal(640, corner.X, 6);
        Assert.Equal(320, corner.Y, 6);
    }

    [Fact]
    public void ScaledTo_LeavesASmallPictureAlone()
    {
        var plan = RenderPlanner.Plan(300, 200, new PreprocessConfig(), null);

        Assert.Same(plan, plan.ScaledTo(640));
    }
}


