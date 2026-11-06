using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Transforms;

public static class BitDepth
{
    private const double LowPercentile = 0.005;
    private const double HighPercentile = 0.995;
    private const int SampleEdge = 1024;

    /// <summary>
    /// 8-bit input is returned as a copy. Deeper input gets a percentile stretch:
    /// a machine-vision 16-bit image often uses a small part of its range, and a
    /// plain division by 256 would leave it nearly black.
    /// </summary>
    public static Mat ToEightBit(Mat image)
    {
        if (image.Depth() == MatType.CV_8U) return image.Clone();

        var (low, high) = Range(image);
        var scale = high > low ? 255.0 / (high - low) : 0;
        var output = new Mat();
        image.ConvertTo(output, MatType.MakeType(MatType.CV_8U, image.Channels()), scale, -low * scale);
        return output;
    }

    /// <summary>Percentiles over all channels, from a nearest-neighbour sample that keeps original values.</summary>
    private static (double Low, double High) Range(Mat image)
    {
        var scale = Math.Min(1.0, SampleEdge / (double)Math.Max(image.Width, image.Height));
        using var sample = new Mat();
        Cv2.Resize(image, sample, new Size(), scale, scale, InterpolationFlags.Nearest);

        using var floats = new Mat();
        sample.ConvertTo(floats, MatType.MakeType(MatType.CV_32F, sample.Channels()));
        using var flat = floats.Reshape(1, 1);
        flat.GetArray(out float[] values);
        Array.Sort(values);

        return (values[(int)((values.Length - 1) * LowPercentile)], values[(int)((values.Length - 1) * HighPercentile)]);
    }
}
