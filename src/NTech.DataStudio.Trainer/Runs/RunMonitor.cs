using System.Globalization;

namespace NTech.DataStudio.Trainer.Runs;

public enum LogLevel
{
    Info,
    Warning,
    Error
}

public sealed record LogLine(string Timestamp, LogLevel Level, string Text);

public sealed record EpochPoint(int Epoch, double? TrainLoss, double? ValLoss, double? Map50, double? Map50To95, double? Precision, double? Recall);

public sealed record ResourceSample(double? CpuPercent, double? RamUsedGb, double? RamTotalGb, double? DiskFreeGb, double? GpuPercent, double? GpuTempC, double? VramUsedGb, double? VramTotalGb);

/// <summary>
/// Folds the events of one run into what a screen shows. Pure: it only reads events, so the CLI,
/// the Desktop and the tests see the same thing for the same log.
/// </summary>
public sealed class RunMonitor
{
    private readonly List<EpochPoint> _epochs = [];
    private readonly List<LogLine> _log = [];

    public string? LastState { get; private set; }

    public int Epoch { get; private set; }

    public int Epochs { get; private set; }

    public int Batch { get; private set; }

    public int Batches { get; private set; }

    public double? EtaSeconds { get; private set; }

    public double? VramPeakGb { get; private set; }

    public string? FirstTimestamp { get; private set; }

    public string? LastTimestamp { get; private set; }

    public ResourceSample? Resources { get; private set; }

    public string? ExportStage { get; private set; }

    public string? ExportStatus { get; private set; }

    public IReadOnlyList<EpochPoint> EpochHistory => _epochs;

    public IReadOnlyList<LogLine> Log => _log;

    public int WarningCount => _log.Count(line => line.Level == LogLevel.Warning);

    public int ErrorCount => _log.Count(line => line.Level == LogLevel.Error);

    public EpochPoint? Latest => _epochs.Count > 0 ? _epochs[^1] : null;

    public void Apply(IEnumerable<TrainerEvent> events)
    {
        foreach (var item in events) Apply(item);
    }

    public void Apply(TrainerEvent item)
    {
        FirstTimestamp ??= item.Timestamp;
        LastTimestamp = item.Timestamp;
        switch (item.Type)
        {
            case "state":
                LastState = item.Text("state");
                break;
            case "progress":
                Epoch = Whole(item.Number("epoch")) ?? Epoch;
                Epochs = Whole(item.Number("epochs")) ?? Epochs;
                Batch = Whole(item.Number("batch")) ?? Batch;
                Batches = Whole(item.Number("batches")) ?? Batches;
                break;
            case "epoch_end":
                var epoch = Whole(item.Number("epoch")) ?? 0;
                Epoch = epoch;
                Epochs = Whole(item.Number("epochs")) ?? Epochs;
                Batch = Batches;
                EtaSeconds = item.Number("eta_seconds");
                if (item.Number("vram_peak_gb") is { } peak) VramPeakGb = Math.Max(VramPeakGb ?? 0, peak);
                // A resumed run repeats nothing, but a redone epoch must replace, not duplicate.
                _epochs.RemoveAll(point => point.Epoch == epoch);
                _epochs.Add(new EpochPoint(epoch, item.Number("train_loss"), item.Number("val_loss"), item.Number("map50"),
                    item.Number("map50_95"), item.Number("precision"), item.Number("recall")));
                break;
            case "resource":
                Resources = ReadResources(item);
                break;
            case "export":
                ExportStage = item.Text("stage");
                ExportStatus = null;
                break;
            case "export_result":
                ExportStatus = item.Text("status");
                ExportStage = null;
                break;
        }

        if (EventText.Describe(item) is { } line) _log.Add(line);
    }

    public TimeSpan? Elapsed()
    {
        if (!TryTime(FirstTimestamp, out var first) || !TryTime(LastTimestamp, out var last)) return null;
        return last - first;
    }

    private static ResourceSample ReadResources(TrainerEvent item)
    {
        double? gpuPercent = null, temp = null, vramUsed = null, vramTotal = null;
        if (item.Data.TryGetProperty("gpu", out var gpu) && gpu.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            gpuPercent = Number(gpu, "percent");
            temp = Number(gpu, "temp_c");
            vramUsed = Number(gpu, "vram_used_mb") / 1024;
            vramTotal = Number(gpu, "vram_total_mb") / 1024;
        }

        return new ResourceSample(item.Number("cpu_percent"), item.Number("ram_used_gb"), item.Number("ram_total_gb"),
            item.Number("disk_free_gb"), gpuPercent, temp, vramUsed, vramTotal);
    }

    private static double? Number(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.GetDouble() : null;

    private static int? Whole(double? value) => value is { } number ? (int)Math.Round(number) : null;

    private static bool TryTime(string? text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
}

/// <summary>One wording for every event, shared by the CLI output and the Desktop log.</summary>
public static class EventText
{
    public static LogLine? Describe(TrainerEvent item)
    {
        var text = item.Type switch
        {
            "state" => $"Trạng thái: {item.Text("state")}",
            "epoch_end" => $"Epoch {item.Number("epoch")}/{item.Number("epochs")}: train loss {F(item.Number("train_loss"))}, val loss {F(item.Number("val_loss"))}, " +
                           $"mAP50 {F(item.Number("map50"))}, mAP50-95 {F(item.Number("map50_95"))}, còn khoảng {Duration(item.Number("eta_seconds"))}",
            "preflight" => $"Chạy thử: {F(item.Number("seconds_per_batch"), "0.##")} s/batch, ước tính {F(item.Number("train_seconds_per_epoch_estimate"), "0")} s/epoch (chưa tính val), " +
                           $"VRAM đỉnh {(item.Number("vram_peak_gb") is { } peak ? peak.ToString("0.##", CultureInfo.InvariantCulture) + " GB" : "không có")}, batch {item.Number("batch")}",
            "warning" or "error" => item.Text("message") ?? "",
            "checkpoint_saved" when item.Text("kind") == "resume" => $"Đã lưu checkpoint để train tiếp, epoch {item.Number("epoch")}",
            "export" => $"Export {item.Text("format")}: {item.Text("stage")}",
            "export_result" => $"Export {item.Text("format")}: {item.Text("status")}" + Problems(item),
            _ => null
        };
        if (text is null) return null;
        var level = item.Type switch
        {
            "warning" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "export_result" when item.Text("status") == "failed" => LogLevel.Error,
            "state" when item.Text("state") == RunStates.Failed => LogLevel.Error,
            _ => LogLevel.Info
        };
        return new LogLine(item.Timestamp, level, text);
    }

    public static string Duration(double? seconds) =>
        TimeSpan.FromSeconds(seconds ?? 0).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static string Problems(TrainerEvent item)
    {
        if (!item.Data.TryGetProperty("problems", out var problems) || problems.ValueKind != System.Text.Json.JsonValueKind.Array) return "";
        var list = problems.EnumerateArray().Select(problem => problem.GetString()).Where(text => !string.IsNullOrEmpty(text)).ToList();
        return list.Count == 0 ? "" : ". " + string.Join("; ", list);
    }

    private static string F(double? value, string format = "0.###") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "-";
}
