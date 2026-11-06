using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Quality;

/// <summary>Perceptual hashes of an 8-bit single-channel image. SHA-256 of the file is computed by the codec.</summary>
public static class HashService
{
    /// <summary>dHash: is each pixel darker than its right neighbour, on a 9×8 image.</summary>
    public static ulong DifferenceHash(Mat gray)
    {
        using var small = new Mat();
        Cv2.Resize(gray, small, new Size(9, 8), 0, 0, InterpolationFlags.Area);

        ulong hash = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                hash <<= 1;
                if (small.At<byte>(y, x) < small.At<byte>(y, x + 1))
                    hash |= 1;
            }
        }

        return hash;
    }

    /// <summary>pHash: signs of the 8×8 lowest DCT frequencies of a 32×32 image, relative to their median.</summary>
    public static ulong PerceptualHash(Mat gray)
    {
        using var small = new Mat();
        Cv2.Resize(gray, small, new Size(32, 32), 0, 0, InterpolationFlags.Area);
        using var floats = new Mat();
        small.ConvertTo(floats, MatType.CV_32F);
        using var frequencies = new Mat();
        Cv2.Dct(floats, frequencies);

        var values = new float[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
                values[y * 8 + x] = frequencies.At<float>(y, x);
        }

        // The DC term only says how bright the image is, so it stays out of the median.
        var median = values.Skip(1).Order().ElementAt(31);
        ulong hash = 0;
        foreach (var value in values)
        {
            hash <<= 1;
            if (value > median)
                hash |= 1;
        }

        return hash;
    }
}
