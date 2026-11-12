using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NTech.DataStudio.Application.Abstractions;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>
/// Runs a Trainer segment ONNX on the CPU (decision 0017). Everything it needs to know about the model comes from the
/// <c>model.meta.json</c> beside it: input size, class names and which tensors hold the detections and the prototypes.
/// A file whose meta says it is not a segment model with embedded NMS is refused instead of read wrongly.
/// </summary>
public sealed class OnnxAutoLabeler : IAutoLabeler, IDisposable
{
    public const string MetaFile = "model.meta.json";
    private const string SegmentKind = "detections_xyxy_conf_cls_mask_coefficients";

    private sealed record Loaded(InferenceSession Session, int Size, string[] Classes, string Detections, string Prototypes, string Input);

    private readonly object _gate = new();
    private Loaded? _loaded;
    private string? _problem;
    private bool _tried;

    public string? ModelPath { get; private set; }

    public string? Unavailable
    {
        get
        {
            lock (_gate)
            {
                if (ModelPath is null) return "Chưa chọn model gán nhãn tự động (file .onnx do Trainer xuất).";
                return TryLoad() is null ? _problem : null;
            }
        }
    }

    public IReadOnlyList<string> ClassNames
    {
        get
        {
            lock (_gate) return TryLoad()?.Classes ?? Array.Empty<string>();
        }
    }

    public void SetModel(string? onnxPath)
    {
        lock (_gate)
        {
            onnxPath = string.IsNullOrWhiteSpace(onnxPath) ? null : onnxPath;
            if (string.Equals(onnxPath, ModelPath, StringComparison.OrdinalIgnoreCase)) return;
            Release();
            ModelPath = onnxPath;
        }
    }

    public Task<IReadOnlyList<AutoLabel>> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(imagePath, minScore, cancellationToken), cancellationToken);

    public void Dispose()
    {
        lock (_gate) Release();
    }

    private void Release()
    {
        _loaded?.Session.Dispose();
        _loaded = null;
        _problem = null;
        _tried = false;
    }

    private Loaded? TryLoad()
    {
        if (_loaded is not null || _tried || ModelPath is null) return _loaded;
        _tried = true;
        try
        {
            _loaded = Load(ModelPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or OnnxRuntimeException or InvalidDataException or UnauthorizedAccessException)
        {
            _problem = exception.Message;
        }

        return _loaded;
    }

    private static Loaded Load(string onnxPath)
    {
        if (!File.Exists(onnxPath))
            throw new InvalidDataException($"Không thấy file model {onnxPath}.");
        var metaPath = Path.Combine(Path.GetDirectoryName(onnxPath) ?? string.Empty, MetaFile);
        if (!File.Exists(metaPath))
            throw new InvalidDataException($"Thiếu {MetaFile} cạnh file model. Hãy chọn file .onnx trong thư mục exports của Trainer.");

        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("task", out var task) || task.GetString() != "segment")
            throw new InvalidDataException("Model này không phải model segment. Chỉ model segment gán được polygon.");

        var output = root.GetProperty("output");
        if (output.GetProperty("kind").GetString() != SegmentKind)
            throw new InvalidDataException("Đầu ra của model không phải dạng segment mà Data Studio đọc được.");
        if (!output.GetProperty("nms_embedded").GetBoolean())
            throw new InvalidDataException("Model không nhúng NMS nên chưa đọc được.");

        var classes = root.GetProperty("classes").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        var input = root.GetProperty("input");
        var size = input.GetProperty("shape")[2].GetInt32();
        if (size < 32 || size > 4096)
            throw new InvalidDataException($"Cỡ đầu vào {size} không hợp lý.");

        var session = new InferenceSession(onnxPath);
        return new Loaded(
            session, size, classes,
            output.GetProperty("detections_tensor").GetString()!,
            output.GetProperty("prototypes_tensor").GetString()!,
            input.GetProperty("name").GetString()!);
    }

    private IReadOnlyList<AutoLabel> Detect(string imagePath, double minScore, CancellationToken cancellationToken)
    {
        Loaded loaded;
        lock (_gate)
        {
            loaded = TryLoad() ?? throw new InvalidOperationException(Unavailable);
        }

        // Read through bytes: OpenCV's own file functions cannot open a Vietnamese path (CLAUDE.md).
        using var raw = Cv2.ImDecode(File.ReadAllBytes(imagePath), ImreadModes.AnyDepth | ImreadModes.AnyColor);
        if (raw.Empty())
            throw new InvalidOperationException($"Không đọc được ảnh {Path.GetFileName(imagePath)}.");
        using var bgr = SamSegmenter.To8BitColor(raw);
        cancellationToken.ThrowIfCancellationRequested();

        var frame = LetterboxFrame.For(bgr.Width, bgr.Height, loaded.Size);
        var tensor = new DenseTensor<float>(Letterbox(bgr, frame), new[] { 1, 3, loaded.Size, loaded.Size });

        float[] rows;
        int rowCount, columns;
        float[] prototypes;
        int protoHeight, protoWidth;
        lock (loaded.Session)
        {
            using var results = loaded.Session.Run(new[] { NamedOnnxValue.CreateFromTensor(loaded.Input, tensor) });
            var detections = results.First(result => result.Name == loaded.Detections).AsTensor<float>();
            var protos = results.First(result => result.Name == loaded.Prototypes).AsTensor<float>();
            var d = detections.Dimensions.ToArray();
            var p = protos.Dimensions.ToArray();
            if (d.Length != 3 || p.Length != 4 || d[2] - 6 != p[1])
                throw new InvalidOperationException("Đầu ra của model không khớp hợp đồng trong model.meta.json.");

            rowCount = d[1];
            columns = d[2];
            rows = detections.ToArray();
            prototypes = protos.ToArray();
            protoHeight = p[2];
            protoWidth = p[3];
        }

        cancellationToken.ThrowIfCancellationRequested();
        return YoloSegDecoder.Decode(rows, rowCount, columns, prototypes, protoHeight, protoWidth, frame, minScore)
            .Where(item => item.ClassId >= 0 && item.ClassId < loaded.Classes.Length)
            .Select(item => new AutoLabel(loaded.Classes[item.ClassId], item.Score, item.Outline))
            .ToList();
    }

    /// <summary>Fit, centre, pad with 114, RGB, 0..1, NCHW: what the Trainer's own check does.</summary>
    internal static float[] Letterbox(Mat bgr, LetterboxFrame frame)
    {
        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(frame.NewWidth, frame.NewHeight), 0, 0, InterpolationFlags.Linear);
        using var canvas = new Mat(frame.Size, frame.Size, MatType.CV_8UC3, new Scalar(114, 114, 114));
        using (var target = new Mat(canvas, new OpenCvSharp.Rect(frame.Left, frame.Top, frame.NewWidth, frame.NewHeight)))
            resized.CopyTo(target);

        var plane = frame.Size * frame.Size;
        // Mat.GetArray<byte> refuses a three-channel Mat; Vec3b is one pixel (B, G, R).
        canvas.GetArray(out Vec3b[] pixels);
        var result = new float[plane * 3];
        for (var index = 0; index < plane; index++)
        {
            result[index] = pixels[index].Item2 / 255f;
            result[plane + index] = pixels[index].Item1 / 255f;
            result[plane * 2 + index] = pixels[index].Item0 / 255f;
        }

        return result;
    }
}
