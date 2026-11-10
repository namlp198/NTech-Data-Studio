using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>
/// Segment Anything (MobileSAM, ONNX) on the CPU: the encoder analyses a picture (or the part of it on screen) once, and
/// the decoder then turns points or a box into a mask in about a tenth of a second (decision 0016). The encoder is given
/// a copy whose long side is 1024 px, so a 20 MP master costs no more than a small one and a small object is as big as the
/// zoom makes it. Outlines are scaled back to master pixels. The two model files are not part of the app: they live in
/// <see cref="ModelFolder"/> (MobileSAM, Apache-2.0).
/// </summary>
public sealed class SamSegmenter : ISmartSegmenter, IDisposable
{
    public const string EncoderFile = "mobile_sam_image_encoder.onnx";
    public const string DecoderFile = "sam_mask_decoder_single.onnx";

    /// <summary>The size the encoder was trained for: the long side of what it is given.</summary>
    private const int EncoderSide = 1024;

    private const int MaskSize = 256;

    /// <summary>A region smaller than this on a side is not worth analysing; it is widened to it.</summary>
    private const int MinRegionSide = 64;

    /// <summary>What the encoder worked out for one picture or region, and how master pixels map into it.</summary>
    private sealed record Frame(
        string Path, int X, int Y, int Width, int Height, bool IsWhole, DenseTensor<float> Embeddings, double Scale, int ScaledWidth, int ScaledHeight);

    private readonly object _gate = new();
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;

    private string? _decodedPath;
    private Mat? _decoded;
    private Frame? _whole;
    private Frame? _zoomed;

    public SamSegmenter(string modelFolder)
    {
        ModelFolder = modelFolder;
    }

    public string ModelFolder { get; }

    /// <summary>The usual place: <c>%LOCALAPPDATA%\NTech\DataStudio\models\sam</c>.</summary>
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NTech", "DataStudio", "models", "sam");

    public string? Unavailable
    {
        get
        {
            var missing = new[] { EncoderFile, DecoderFile }.Where(name => !File.Exists(Path.Combine(ModelFolder, name))).ToList();
            return missing.Count == 0
                ? null
                : $"Chưa có model SAM. Hãy đặt {string.Join(" và ", missing)} vào thư mục {ModelFolder}.";
        }
    }

    public Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            lock (_gate) FrameFor(imagePath, region, cancellationToken);
        }, cancellationToken);

    public Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
        string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => Segment(imagePath, region, request, cancellationToken), cancellationToken);

    public void Dispose()
    {
        lock (_gate)
        {
            _encoder?.Dispose();
            _decoder?.Dispose();
            _decoded?.Dispose();
            _encoder = null;
            _decoder = null;
            _decoded = null;
            _whole = null;
            _zoomed = null;
        }
    }

    // ---- analysing a picture or a region of it ----

    /// <summary>Whole picture and the last zoomed region are both kept, so zooming in and out again costs nothing the second time.</summary>
    private Frame FrameFor(string imagePath, PixelRect? region, CancellationToken cancellationToken)
    {
        if (Unavailable is { } why) throw new InvalidOperationException(why);

        if (_decodedPath != imagePath)
        {
            _whole = null;
            _zoomed = null;
        }

        var crop = region is { } wanted ? CropOf(imagePath, wanted) : null;
        if (crop is null && _whole is { } whole && whole.Path == imagePath) return whole;
        if (crop is { } rect && _zoomed is { } zoomed && zoomed.Path == imagePath
            && zoomed.X == rect.X && zoomed.Y == rect.Y && zoomed.Width == rect.Width && zoomed.Height == rect.Height)
            return zoomed;

        var frame = Encode(imagePath, crop, cancellationToken);
        if (frame.IsWhole) _whole = frame;
        else _zoomed = frame;
        return frame;
    }

    /// <summary>The region rounded outwards to whole pixels, kept inside the picture and not tiny. Null (the whole picture) when it covers it all.</summary>
    private OpenCvSharp.Rect? CropOf(string imagePath, PixelRect region)
    {
        var picture = Decoded(imagePath);
        var left = Math.Clamp((int)Math.Floor(region.X), 0, picture.Width - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y), 0, picture.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(region.Right), left + 1, picture.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(region.Bottom), top + 1, picture.Height);

        if (right - left < MinRegionSide) (left, right) = Widen(left, right, picture.Width);
        if (bottom - top < MinRegionSide) (top, bottom) = Widen(top, bottom, picture.Height);
        if (left == 0 && top == 0 && right == picture.Width && bottom == picture.Height) return null;

        return new OpenCvSharp.Rect(left, top, right - left, bottom - top);
    }

    private static (int Low, int High) Widen(int low, int high, int limit)
    {
        var size = Math.Min(MinRegionSide, limit);
        var start = Math.Clamp((low + high) / 2 - size / 2, 0, limit - size);
        return (start, start + size);
    }

    private Mat Decoded(string imagePath)
    {
        if (_decoded is not null && _decodedPath == imagePath) return _decoded;

        // Read through bytes: OpenCV's own file functions cannot open a Vietnamese path (CLAUDE.md).
        using var source = Cv2.ImDecode(File.ReadAllBytes(imagePath), ImreadModes.AnyDepth | ImreadModes.AnyColor);
        if (source.Empty())
            throw new InvalidOperationException($"Không đọc được ảnh {Path.GetFileName(imagePath)}.");

        _decoded?.Dispose();
        _decoded = To8BitColor(source);
        _decodedPath = imagePath;
        return _decoded;
    }

    private Frame Encode(string imagePath, OpenCvSharp.Rect? crop, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _encoder ??= new InferenceSession(Path.Combine(ModelFolder, EncoderFile));
        _decoder ??= new InferenceSession(Path.Combine(ModelFolder, DecoderFile));

        var picture = Decoded(imagePath);
        var rect = crop ?? new OpenCvSharp.Rect(0, 0, picture.Width, picture.Height);
        using var part = new Mat(picture, rect);
        var scale = (double)EncoderSide / Math.Max(rect.Width, rect.Height);
        var width = Math.Max(1, (int)Math.Round(rect.Width * scale));
        var height = Math.Max(1, (int)Math.Round(rect.Height * scale));
        using var resized = new Mat();
        Cv2.Resize(part, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);

        // The encoder takes RGB 0..255 as floats, height x width x 3, already at its working size; it normalises inside.
        var data = new float[height * width * 3];
        resized.GetArray(out Vec3b[] pixels);
        for (var index = 0; index < pixels.Length; index++)
        {
            data[index * 3] = pixels[index].Item2;
            data[index * 3 + 1] = pixels[index].Item1;
            data[index * 3 + 2] = pixels[index].Item0;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var results = _encoder.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_image", new DenseTensor<float>(data, new[] { height, width, 3 }))
        });

        return new Frame(
            imagePath, rect.X, rect.Y, rect.Width, rect.Height, crop is null,
            results.First().AsTensor<float>().ToDenseTensor(), scale, width, height);
    }

    // ---- cutting an object out ----

    private IReadOnlyList<PixelPoint>? Segment(string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken)
    {
        if (request.IsEmpty) return null;

        lock (_gate)
        {
            var frame = FrameFor(imagePath, region, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // A positive point outside the analysed part cannot be answered: the model never saw that pixel.
            if (request.Points.Any(prompt => prompt.Positive && !Inside(frame, prompt.Position))) return null;

            // Points in the encoder's frame; a box adds its two corners (labels 2 and 3), otherwise one padding point (label -1).
            var points = request.Points.Where(prompt => Inside(frame, prompt.Position)).ToList();
            PixelRect? box = request.Box is { } wanted ? Clip(frame, wanted) : null;
            if (request.Box is not null && box is null) return null;

            var count = points.Count + (box is null ? 1 : 2);
            var coordinates = new float[count * 2];
            var labels = new float[count];
            for (var index = 0; index < points.Count; index++)
            {
                coordinates[index * 2] = (float)((points[index].Position.X - frame.X) * frame.Scale);
                coordinates[index * 2 + 1] = (float)((points[index].Position.Y - frame.Y) * frame.Scale);
                labels[index] = points[index].Positive ? 1 : 0;
            }

            if (box is { } rectangle)
            {
                var first = points.Count;
                coordinates[first * 2] = (float)((rectangle.X - frame.X) * frame.Scale);
                coordinates[first * 2 + 1] = (float)((rectangle.Y - frame.Y) * frame.Scale);
                coordinates[(first + 1) * 2] = (float)((rectangle.Right - frame.X) * frame.Scale);
                coordinates[(first + 1) * 2 + 1] = (float)((rectangle.Bottom - frame.Y) * frame.Scale);
                labels[first] = 2;
                labels[first + 1] = 3;
            }
            else
            {
                labels[count - 1] = -1;
            }

            using var results = _decoder!.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", frame.Embeddings),
                NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coordinates, new[] { 1, count, 2 })),
                NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(labels, new[] { 1, count })),
                NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>(new float[MaskSize * MaskSize], new[] { 1, 1, MaskSize, MaskSize })),
                NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>(new float[] { 0 }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new float[] { frame.ScaledHeight, frame.ScaledWidth }, new[] { 2 }))
            });

            var masks = results.First(result => result.Name == "masks").AsTensor<float>();
            var anchor = request.Points.FirstOrDefault(prompt => prompt.Positive) is { } positive
                ? positive.Position
                : box is { } inBox ? new PixelPoint((inBox.X + inBox.Right) / 2, (inBox.Y + inBox.Bottom) / 2) : (PixelPoint?)null;
            return OutlineOf(masks, anchor, frame);
        }
    }

    private static bool Inside(Frame frame, PixelPoint point) =>
        point.X >= frame.X && point.X <= frame.X + frame.Width && point.Y >= frame.Y && point.Y <= frame.Y + frame.Height;

    private static PixelRect? Clip(Frame frame, PixelRect box)
    {
        var left = Math.Max(box.X, frame.X);
        var top = Math.Max(box.Y, frame.Y);
        var right = Math.Min(box.Right, frame.X + frame.Width);
        var bottom = Math.Min(box.Bottom, frame.Y + frame.Height);
        return right - left >= 1 && bottom - top >= 1 ? new PixelRect(left, top, right - left, bottom - top) : null;
    }

    /// <summary>The outline of the part of the mask the anchor lies in (else the biggest part), in master pixels.</summary>
    private static IReadOnlyList<PixelPoint>? OutlineOf(Tensor<float> masks, PixelPoint? anchorPoint, Frame frame)
    {
        var dimensions = masks.Dimensions.ToArray();
        var height = dimensions[2];
        var width = dimensions[3];
        var logits = masks.ToArray();

        using var binary = new Mat(height, width, MatType.CV_8UC1);
        var bytes = new byte[height * width];
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = logits[index] > 0 ? (byte)255 : (byte)0;
        binary.SetArray(bytes);

        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        if (contours.Length == 0) return null;

        var anchor = anchorPoint is { } at
            ? new Point2f((float)((at.X - frame.X) * frame.Scale), (float)((at.Y - frame.Y) * frame.Scale))
            : (Point2f?)null;
        var chosen = anchor is { } point
            ? contours.Where(contour => Cv2.PointPolygonTest(contour, point, false) >= 0).OrderByDescending(contour => Cv2.ContourArea(contour)).FirstOrDefault()
            : null;
        chosen ??= contours.OrderByDescending(contour => Cv2.ContourArea(contour)).First();
        if (Cv2.ContourArea(chosen) < 4) return null;

        // Keep the outline detailed enough to follow the object but with few vertices to edit afterwards.
        var epsilon = Math.Clamp(Cv2.ArcLength(chosen, true) * 0.0025, 0.75, 4.0);
        var simplified = Cv2.ApproxPolyDP(chosen, epsilon, true);
        if (simplified.Length < 3) return null;

        var outline = simplified.Select(point => ToMaster(point, frame)).ToList();
        return PolygonMath.HasSelfIntersection(outline, true)
            ? Cv2.ConvexHull(chosen).Select(point => ToMaster(point, frame)).ToList()
            : outline;
    }

    /// <summary>Contour points are pixel indices; +0.5 moves them to pixel centres, then back to master pixels.</summary>
    private static PixelPoint ToMaster(Point point, Frame frame) =>
        new((point.X + 0.5) / frame.Scale + frame.X, (point.Y + 0.5) / frame.Scale + frame.Y);

    internal static Mat To8BitColor(Mat source)
    {
        var converted = new Mat();
        var channels = source.Channels();
        if (source.Depth() != MatType.CV_8U)
        {
            using var scaled = new Mat();
            Cv2.Normalize(source, scaled, 0, 255, NormTypes.MinMax);
            scaled.ConvertTo(converted, MatType.CV_8U);
        }
        else
        {
            source.CopyTo(converted);
        }

        if (channels == 1)
        {
            var color = new Mat();
            Cv2.CvtColor(converted, color, ColorConversionCodes.GRAY2BGR);
            converted.Dispose();
            return color;
        }

        if (channels == 4)
        {
            var color = new Mat();
            Cv2.CvtColor(converted, color, ColorConversionCodes.BGRA2BGR);
            converted.Dispose();
            return color;
        }

        return converted;
    }
}
