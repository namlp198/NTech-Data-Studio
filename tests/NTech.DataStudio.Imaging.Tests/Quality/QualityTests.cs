using System.Numerics;
using NTech.DataStudio.Imaging.Quality;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Quality;

public class QualityTests
{
    private static Mat Gray(Mat image)
    {
        var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static int PDistance(Mat first, Mat second) =>
        BitOperations.PopCount(HashService.PerceptualHash(first) ^ HashService.PerceptualHash(second));

    private static int DDistance(Mat first, Mat second) =>
        BitOperations.PopCount(HashService.DifferenceHash(first) ^ HashService.DifferenceHash(second));

    [Fact]
    public void Hashes_AreStableForTheSameImage()
    {
        using var scene = TestImages.Scene(1);
        using var gray = Gray(scene);

        Assert.Equal(0, PDistance(gray, gray));
        Assert.Equal(0, DDistance(gray, gray));
    }

    [Fact]
    public void Hashes_StayCloseAfterRecompression()
    {
        using var scene = TestImages.Scene(2);
        Cv2.ImEncode(".jpg", scene, out var bytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, 60));
        using var recompressed = Cv2.ImDecode(bytes, ImreadModes.Color);
        using var first = Gray(scene);
        using var second = Gray(recompressed);

        Assert.InRange(PDistance(first, second), 0, 5);
    }

    [Fact]
    public void Hashes_StayCloseAfterResizeAndBrightnessShift()
    {
        using var scene = TestImages.Scene(3);
        using var smaller = new Mat();
        Cv2.Resize(scene, smaller, new Size(), 0.7, 0.7, InterpolationFlags.Area);
        using var brighter = new Mat();
        smaller.ConvertTo(brighter, -1, 1.0, 12);
        using var first = Gray(scene);
        using var second = Gray(brighter);

        Assert.InRange(PDistance(first, second), 0, 5);
    }

    [Fact]
    public void Hashes_StayFarApartForDifferentScenes()
    {
        var hashes = Enumerable.Range(10, 12).Select(seed =>
        {
            using var scene = TestImages.Scene(seed);
            using var gray = Gray(scene);
            return (P: HashService.PerceptualHash(gray), D: HashService.DifferenceHash(gray));
        }).ToList();

        for (var first = 0; first < hashes.Count; first++)
        {
            for (var second = first + 1; second < hashes.Count; second++)
            {
                Assert.True(BitOperations.PopCount(hashes[first].P ^ hashes[second].P) > 5);
                Assert.True(BitOperations.PopCount(hashes[first].D ^ hashes[second].D) > 5);
            }
        }
    }

    [Fact]
    public void Blur_LowersTheLaplacianVariance()
    {
        using var scene = TestImages.Scene(4);
        using var sharp = Gray(scene);
        using var blurred = new Mat();
        Cv2.GaussianBlur(sharp, blurred, new Size(15, 15), 0);

        Assert.True(BlurDetector.LaplacianVariance(blurred) < BlurDetector.LaplacianVariance(sharp) / 10);
    }

    [Fact]
    public void Exposure_CountsDarkAndBrightPixels()
    {
        using var image = new Mat(10, 10, MatType.CV_8UC1, new Scalar(128));
        using (var dark = new Mat(image, new Rect(0, 0, 10, 2))) dark.SetTo(new Scalar(5));
        using (var bright = new Mat(image, new Rect(0, 5, 10, 5))) bright.SetTo(new Scalar(255));

        var (darkFraction, brightFraction) = ExposureAnalyzer.Analyze(image);

        Assert.Equal(0.2, darkFraction, 6);
        Assert.Equal(0.5, brightFraction, 6);
    }
}
