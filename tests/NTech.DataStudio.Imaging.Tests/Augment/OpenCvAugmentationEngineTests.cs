using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Augment;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Augment;

public sealed class OpenCvAugmentationEngineTests : IDisposable
{
    private const int Width = 400;
    private const int Height = 200;
    private const int BoxX = 60;
    private const int BoxY = 40;
    private const int BoxWidth = 80;
    private const int BoxHeight = 50;

    private readonly TempFolder _temp = new();
    private readonly OpenCvAugmentationEngine _engine = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A dark picture with one white block: its centre of mass is where the box's centre must land.</summary>
    private string Master(int width = Width, int height = Height, int boxX = BoxX, int boxY = BoxY, int boxWidth = BoxWidth, int boxHeight = BoxHeight, MatType? type = null)
    {
        using var image = new Mat(height, width, type ?? MatType.CV_8UC3, Scalar.All(30));
        Cv2.Rectangle(image, new Rect(boxX, boxY, boxWidth, boxHeight), Scalar.All(255), -1);
        return TestImages.Write(_temp.Path, $"master-{Guid.NewGuid():N}.png", image);
    }

    private static Mat Decode(RenderedImage rendered) => Cv2.ImDecode(rendered.Data, ImreadModes.Unchanged);

    private static (double X, double Y) CentreOfMass(Mat image, double threshold = 160)
    {
        using var gray = image.Channels() == 1 ? image.Clone() : image.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var mask = gray.Threshold(threshold, 255, ThresholdTypes.Binary);
        var moments = Cv2.Moments(mask, binaryImage: true);
        // Moments are in pixel indices; +0.5 turns the pixel centre into the edge coordinates labels use.
        return (moments.M10 / moments.M00 + 0.5, moments.M01 / moments.M00 + 0.5);
    }

    private static PixelPoint BoxCentre => new(BoxX + BoxWidth / 2.0, BoxY + BoxHeight / 2.0);

    [Theory]
    [InlineData("flip-h", 1)]
    [InlineData("flip-v", 2)]
    [InlineData("both-flips", 3)]
    [InlineData("turn-cw", 4)]
    [InlineData("turn-180", 5)]
    [InlineData("turn-ccw", 6)]
    [InlineData("rotate", 7)]
    [InlineData("scale-down", 8)]
    [InlineData("scale-up", 9)]
    [InlineData("translate", 10)]
    [InlineData("shear", 11)]
    [InlineData("everything", 12)]
    public void Render_PicturesAndLabelsMoveToTheSameSpot(string name, int variant)
    {
        var augment = variant switch
        {
            1 => new AugmentParams { FlipHorizontal = true },
            2 => new AugmentParams { FlipVertical = true },
            3 => new AugmentParams { FlipHorizontal = true, FlipVertical = true },
            4 => new AugmentParams { QuarterTurns = 1 },
            5 => new AugmentParams { QuarterTurns = 2 },
            6 => new AugmentParams { QuarterTurns = 3 },
            7 => new AugmentParams { RotateDegrees = 17 },
            8 => new AugmentParams { Scale = 0.7 },
            9 => new AugmentParams { Scale = 1.3 },
            10 => new AugmentParams { TranslateX = 0.1, TranslateY = -0.12 },
            11 => new AugmentParams { ShearXDegrees = 10, ShearYDegrees = -6 },
            _ => new AugmentParams { FlipHorizontal = true, QuarterTurns = 1, RotateDegrees = -12, Scale = 0.85, TranslateX = 0.05, ShearXDegrees = 5 }
        };
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), augment);

        using var output = Decode(_engine.Render(Master(), plan, RenderFormat.Png));

        Assert.Equal((plan.OutputWidth, plan.OutputHeight), (output.Width, output.Height));
        var expected = plan.Transform.Apply(BoxCentre);
        var actual = CentreOfMass(output);
        Assert.True(Math.Abs(actual.X - expected.X) < 1.0 && Math.Abs(actual.Y - expected.Y) < 1.0,
            $"{name}: block centre at ({actual.X:0.00}, {actual.Y:0.00}), label centre at ({expected.X:0.00}, {expected.Y:0.00})");
    }

    [Theory]
    [InlineData(ResizeMode.Stretch)]
    [InlineData(ResizeMode.Fit)]
    [InlineData(ResizeMode.Fill)]
    public void Render_PreprocessingResizeLinesUpWithItsLabels(ResizeMode mode)
    {
        var preprocess = new PreprocessConfig { Resize = mode, Width = 320, Height = 256 };
        var plan = RenderPlanner.Plan(Width, Height, preprocess, null);

        // A block in the middle, so that Fill, which crops the sides, keeps all of it.
        using var output = Decode(_engine.Render(Master(boxX: 160, boxY: 70, boxWidth: 80, boxHeight: 60), plan, RenderFormat.Png));

        Assert.Equal((320, 256), (output.Width, output.Height));
        var expected = plan.Transform.Apply(new PixelPoint(200, 100));
        var actual = CentreOfMass(output);
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, 1.0);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, 1.0);
    }

    [Fact]
    public void Render_ABigMasterIsShrunkByAreaAndStillLinesUp()
    {
        // 3200 x 1600 down to 320: far past the factor where plain warping would alias.
        var master = Master(3200, 1600, 600, 400, 800, 500);
        var plan = RenderPlanner.Plan(3200, 1600, new PreprocessConfig { Resize = ResizeMode.Fit, Width = 320, Height = 320 }, new AugmentParams { RotateDegrees = 9 });

        using var output = Decode(_engine.Render(master, plan, RenderFormat.Png));

        var expected = plan.Transform.Apply(new PixelPoint(1000, 650));
        var actual = CentreOfMass(output);
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, 1.0);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, 1.0);
    }

    [Fact]
    public void Render_UncoveredAreasAreFilledWithTheLetterboxGrey()
    {
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig { Resize = ResizeMode.Fit, Width = 400, Height = 400 }, null);

        using var output = Decode(_engine.Render(Master(), plan, RenderFormat.Png));

        // The 400 x 200 picture sits in the middle of 400 x 400: bars above and below.
        Assert.Equal(RenderPlan.FillValue, output.At<Vec3b>(10, 200).Item0);
        Assert.Equal(RenderPlan.FillValue, output.At<Vec3b>(390, 200).Item0);
        Assert.Equal(30, output.At<Vec3b>(110, 10).Item0);
    }

    [Fact]
    public void Render_SamePlanGivesIdenticalBytesIncludingNoiseAndBlur()
    {
        var master = Master();
        var augment = AugmentSampler.Sample(
            new AugmentConfig
            {
                Multiplier = 3, RotateDegrees = 10, NoisePercent = 5, BlurPx = 1.5, MotionBlurPx = 7,
                HueDegrees = 15, SaturationPercent = 20, GammaPercent = 15, CutoutCount = 2, JpegQualityMin = 50
            },
            seed: 3, imageId: 1, copyIndex: 1);
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), augment);

        var first = _engine.Render(master, plan, RenderFormat.Png);
        var second = _engine.Render(master, plan, RenderFormat.Png);

        Assert.Equal(first.Data, second.Data);
    }

    [Fact]
    public void Render_DifferentNoiseSeedsGiveDifferentNoise()
    {
        var master = Master();
        byte[] Noisy(long seed) => _engine.Render(
            master,
            RenderPlanner.Plan(Width, Height, new PreprocessConfig(), new AugmentParams { NoiseSigma = 12, NoiseSeed = seed }),
            RenderFormat.Png).Data;

        Assert.NotEqual(Noisy(1), Noisy(2));
    }

    [Fact]
    public void Render_NoiseHasTheRequestedSpread()
    {
        var master = Master(200, 200, 0, 0, 1, 1);
        var plan = RenderPlanner.Plan(200, 200, new PreprocessConfig(), new AugmentParams { NoiseSigma = 10, NoiseSeed = 5 });

        using var output = Decode(_engine.Render(master, plan, RenderFormat.Png));
        using var flat = new Mat(output, new Rect(20, 20, 160, 160));
        Cv2.MeanStdDev(flat, out var mean, out var deviation);

        Assert.InRange(mean.Val0, 28, 32);
        Assert.InRange(deviation.Val0, 8.5, 11.5);
    }

    [Fact]
    public void Render_BrightnessExposureAndContrastChangeThePixelsAsDescribed()
    {
        var master = Master(100, 100, 0, 0, 1, 1);
        Mat Render(AugmentParams augment)
        {
            var plan = RenderPlanner.Plan(100, 100, new PreprocessConfig(), augment);
            return Decode(_engine.Render(master, plan, RenderFormat.Png));
        }

        using var brighter = Render(new AugmentParams { Brightness = 40 });
        using var doubled = Render(new AugmentParams { Exposure = 2 });
        using var flatter = Render(new AugmentParams { Contrast = 0.5 });

        Assert.Equal(70, brighter.At<Vec3b>(50, 50).Item1);
        Assert.Equal(60, doubled.At<Vec3b>(50, 50).Item1);
        // (30 - 128) * 0.5 + 128 = 79
        Assert.Equal(79, flatter.At<Vec3b>(50, 50).Item1);
    }

    [Fact]
    public void Render_GammaBelowOneBrightensTheMidtones()
    {
        var master = Master(50, 50, 0, 0, 1, 1);
        var plan = RenderPlanner.Plan(50, 50, new PreprocessConfig(), new AugmentParams { Gamma = 0.5 });

        using var output = Decode(_engine.Render(master, plan, RenderFormat.Png));

        // 255 * (30/255) ^ 0.5 = 87.5
        Assert.InRange((int)output.At<Vec3b>(25, 25).Item0, 86, 89);
    }

    [Fact]
    public void Render_GrayscaleMakesEveryChannelEqual()
    {
        using var colourful = new Mat(60, 60, MatType.CV_8UC3, new Scalar(200, 60, 20));
        var path = TestImages.Write(_temp.Path, "colour.png", colourful);
        var plan = RenderPlanner.Plan(60, 60, new PreprocessConfig { Grayscale = true }, null);

        using var output = Decode(_engine.Render(path, plan, RenderFormat.Png));

        var pixel = output.At<Vec3b>(30, 30);
        Assert.Equal(pixel.Item0, pixel.Item1);
        Assert.Equal(pixel.Item1, pixel.Item2);
    }

    [Fact]
    public void Render_HueShiftChangesColourButNotAGreyPixel()
    {
        using var colourful = new Mat(60, 60, MatType.CV_8UC3, new Scalar(200, 60, 20));
        var colourPath = TestImages.Write(_temp.Path, "colour.png", colourful);
        var plan = RenderPlanner.Plan(60, 60, new PreprocessConfig(), new AugmentParams { HueShiftDegrees = 90 });

        using var shifted = Decode(_engine.Render(colourPath, plan, RenderFormat.Png));
        using var grey = Decode(_engine.Render(Master(60, 60, 0, 0, 1, 1), plan, RenderFormat.Png));

        Assert.NotEqual(new Vec3b(200, 60, 20), shifted.At<Vec3b>(30, 30));
        Assert.Equal(30, grey.At<Vec3b>(30, 30).Item0);
    }

    [Fact]
    public void Render_CutoutsAreFilledWithTheCutoutGrey()
    {
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig(),
            new AugmentParams { Cutouts = new[] { new NormalizedRect(0.5, 0.5, 0.25, 0.25) } });

        using var output = Decode(_engine.Render(Master(), plan, RenderFormat.Png));

        Assert.Equal(RenderPlan.CutoutValue, output.At<Vec3b>(125, 250).Item0);
        Assert.Equal(30, output.At<Vec3b>(125, 380).Item0);
    }

    [Fact]
    public void Render_JpegArtifactsChangeThePixelsButNotTheSize()
    {
        var master = Master();
        var clean = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), null);
        var crushed = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), new AugmentParams { JpegQuality = 12 });

        using var before = Decode(_engine.Render(master, clean, RenderFormat.Png));
        using var after = Decode(_engine.Render(master, crushed, RenderFormat.Png));

        Assert.Equal((before.Width, before.Height), (after.Width, after.Height));
        Assert.True(Cv2.Norm(before, after) > 0);
    }

    [Fact]
    public void Render_JpegOutputHonoursTheRequestedQuality()
    {
        var master = Master();
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), new AugmentParams { NoiseSigma = 15, NoiseSeed = 1 });

        var high = _engine.Render(master, plan, RenderFormat.Jpeg, 95);
        var low = _engine.Render(master, plan, RenderFormat.Jpeg, 20);

        Assert.True(low.Data.Length < high.Data.Length);
        Assert.Equal(0xFF, high.Data[0]);
        Assert.Equal(0xD8, high.Data[1]);
    }

    [Fact]
    public void Render_AGrayMasterStaysGray()
    {
        var master = Master(type: MatType.CV_8UC1);
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), new AugmentParams { FlipHorizontal = true, BlurSigma = 1, Brightness = 10 });

        using var output = Decode(_engine.Render(master, plan, RenderFormat.Png));

        Assert.Equal(1, output.Channels());
    }

    [Fact]
    public void Render_AnAlphaChannelIsDropped()
    {
        using var rgba = new Mat(40, 40, MatType.CV_8UC4, new Scalar(10, 20, 30, 128));
        var path = TestImages.Write(_temp.Path, "alpha.png", rgba);

        using var output = Decode(_engine.Render(path, RenderPlanner.Plan(40, 40, new PreprocessConfig(), null), RenderFormat.Png));

        Assert.Equal(3, output.Channels());
    }

    [Fact]
    public void Render_AnIdentityPlanKeepsEveryPixel()
    {
        var master = Master();

        using var output = Decode(_engine.Render(master, RenderPlanner.Plan(Width, Height, new PreprocessConfig(), null), RenderFormat.Png));
        using var original = Cv2.ImDecode(File.ReadAllBytes(master), ImreadModes.Unchanged);

        Assert.Equal(0, Cv2.Norm(original, output));
    }

    [Fact]
    public void Render_AMissingFileIsAnImageReadException()
    {
        var plan = RenderPlanner.Plan(10, 10, new PreprocessConfig(), null);

        Assert.Throws<ImageReadException>(() => _engine.Render(Path.Combine(_temp.Path, "nope.png"), plan, RenderFormat.Png));
    }

    [Fact]
    public void Render_ACorruptFileIsAnImageReadException()
    {
        var path = Path.Combine(_temp.Path, "broken.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var plan = RenderPlanner.Plan(10, 10, new PreprocessConfig(), null);

        Assert.Throws<ImageReadException>(() => _engine.Render(path, plan, RenderFormat.Png));
    }

    [Fact]
    public void Render_CanBeCalledFromManyThreadsAtOnce()
    {
        var master = Master();
        var plan = RenderPlanner.Plan(Width, Height, new PreprocessConfig(), new AugmentParams { RotateDegrees = 8, NoiseSigma = 6, NoiseSeed = 9 });
        var expected = _engine.Render(master, plan, RenderFormat.Png).Data;

        var results = Enumerable.Range(0, 16).AsParallel().Select(_ => _engine.Render(master, plan, RenderFormat.Png).Data).ToList();

        Assert.All(results, data => Assert.Equal(expected, data));
    }
}

