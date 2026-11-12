using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NTech.DataStudio.Application.Abstractions;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>
/// Runs a Trainer detect ONNX on the CPU (decision 0018). Like <see cref="OnnxAutoLabeler"/> it learns everything about
/// the model from the <c>model.meta.json</c> beside it, and refuses a file that is not a detect model with embedded NMS
/// instead of reading it wrongly.
/// </summary>
public sealed class OnnxBoxDetector : IBoxDetector, IDisposable
{
    private const string DetectKind = "detections_xyxy_conf_cls";

    private sealed record Loaded(InferenceSession Session, int Size, string[] Classes, string Detections, string Input);

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
                if (ModelPath is null) return "Chưa chọn model detect (file .onnx do Trainer xuất).";
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

    public Task<BoxDetection> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default) =>
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
        catch (Exception exception) when (exception is IOException or JsonException or OnnxRuntimeException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException)
        {
            _problem = exception.Message;
        }

        return _loaded;
    }

    private static Loaded Load(string onnxPath)
    {
        if (!File.Exists(onnxPath))
            throw new InvalidDataException($"Không thấy file model {onnxPath}.");
        var metaPath = Path.Combine(Path.GetDirectoryName(onnxPath) ?? string.Empty, OnnxAutoLabeler.MetaFile);
        if (!File.Exists(metaPath))
            throw new InvalidDataException($"Thiếu {OnnxAutoLabeler.MetaFile} cạnh file model. Hãy chọn file .onnx trong thư mục exports của Trainer.");

        using var document = JsonDocument.Parse(File.ReadAllText(metaPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("task", out var task) || task.GetString() != "detect")
            throw new InvalidDataException("Model này không phải model detect. Chọn file .onnx của model detect, hoặc đổi sang cách gán bằng model segment.");

        var output = root.GetProperty("output");
        if (output.GetProperty("kind").GetString() != DetectKind)
            throw new InvalidDataException("Đầu ra của model không phải dạng detect mà Data Studio đọc được.");
        if (!output.GetProperty("nms_embedded").GetBoolean())
            throw new InvalidDataException("Model không nhúng NMS nên chưa đọc được.");

        var classes = root.GetProperty("classes").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        var input = root.GetProperty("input");
        var size = input.GetProperty("shape")[2].GetInt32();
        if (size < 32 || size > 4096)
            throw new InvalidDataException($"Cỡ đầu vào {size} không hợp lý.");

        var session = new InferenceSession(onnxPath);
        // A detect export has the one output; the meta names it only for segment models.
        var detections = output.TryGetProperty("detections_tensor", out var named) && named.GetString() is { } name
            ? name
            : session.OutputMetadata.Keys.First();
        return new Loaded(session, size, classes, detections, input.GetProperty("name").GetString()!);
    }

    private BoxDetection Detect(string imagePath, double minScore, CancellationToken cancellationToken)
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
        var tensor = new DenseTensor<float>(OnnxAutoLabeler.Letterbox(bgr, frame), new[] { 1, 3, loaded.Size, loaded.Size });

        float[] rows;
        int rowCount;
        lock (loaded.Session)
        {
            using var results = loaded.Session.Run(new[] { NamedOnnxValue.CreateFromTensor(loaded.Input, tensor) });
            var detections = results.First(result => result.Name == loaded.Detections).AsTensor<float>();
            var d = detections.Dimensions.ToArray();
            if (d.Length != 3 || d[2] != YoloBoxDecoder.Columns)
                throw new InvalidOperationException("Đầu ra của model không khớp hợp đồng trong model.meta.json.");

            rowCount = d[1];
            rows = detections.ToArray();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var boxes = YoloBoxDecoder.Decode(rows, rowCount, YoloBoxDecoder.Columns, frame, minScore)
            .Where(item => item.ClassId >= 0 && item.ClassId < loaded.Classes.Length)
            .Select(item => new DetectedBox(loaded.Classes[item.ClassId], item.Score, item.Box))
            .ToList();
        return new BoxDetection(bgr.Width, bgr.Height, boxes);
    }
}
