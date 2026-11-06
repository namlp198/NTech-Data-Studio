using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests;

/// <summary>Synthetic images, so the tests carry no binary files.</summary>
internal static class TestImages
{
    /// <summary>A scene of random shapes: every seed gives a different, repeatable picture.</summary>
    public static Mat Scene(int seed, int width = 640, int height = 480)
    {
        var random = new Random(seed);
        var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(random.Next(80, 170), random.Next(80, 170), random.Next(80, 170)));
        for (var index = 0; index < 40; index++)
        {
            var color = new Scalar(random.Next(256), random.Next(256), random.Next(256));
            var center = new Point(random.Next(width), random.Next(height));
            var size = random.Next(10, Math.Max(11, width / 4));
            if (index % 2 == 0)
                Cv2.Circle(image, center, size / 2, color, -1);
            else
                Cv2.Rectangle(image, new Rect(center.X, center.Y, size, size / 2), color, -1);
        }

        return image;
    }

    public static byte[] Encode(Mat image, string extension)
    {
        Cv2.ImEncode(extension, image, out var bytes);
        return bytes;
    }

    public static string Write(string folder, string name, Mat image)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Encode(image, Path.GetExtension(name)));
        return path;
    }

    /// <summary>Inserts a minimal EXIF block with the given orientation after the JPEG start marker.</summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] exif =
        {
            0xFF, 0xE1, 0x00, 0x22,
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8,
            0, 1,
            0x01, 0x12, 0, 3, 0, 0, 0, 1, (byte)(orientation >> 8), (byte)orientation, 0, 0,
            0, 0, 0, 0
        };
        return jpeg.Take(2).Concat(exif).Concat(jpeg.Skip(2)).ToArray();
    }
}

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        // The accents are deliberate: Cv2.ImRead cannot open such a path.
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ntech-studio-tests", Guid.NewGuid().ToString("N"), "ảnh gốc");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        var root = Directory.GetParent(Path)!.FullName;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
