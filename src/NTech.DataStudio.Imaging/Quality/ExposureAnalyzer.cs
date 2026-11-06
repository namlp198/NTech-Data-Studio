using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Quality;

public static class ExposureAnalyzer
{
    private const byte DarkBelow = 30;
    private const byte BrightFrom = 250;

    /// <summary>Share of pixels that are nearly black and nearly white in an 8-bit single-channel image.</summary>
    public static (double DarkFraction, double BrightFraction) Analyze(Mat gray)
    {
        var total = (double)gray.Total();
        if (total == 0) return (0, 0);

        using var dark = new Mat();
        Cv2.Compare(gray, new Scalar(DarkBelow), dark, CmpTypes.LT);
        using var bright = new Mat();
        Cv2.Compare(gray, new Scalar(BrightFrom), bright, CmpTypes.GE);
        return (Cv2.CountNonZero(dark) / total, Cv2.CountNonZero(bright) / total);
    }
}
