using System.Text.Json;
using NTech.DataStudio.Trainer.Containers;

namespace NTech.DataStudio.Trainer.Runs;

public sealed record DatasetIssue(string Code, string Message);

public sealed record SplitCount(string Split, int Images, int Boxes);

public sealed record ClassCount(string Name, int Boxes);

/// <summary>What the worker's <c>validate</c> reports (design section 6.2). Raw keeps the stats for printing as they are.</summary>
public sealed record DatasetReport(
    bool Ok,
    IReadOnlyList<DatasetIssue> Errors,
    IReadOnlyList<DatasetIssue> Warnings,
    IReadOnlyList<SplitCount> Splits,
    IReadOnlyList<ClassCount> Classes,
    int Imgsz,
    double? MinSideP5,
    double? MinSideP50,
    string Fingerprint,
    bool Synthetic,
    JsonElement Stats)
{
    public static DatasetReport Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var stats = root.TryGetProperty("stats", out var value) ? value.Clone() : default;

        var splits = new List<SplitCount>();
        var classes = new List<ClassCount>();
        int imgsz = 0;
        double? p5 = null, p50 = null;
        if (stats.ValueKind == JsonValueKind.Object)
        {
            if (stats.TryGetProperty("splits", out var splitTable))
                foreach (var split in splitTable.EnumerateObject())
                    splits.Add(new SplitCount(split.Name, Int(split.Value, "images"), Int(split.Value, "boxes")));
            if (stats.TryGetProperty("per_class", out var perClass))
                foreach (var item in perClass.EnumerateObject())
                    classes.Add(new ClassCount(item.Name, item.Value.GetInt32()));
            imgsz = Int(stats, "imgsz");
            if (stats.TryGetProperty("min_side_px_at_imgsz", out var sides) && sides.ValueKind == JsonValueKind.Object)
            {
                p5 = sides.GetProperty("p5").GetDouble();
                p50 = sides.GetProperty("p50").GetDouble();
            }
        }

        return new DatasetReport(
            root.GetProperty("ok").GetBoolean(),
            Issues(root, "errors"),
            Issues(root, "warnings"),
            splits,
            classes,
            imgsz,
            p5,
            p50,
            root.TryGetProperty("fingerprint", out var print) ? print.GetString() ?? "" : "",
            root.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True,
            stats);
    }

    private static List<DatasetIssue> Issues(JsonElement root, string name) =>
        root.TryGetProperty(name, out var list)
            ? list.EnumerateArray().Select(item => new DatasetIssue(item.GetProperty("code").GetString() ?? "", item.GetProperty("message").GetString() ?? "")).ToList()
            : [];

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}

/// <summary>Runs the worker's dataset check in a short container. The dataset is mounted read-only.</summary>
public sealed class DatasetChecker(IContainerRuntime containers)
{
    public async Task<DatasetReport> CheckAsync(Workspace workspace, string image, int imgsz, CancellationToken cancellationToken = default)
    {
        if (await containers.ImageDigestAsync(image, cancellationToken) is null)
            throw new TrainerException($"Không có image {image}. Build bằng docker/trainer/Dockerfile.");
        var result = await containers.RunToEndAsync(JobService.ValidateSpec(workspace, image, imgsz), cancellationToken);
        if (string.IsNullOrWhiteSpace(result.Output))
            throw new ContainerException("Không kiểm tra được dataset: " + (string.IsNullOrWhiteSpace(result.Error) ? "worker không trả kết quả." : result.Error.Trim()));
        try
        {
            return DatasetReport.Parse(result.Output);
        }
        catch (Exception problem) when (problem is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ContainerException("Không đọc được kết quả kiểm tra dataset: " + problem.Message);
        }
    }
}
