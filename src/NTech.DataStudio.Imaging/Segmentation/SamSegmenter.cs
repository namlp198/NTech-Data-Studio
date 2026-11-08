using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>
/// Segment Anything (MobileSAM, ONNX) on the CPU: the encoder analyses the whole picture once, and the decoder then
/// turns points into a mask in about a tenth of a second (decision 0016). Works on a copy of the picture whose long side is
/// 1024 px, so a 20 MP master costs no more than a small one; outlines are scaled back to master pixels.
/// The two model files are not part of the app: they live in <see cref="ModelFolder"/> (MobileSAM, Apache-2.0).
/// </summary>
public sealed class SamSegmenter : ISmartSegmenter, IDisposable
{
    public const string EncoderFile = "mobile_sam_image_encoder.onnx";
    public const string DecoderFile = "sam_mask_decoder_single.onnx";

    /// <summary>The size the encoder was trained for: the long side of the picture it is given.</summary>
    private const int EncoderSide = 1024;

    private const int MaskSize = 256;

    private readonly object _gate = new();
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;

    private string? _cachedPath;
    private DenseTensor<float>? _embeddings;
    private double _scale;
    private int _scaledWidth;
    private int _scaledHeight;

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

    public Task PrepareAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(imagePath, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<PixelPoint>?> SegmentAsync(
        string imagePath, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken = default) =>
        Task.Run(() => Segment(imagePath, prompts, cancellationToken), cancellationToken);

    public void Dispose()
    {
        lock (_gate)
        {
            _encoder?.Dispose();
            _decoder?.Dispose();
            _encoder = null;
            _decoder = null;
            _embeddings = null;
        }
    }

    private void Prepare(string imagePath, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            PrepareLocked(imagePath, cancellationToken);
        }
    }

    private void PrepareLocked(string imagePath, CancellationToken cancellationToken)
    {
        if (_embeddings is not null && _cachedPath == imagePath) return;

        if (Unavailable is { } why) throw new InvalidOperationException(why);
        cancellationToken.ThrowIfCancellationRequested();

        _encoder ??= new InferenceSession(Path.Combine(ModelFolder, EncoderFile));
        _decoder ??= new InferenceSession(Path.Combine(ModelFolder, DecoderFile));

        // Read through bytes: OpenCV's own file functions cannot open a Vietnamese path (CLAUDE.md).
        using var source = Cv2.ImDecode(File.ReadAllBytes(imagePath), ImreadModes.AnyDepth | ImreadModes.AnyColor);
        if (source.Empty())
            throw new InvalidOperationException($"Không đọc được ảnh {Path.GetFileName(imagePath)}.");

        using var color8 = To8BitColor(source);
        var scale = (double)EncoderSide / Math.Max(color8.Width, color8.Height);
        var width = Math.Max(1, (int)Math.Round(color8.Width * scale));
        var height = Math.Max(1, (int)Math.Round(color8.Height * scale));
        using var resized = new Mat();
        Cv2.Resize(color8, resized, new Size(width, height), 0, 0, InterpolationFlags.Linear);

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

        _embeddings = results.First().AsTensor<float>().ToDenseTensor();
        _cachedPath = imagePath;
        _scale = scale;
        _scaledWidth = width;
        _scaledHeight = height;
    }

    private IReadOnlyList<PixelPoint>? Segment(string imagePath, IReadOnlyList<SegmentPrompt> prompts, CancellationToken cancellationToken)
    {
        if (prompts.Count == 0) return null;

        lock (_gate)
        {
            PrepareLocked(imagePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Points in the encoder's frame, plus the padding point (label -1) the decoder expects when no box is given.
            var count = prompts.Count + 1;
            var coordinates = new float[count * 2];
            var labels = new float[count];
            for (var index = 0; index < prompts.Count; index++)
            {
                coordinates[index * 2] = (float)(prompts[index].Position.X * _scale);
                coordinates[index * 2 + 1] = (float)(prompts[index].Position.Y * _scale);
                labels[index] = prompts[index].Positive ? 1 : 0;
            }

            labels[count - 1] = -1;

            using var results = _decoder!.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", _embeddings!),
                NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coordinates, new[] { 1, count, 2 })),
                NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(labels, new[] { 1, count })),
                NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>(new float[MaskSize * MaskSize], new[] { 1, 1, MaskSize, MaskSize })),
                NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>(new float[] { 0 }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new float[] { _scaledHeight, _scaledWidth }, new[] { 2 }))
            });

            var masks = results.First(result => result.Name == "masks").AsTensor<float>();
            return OutlineOf(masks, prompts);
        }
    }

    /// <summary>The outline of the part of the mask the first positive point lies in (else the biggest part), in master pixels.</summary>
    private IReadOnlyList<PixelPoint>? OutlineOf(Tensor<float> masks, IReadOnlyList<SegmentPrompt> prompts)
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

        var anchor = prompts.FirstOrDefault(prompt => prompt.Positive) is { } positive
            ? new Point2f((float)(positive.Position.X * _scale), (float)(positive.Position.Y * _scale))
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

        // Contour points are pixel indices; +0.5 moves them to pixel centres, then back to master pixels.
        var outline = simplified
            .Select(point => new PixelPoint((point.X + 0.5) / _scale, (point.Y + 0.5) / _scale))
            .ToList();
        return PolygonMath.HasSelfIntersection(outline, true) ? ConvexOutline(chosen) : outline;
    }

    /// <summary>The rare simplified outline that crosses itself: fall back to the object's convex hull, which never does.</summary>
    private IReadOnlyList<PixelPoint> ConvexOutline(Point[] contour) =>
        Cv2.ConvexHull(contour)
            .Select(point => new PixelPoint((point.X + 0.5) / _scale, (point.Y + 0.5) / _scale))
            .ToList();

    private static Mat To8BitColor(Mat source)
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
