using System.Security.Cryptography;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Quality;
using NTech.DataStudio.Imaging.Transforms;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Codecs;

/// <summary>
/// All files go through byte arrays: Cv2.ImRead and ImWrite cannot open
/// non-ASCII paths on Windows, and Vietnamese folder names are the norm here.
/// </summary>
public sealed class OpenCvImageCodec : IImageCodec
{
    private const int ThumbnailEdge = 256;
    private const int AnalysisEdge = 2048;

    // Keeps bit depth and mono, drops alpha, and applies EXIF rotation.
    private const ImreadModes DecodeFlags = ImreadModes.AnyDepth | ImreadModes.AnyColor;

    public ImageHeader ReadHeader(string path)
    {
        try
        {
            var header = ImageHeaderReader.Read(path);
            if (header is not null && header.Width > 0 && header.Height > 0)
                return header;

            using var image = Decode(ReadAllBytes(path));
            return new ImageHeader("?", image.Width, image.Height, DepthBits(image), image.Channels());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImageReadException($"Không đọc được file: {exception.Message}", exception);
        }
    }

    public ImageAnalysis Analyze(string path, string thumbnailPath)
    {
        var bytes = ReadAllBytes(path);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        using var decoded = Decode(bytes);
        using var eightBit = BitDepth.ToEightBit(decoded);
        using var gray = ColorSpace.ToGray(eightBit);
        using var small = Resize.ToLongEdge(gray, AnalysisEdge);

        var exposure = ExposureAnalyzer.Analyze(small);
        var analysis = new ImageAnalysis(
            sha256,
            HashService.PerceptualHash(small),
            HashService.DifferenceHash(small),
            BlurDetector.LaplacianVariance(small),
            exposure.DarkFraction,
            exposure.BrightFraction);

        WriteThumbnail(eightBit, thumbnailPath);
        return analysis;
    }

    public IReadOnlyList<MasterFile> Normalize(
        string sourcePath,
        NormalizeOptions options,
        string masterFolder,
        string thumbsFolder,
        string fileStem)
    {
        using var image = DecodeForMaster(ReadAllBytes(sourcePath), options.Mono);
        var parts = Layout(image, options.Geometry);
        var multiple = parts.Count > 1;
        var extension = Extension(options.Format);

        var masters = new List<MasterFile>(parts.Count);
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            var name = multiple ? $"{fileStem}_t{index:D3}" : fileStem;
            using var master = Render(image, part);

            var encoded = Encode(master, options.Format);
            WriteAtomically(Path.Combine(masterFolder, name + extension), encoded);
            WriteThumbnail(master, Path.Combine(thumbsFolder, name + ".jpg"));

            masters.Add(new MasterFile(
                name + extension,
                name + ".jpg",
                master.Width,
                master.Height,
                part.Area.X,
                part.Area.Y,
                part.Scale,
                encoded.Length));
        }

        return masters;
    }

    public NormalizePreview RenderPreview(string sourcePath, NormalizeOptions options, int maxEdge)
    {
        using var image = DecodeForMaster(ReadAllBytes(sourcePath), options.Mono);
        var parts = Layout(image, options.Geometry);
        using var master = Render(image, parts[0]);

        using var beforeSmall = Resize.ToLongEdge(image, maxEdge);
        using var afterSmall = Resize.ToLongEdge(master, maxEdge);
        var firstBytes = Encode(master, options.Format).LongLength;

        return new NormalizePreview(
            Encode(beforeSmall, MasterFormat.Jpeg95),
            Encode(afterSmall, MasterFormat.Jpeg95),
            image.Width,
            image.Height,
            master.Width,
            master.Height,
            parts.Count,
            firstBytes * parts.Count);
    }

    private readonly record struct MasterPart(Rect Area, double Scale);

    private static IReadOnlyList<MasterPart> Layout(Mat image, GeometryPlan plan)
    {
        var whole = new Rect(0, 0, image.Width, image.Height);
        switch (plan.Method)
        {
            case GeometryMethod.Downscale:
                return new[] { new MasterPart(whole, Resize.ScaleFor(image.Width, image.Height, plan.MaxLongEdge)) };

            case GeometryMethod.Tile:
                return TileLayout.Compute(image.Width, image.Height, plan.TileSize, plan.TileOverlap)
                    .Select(tile => new MasterPart(new Rect(tile.X, tile.Y, tile.Width, tile.Height), 1))
                    .ToList();

            case GeometryMethod.RoiCrop:
                return new[] { new MasterPart(RoiCrop.ClampToImage(plan.Roi, image.Width, image.Height), 1) };

            default:
                throw new ArgumentOutOfRangeException(nameof(plan));
        }
    }

    private static Mat Render(Mat image, MasterPart part)
    {
        using var view = new Mat(image, part.Area);
        return part.Scale < 1 ? Resize.ByScale(view, part.Scale) : view.Clone();
    }

    private static Mat DecodeForMaster(byte[] bytes, MonoMode mono)
    {
        using var decoded = Decode(bytes);
        using var eightBit = BitDepth.ToEightBit(decoded);
        return ColorSpace.ForMaster(eightBit, mono);
    }

    private static Mat Decode(byte[] bytes)
    {
        Mat image;
        try
        {
            image = Cv2.ImDecode(bytes, DecodeFlags);
        }
        catch (OpenCVException exception)
        {
            throw new ImageReadException($"Không giải mã được ảnh: {exception.Message}", exception);
        }

        if (image is null || image.Empty())
        {
            image?.Dispose();
            throw new ImageReadException("Không giải mã được ảnh: file hỏng hoặc sai định dạng.");
        }

        return image;
    }

    private static byte[] ReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImageReadException($"Không đọc được file: {exception.Message}", exception);
        }
    }

    private static byte[] Encode(Mat image, MasterFormat format)
    {
        var parameters = format == MasterFormat.Jpeg95
            ? new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, 95) }
            : Array.Empty<ImageEncodingParam>();
        Cv2.ImEncode(Extension(format), image, out var buffer, parameters);
        return buffer;
    }

    private static void WriteThumbnail(Mat image, string path)
    {
        using var small = Resize.ToLongEdge(image, ThumbnailEdge);
        WriteAtomically(path, Encode(small, MasterFormat.Jpeg95));
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var partial = path + ".partial";
        File.WriteAllBytes(partial, bytes);
        File.Move(partial, path, overwrite: true);
    }

    private static string Extension(MasterFormat format) => format == MasterFormat.Jpeg95 ? ".jpg" : ".png";

    private static int DepthBits(Mat image)
    {
        var depth = image.Depth();
        if (depth == MatType.CV_8U || depth == MatType.CV_8S) return 8;
        if (depth == MatType.CV_16U || depth == MatType.CV_16S) return 16;
        return depth == MatType.CV_64F ? 64 : 32;
    }
}
