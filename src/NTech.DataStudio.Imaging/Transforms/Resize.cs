using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Transforms;

public static class Resize
{
    /// <summary>The factor that brings the long edge down to the limit; 1 when it already fits. Never upscales.</summary>
    public static double ScaleFor(int width, int height, int maxLongEdge)
    {
        var longEdge = Math.Max(width, height);
        return maxLongEdge > 0 && longEdge > maxLongEdge ? maxLongEdge / (double)longEdge : 1;
    }

    public static Mat ToLongEdge(Mat image, int maxLongEdge)
    {
        var scale = ScaleFor(image.Width, image.Height, maxLongEdge);
        return scale < 1 ? ByScale(image, scale) : image.Clone();
    }

    /// <summary>INTER_AREA: the sharpest choice for shrinking, and it does not alias thin details.</summary>
    public static Mat ByScale(Mat image, double scale)
    {
        var size = new Size(
            Math.Max(1, (int)Math.Round(image.Width * scale)),
            Math.Max(1, (int)Math.Round(image.Height * scale)));
        var output = new Mat();
        Cv2.Resize(image, output, size, 0, 0, InterpolationFlags.Area);
        return output;
    }
}
