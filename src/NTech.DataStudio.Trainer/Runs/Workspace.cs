using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NTech.DataStudio.Trainer.Catalog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NTech.DataStudio.Trainer.Runs;

public sealed record WorkspaceFile(int Schema, string Name, string DataYaml, string CreatedAt);

public sealed record TrainOptions(int Imgsz, int Epochs, int Batch, string Device, int Workers, int Seed, int Patience, bool Preflight, IReadOnlyDictionary<string, object>? Overrides);

public sealed record RunRecord(int Schema, string Id, string Model, string ModelFamily, string Weights, string License, string Compute, string Image, string ImageDigest, string CreatedAt);

/// <summary>
/// A folder for one problem, holding many runs of many YOLO versions. Files here are the source
/// of truth: the worker writes them even when no app is open. The dataset is only referenced.
/// </summary>
public sealed class Workspace
{
    private const string FileName = "workspace.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private Workspace(string root, WorkspaceFile file)
    {
        Root = root;
        File_ = file;
    }

    public string Root { get; }

    public WorkspaceFile File_ { get; }

    public string RunsFolder => Path.Combine(Root, "runs");

    public string CacheFolder => Path.Combine(Root, "cache");

    public string DatasetFolder => Path.GetDirectoryName(File_.DataYaml)!;

    public string ContainerDataYaml => "/data/" + Path.GetFileName(File_.DataYaml);

    public static Workspace Create(string folder, string dataYaml)
    {
        var root = Path.GetFullPath(folder);
        var data = Path.GetFullPath(dataYaml);
        if (!File.Exists(data)) throw new TrainerException($"Không tìm thấy {data}.");
        if (File.Exists(Path.Combine(root, FileName))) throw new TrainerException($"{root} đã là một workspace.");

        var datasetFolder = Path.GetDirectoryName(data)!;
        if (IsInside(root, datasetFolder))
            throw new TrainerException("Workspace không được nằm trong thư mục dataset: dataset phải giữ nguyên.");

        Directory.CreateDirectory(Path.Combine(root, "runs"));
        Directory.CreateDirectory(Path.Combine(root, "cache"));
        var file = new WorkspaceFile(1, Path.GetFileName(root), data, Now());
        File.WriteAllText(Path.Combine(root, FileName), JsonSerializer.Serialize(file, Json));
        return new Workspace(root, file);
    }

    public static Workspace Open(string folder)
    {
        var root = Path.GetFullPath(folder);
        var path = Path.Combine(root, FileName);
        if (!File.Exists(path)) throw new TrainerException($"{root} chưa phải workspace. Chạy init trước.");
        var file = JsonSerializer.Deserialize<WorkspaceFile>(File.ReadAllText(path), Json)
            ?? throw new TrainerException($"Không đọc được {path}.");
        return new Workspace(root, file);
    }

    public RunFolder CreateRun(ModelVariant model, TrainOptions options, string compute, string image, string digest, DateTime utcNow)
    {
        var id = $"{utcNow:yyyyMMdd-HHmmss}-{model.Name}";
        var folder = Path.Combine(RunsFolder, id);
        if (Directory.Exists(folder)) throw new TrainerException($"Run {id} đã tồn tại.");
        Directory.CreateDirectory(folder);

        var record = new RunRecord(1, id, model.Name, model.Family.Id, model.Weights, model.Family.License, compute, image, digest, Now());
        File.WriteAllText(Path.Combine(folder, "run.json"), JsonSerializer.Serialize(record, Json));
        File.WriteAllText(Path.Combine(folder, "config.yaml"), ConfigYaml(id, model, options, ContainerDataYaml, digest));
        return new RunFolder(folder);
    }

    public IReadOnlyList<RunFolder> Runs() =>
        Directory.Exists(RunsFolder)
            ? Directory.EnumerateDirectories(RunsFolder).Order(StringComparer.Ordinal).Select(path => new RunFolder(path)).ToList()
            : [];

    public RunFolder Run(string id)
    {
        var path = Path.Combine(RunsFolder, id);
        if (!File.Exists(Path.Combine(path, "run.json"))) throw new TrainerException($"Không có run {id}.");
        return new RunFolder(path);
    }

    public static string ConfigYaml(string id, ModelVariant model, TrainOptions options, string containerData, string digest)
    {
        var config = new Dictionary<string, object?>
        {
            ["run_id"] = id,
            ["model"] = model.Name,
            ["model_family"] = model.Family.Id,
            ["weights"] = model.Weights,
            ["license"] = model.Family.License,
            ["data"] = containerData,
            ["imgsz"] = options.Imgsz,
            ["epochs"] = options.Epochs,
            ["batch"] = options.Batch,
            ["device"] = options.Device,
            ["workers"] = options.Workers,
            ["seed"] = options.Seed,
            ["patience"] = options.Patience,
            ["preflight"] = options.Preflight,
            ["overrides"] = options.Overrides ?? new Dictionary<string, object>(),
            ["onnx"] = new Dictionary<string, object> { ["nms"] = model.Family.OnnxNms },
            ["image_digest"] = digest
        };
        return new SerializerBuilder().WithNamingConvention(NullNamingConvention.Instance).Build().Serialize(config);
    }

    private static bool IsInside(string candidate, string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var parent = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return (Path.TrimEndingDirectorySeparator(candidate) + Path.DirectorySeparatorChar).StartsWith(parent, comparison);
    }

    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    internal static JsonSerializerOptions JsonOptions => Json;
}

public sealed class RunFolder(string path)
{
    public string Path_ => path;

    public string Id => Path.GetFileName(path);

    public string EventsFile => Path.Combine(path, "events.jsonl");

    public string ResultFile => Path.Combine(path, "result.json");

    public string ReportFile => Path.Combine(path, "exports", "export-report.json");

    public bool HasCheckpoint =>
        File.Exists(Path.Combine(path, "checkpoints", "resume.pt"))
        || File.Exists(Path.Combine(path, "train", "weights", "last.pt"));

    public bool HasBest => File.Exists(Path.Combine(path, "train", "weights", "best.pt"));

    public RunRecord Record() =>
        JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(Path.Combine(path, "run.json")), Workspace.JsonOptions)
        ?? throw new TrainerException($"Không đọc được run.json của {Id}.");

    /// <summary>The worker reads this at the end of each epoch. Written atomically so it never sees half a file.</summary>
    public void RequestStop() => WriteControl("{\"stop\": true}");

    public void ClearStop() => WriteControl("{\"stop\": false}");

    public string? LastState()
    {
        string? state = null;
        var reader = new EventLogReader(EventsFile);
        foreach (var item in reader.ReadNew())
            if (item.Type == "state") state = item.Text("state");
        return state;
    }

    private void WriteControl(string json)
    {
        var target = Path.Combine(path, "control.json");
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, target, overwrite: true);
    }
}

public sealed class TrainerException(string message) : Exception(message);
