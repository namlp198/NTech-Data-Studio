using System.Text.Json;
using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Desktop.ViewModels;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Processes;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.Tests;

/// <summary>Docker as the tests want it: which runs have a container, what validate prints, what was started.</summary>
public sealed class FakeContainers : IContainerRuntime
{
    public DockerInfo Info { get; set; } = new(true, "29.8.0", true, 16, 32, null);

    public string? Digest { get; set; } = "sha256:test";

    public HashSet<string> Running { get; } = new(StringComparer.Ordinal);

    public List<ContainerSpec> Started { get; } = [];

    public string ValidateOutput { get; set; } = "";

    public Task<DockerInfo> InfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Info);

    public Task<string?> ImageDigestAsync(string image, CancellationToken cancellationToken = default) => Task.FromResult(Digest);

    public Task StartDetachedAsync(ContainerSpec spec, CancellationToken cancellationToken = default)
    {
        Started.Add(spec);
        if (spec.Labels.TryGetValue(JobService.RunLabel, out var run)) Running.Add(run);
        return Task.CompletedTask;
    }

    public Task<ProcessResult> RunToEndAsync(ContainerSpec spec, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProcessResult(0, spec.Command[0] == "validate" ? ValidateOutput : "{\"torch\": \"2.15\"}", ""));

    public Task<bool> IsRunningAsync(string label, string value, CancellationToken cancellationToken = default) => Task.FromResult(Running.Contains(value));

    public Task<IReadOnlySet<string>> RunningLabelValuesAsync(string label, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(Running));

    public Task StopAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>nvidia-smi: one line per GPU, or not installed.</summary>
public sealed class FakeRunner : IProcessRunner
{
    public string? NvidiaSmi { get; set; }

    public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(NvidiaSmi is null ? new ProcessResult(ProcessRunner.NotFound, "", "") : new ProcessResult(0, NvidiaSmi, ""));
}

public sealed class FakeDialogs : IDialogs
{
    public string? DataYaml { get; set; }

    public Queue<string> Folders { get; } = new();

    public List<string> Opened { get; } = [];

    public Task<string?> PickDataYamlAsync() => Task.FromResult(DataYaml);

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

    public Task OpenFolderAsync(string path)
    {
        Opened.Add(path);
        return Task.CompletedTask;
    }
}

public sealed class MemorySettings : ISettingsStore
{
    public string? LastWorkspace { get; set; }
}

/// <summary>A dataset, a workspace and runs on disk in a temporary folder, with fake Docker and GPU.</summary>
public sealed class TestWorld : IDisposable
{
    public const string RtxLine = "NVIDIA GeForce RTX 4090, 24564, 23000, 560.94, 8.9";

    public TestWorld(bool gpu = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "trainer-desktop-" + Guid.NewGuid().ToString("N")[..8]);
        Dataset = Path.Combine(Root, "dữ liệu");
        Directory.CreateDirectory(Dataset);
        DataYaml = Path.Combine(Dataset, "data.yaml");
        File.WriteAllText(DataYaml, "train: images/train\nval: images/val\nnames: [dau_cos, day_thit, bang_dinh]\n");
        Runner.NvidiaSmi = gpu ? RtxLine : null;
        Catalog = ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog"));
        Services = new TrainerServices(Catalog, new HardwareProbe(Runner, Containers), new JobService(Containers),
            new DatasetChecker(Containers), Containers, Dialogs, Settings, Polling: false);
    }

    public string Root { get; }

    public string Dataset { get; }

    public string DataYaml { get; }

    public FakeContainers Containers { get; } = new();

    public FakeRunner Runner { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public MemorySettings Settings { get; } = new();

    public ModelCatalog Catalog { get; }

    public TrainerServices Services { get; }

    public Workspace CreateWorkspace(string name = "WireHarness_Detect") => Workspace.Create(Path.Combine(Root, name), DataYaml);

    public ShellViewModel Shell(Workspace? workspace = null)
    {
        if (workspace is not null) Settings.LastWorkspace = workspace.Root;
        return new ShellViewModel(Services);
    }

    /// <summary>A run as the worker leaves it: events, and optionally result, export report and dataset report.</summary>
    public RunFolder AddRun(Workspace workspace, string model, DateTime createdUtc, IEnumerable<string> events,
        bool result = false, string? export = null, bool synthetic = true, bool best = false, bool checkpoint = false, int imgsz = 320)
    {
        var variant = Catalog.Find(model)!;
        var run = workspace.CreateRun(variant, new TrainOptions(imgsz, 3, 4, "cpu", 2, 0, 100, true, null), "cpu", "ntech-trainer:dev-cpu", "sha256:test", createdUtc);
        File.WriteAllLines(run.EventsFile, events);
        File.WriteAllText(Path.Combine(run.Path_, "dataset.json"), JsonSerializer.Serialize(new { fingerprint = "abc", synthetic }));
        if (best || result || export is not null) Touch(Path.Combine(run.Path_, "train", "weights", "best.pt"));
        if (checkpoint) Touch(Path.Combine(run.Path_, "checkpoints", "resume.pt"));
        if (result)
        {
            File.WriteAllText(run.ResultFile, """
                {"precision": 0.61, "recall": 0.55, "map50": 0.58, "map50_95": 0.31, "classes": [
                  {"id": 0, "name": "dau_cos", "precision": 0.7, "recall": 0.6, "map50": 0.65, "map50_95": 0.35},
                  {"id": 1, "name": "day_thit", "precision": 0.5, "recall": 0.45, "map50": 0.5, "map50_95": 0.25},
                  {"id": 2, "name": "bang_dinh", "precision": 0.63, "recall": 0.6, "map50": 0.59, "map50_95": 0.33}]}
                """);
        }

        if (export is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(run.ReportFile)!);
            File.WriteAllText(run.ReportFile, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["status"] = export,
                ["problems"] = export == "failed" ? new[] { "box lệch" } : Array.Empty<string>(),
                ["boxes_compared"] = 6000,
                ["boxes_without_pair"] = 2,
                ["highest_score_without_pair"] = 0.045,
                ["min_box_iou"] = 0.99997,
                ["latency_ms_median"] = 19.0,
                ["latency_device"] = "CPUExecutionProvider",
                ["file_mb"] = 10.4
            }));
        }

        return run;
    }

    public static string Event(long id, string type, object data, string ts = "2026-11-08T10:00:00Z") =>
        JsonSerializer.Serialize(new { schema = 1, id, run = "r", ts, type, data });

    /// <summary>Three epochs of a finished CPU run with a warning, as the worker writes them.</summary>
    public static List<string> FinishedRun(int epochs = 3, string final = RunStates.Completed)
    {
        var lines = new List<string>
        {
            Event(1, "state", new { state = RunStates.Preparing }, "2026-11-08T10:00:00Z"),
            Event(2, "state", new { state = RunStates.Preflight }, "2026-11-08T10:00:05Z"),
            Event(3, "preflight", new { seconds_per_batch = 0.4, train_seconds_per_epoch_estimate = 12, vram_peak_gb = (double?)null, batch = 4 }, "2026-11-08T10:00:10Z"),
            Event(4, "state", new { state = RunStates.Running }, "2026-11-08T10:00:11Z")
        };
        var id = 5L;
        for (var epoch = 1; epoch <= epochs; epoch++)
        {
            lines.Add(Event(id++, "progress", new { epoch, epochs, batch = 10, batches = 20 }, $"2026-11-08T10:0{epoch}:00Z"));
            lines.Add(Event(id++, "epoch_end", new
            {
                epoch, epochs, train_loss = 4.0 - epoch * 0.5, val_loss = 4.2 - epoch * 0.4, precision = 0.2 * epoch, recall = 0.18 * epoch,
                map50 = 0.19 * epoch, map50_95 = 0.1 * epoch, vram_peak_gb = (double?)null, batch = 4, eta_seconds = (epochs - epoch) * 15
            }, $"2026-11-08T10:0{epoch}:30Z"));
            lines.Add(Event(id++, "resource", new { gpu = (object?)null, cpu_percent = 87.5, ram_used_gb = 3.1, ram_total_gb = 7.7, disk_free_gb = 120.4 }, $"2026-11-08T10:0{epoch}:31Z"));
        }

        lines.Add(Event(id++, "warning", new { code = "no_improvement", message = "mAP không cải thiện sau 2 epoch." }, "2026-11-08T10:04:00Z"));
        if (final != RunStates.Running)
        {
            lines.Add(Event(id++, "state", new { state = RunStates.Evaluating }, "2026-11-08T10:04:10Z"));
            lines.Add(Event(id, "state", new { state = final }, "2026-11-08T10:04:30Z"));
        }

        return lines;
    }

    public static string ValidateJson(bool ok = true) => JsonSerializer.Serialize(new
    {
        ok,
        errors = ok ? Array.Empty<object>() : [new { code = "no_val", message = "Không có ảnh val." }],
        warnings = new[] { new { code = "synthetic", message = "Dataset tổng hợp: chỉ dùng để thử luồng, metric không nói lên chất lượng thật." } },
        stats = new
        {
            names = new[] { "dau_cos", "day_thit", "bang_dinh" },
            splits = new { train = new { images = 64, boxes = 410 }, val = new { images = 16, boxes = 98 } },
            per_class = new Dictionary<string, int> { ["dau_cos"] = 220, ["day_thit"] = 180, ["bang_dinh"] = 108 },
            imgsz = 640,
            min_side_px_at_imgsz = new { p5 = 14.2, p50 = 38.5 }
        },
        fingerprint = "9f2c4e",
        synthetic = true
    });

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }
}
