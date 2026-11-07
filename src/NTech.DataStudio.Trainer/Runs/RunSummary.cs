using System.Globalization;
using System.Text.Json;
using NTech.DataStudio.Trainer.Hardware;
using YamlDotNet.Serialization;

namespace NTech.DataStudio.Trainer.Runs;

public sealed record Metrics(double? Precision, double? Recall, double? Map50, double? Map50To95);

public sealed record ClassMetrics(string Name, Metrics Metrics);

public sealed record ExportSummary(
    string Status,
    IReadOnlyList<string> Problems,
    int BoxesCompared,
    int BoxesWithoutPair,
    double? HighestScoreWithoutPair,
    double? MinBoxIou,
    double? LatencyMsMedian,
    string? LatencyDevice,
    double? FileMb,
    string Folder);

/// <summary>Settings that decide whether two runs can be compared (design section 5.2).</summary>
public sealed record RunSettings(int? Imgsz, int? Epochs, int? Seed, string? Fingerprint);

/// <summary>Everything the results table needs about one run, read from its files.</summary>
public sealed record RunSummary(
    RunRecord Record,
    string State,
    RunSettings Settings,
    bool Synthetic,
    bool HasBest,
    bool HasCheckpoint,
    Metrics? Overall,
    IReadOnlyList<ClassMetrics> Classes,
    ExportSummary? Export)
{
    public string Id => Record.Id;

    public bool CanExport => HasBest && !JobRunning(State);

    public bool CanResume => RunStates.CanResume(State, HasCheckpoint);

    public bool IsRunning => JobRunning(State);

    public static bool JobRunning(string state) => state is RunStates.Preparing or RunStates.Preflight or RunStates.Running or RunStates.Evaluating;

    public static RunSummary Read(RunFolder run, string state)
    {
        var result = JsonFiles.Read(run.ResultFile);
        var report = JsonFiles.Read(run.ReportFile);
        var dataset = JsonFiles.Read(Path.Combine(run.Path_, "dataset.json"));

        var classes = new List<ClassMetrics>();
        if (result is { } value && value.TryGetProperty("classes", out var list))
            foreach (var item in list.EnumerateArray())
                classes.Add(new ClassMetrics(item.GetProperty("name").GetString() ?? "", MetricsOf(item)));

        return new RunSummary(
            run.Record(),
            state,
            ReadSettings(run, dataset),
            dataset is { } data && data.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True,
            run.HasBest,
            run.HasCheckpoint,
            result is { } overall ? MetricsOf(overall) : null,
            classes,
            report is { } exported ? ExportOf(exported, Path.GetDirectoryName(run.ReportFile)!) : null);
    }

    /// <summary>Reasons why these runs are not directly comparable; empty when they are.</summary>
    public static IReadOnlyList<string> Differences(IEnumerable<RunSummary> runs)
    {
        var list = runs.ToList();
        var reasons = new List<string>();
        Check(list.Select(run => run.Settings.Fingerprint), "dataset", reasons);
        Check(list.Select(run => run.Settings.Imgsz?.ToString(CultureInfo.InvariantCulture)), "imgsz", reasons);
        Check(list.Select(run => run.Settings.Epochs?.ToString(CultureInfo.InvariantCulture)), "số epoch", reasons);
        Check(list.Select(run => run.Settings.Seed?.ToString(CultureInfo.InvariantCulture)), "seed", reasons);
        return reasons;
    }

    private static void Check(IEnumerable<string?> values, string what, List<string> reasons)
    {
        if (values.Where(value => value is not null).Distinct(StringComparer.Ordinal).Count() > 1)
            reasons.Add($"Các run khác {what}.");
    }

    private static RunSettings ReadSettings(RunFolder run, JsonElement? dataset)
    {
        int? imgsz = null, epochs = null, seed = null;
        var config = Path.Combine(run.Path_, "config.yaml");
        if (File.Exists(config))
        {
            var values = new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(File.ReadAllText(config));
            imgsz = Whole(values.GetValueOrDefault("imgsz"));
            epochs = Whole(values.GetValueOrDefault("epochs"));
            seed = Whole(values.GetValueOrDefault("seed"));
        }

        var fingerprint = dataset is { } data && data.TryGetProperty("fingerprint", out var print) ? print.GetString() : null;
        return new RunSettings(imgsz, epochs, seed, string.IsNullOrEmpty(fingerprint) ? null : fingerprint);
    }

    private static int? Whole(object? value) =>
        int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static Metrics MetricsOf(JsonElement element) => new(
        JsonFiles.Number(element, "precision"),
        JsonFiles.Number(element, "recall"),
        JsonFiles.Number(element, "map50"),
        JsonFiles.Number(element, "map50_95"));

    private static ExportSummary ExportOf(JsonElement report, string folder) => new(
        report.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
        report.TryGetProperty("problems", out var problems) && problems.ValueKind == JsonValueKind.Array
            ? problems.EnumerateArray().Select(item => item.GetString() ?? "").ToList()
            : [],
        (int)(JsonFiles.Number(report, "boxes_compared") ?? 0),
        (int)(JsonFiles.Number(report, "boxes_without_pair") ?? 0),
        JsonFiles.Number(report, "highest_score_without_pair"),
        JsonFiles.Number(report, "min_box_iou"),
        JsonFiles.Number(report, "latency_ms_median"),
        report.TryGetProperty("latency_device", out var device) ? device.GetString() : null,
        JsonFiles.Number(report, "file_mb"),
        folder);
}

public static class JsonFiles
{
    /// <summary>Null when the file is missing or half written by a worker that is still busy.</summary>
    public static JsonElement? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.Clone();
        }
        catch (Exception problem) when (problem is JsonException or IOException)
        {
            return null;
        }
    }

    public static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}

/// <summary>Defaults for a new run, shared by the CLI and the Desktop so both start the same job.</summary>
public static class TrainPlanner
{
    public const int DefaultImgsz = 640;
    public const int DefaultEpochs = 100;
    public const int DefaultPatience = 100;
    public const int AutoBatch = -1;
    public const int CpuBatch = 4;

    public static string ComputeFor(HardwareSnapshot machine) => Assessor.UsableGpu(machine) is null ? "cpu" : "cuda";

    public static int DefaultBatch(string compute) => compute == "cuda" ? AutoBatch : CpuBatch;

    public static string DeviceFor(string compute) => compute == "cuda" ? "0" : "cpu";

    public static int DefaultWorkers(HardwareSnapshot machine) => Math.Min(8, Math.Max(1, machine.CpuThreads / 2));
}
