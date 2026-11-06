using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Quality;

public static class BlurDetector
{
    /// <summary>Variance of the Laplacian: few sharp edges give a low value.</summary>
    public static double LaplacianVariance(Mat gray)
    {
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        Cv2.MeanStdDev(laplacian, out _, out var deviation);
        return deviation.Val0 * deviation.Val0;
    }
}
