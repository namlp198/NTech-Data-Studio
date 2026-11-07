using System.Text.Json;
using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Tests;

internal static class Events
{
    public static TrainerEvent Of(string type, object data, string ts = "2026-11-08T10:00:00Z") =>
        new(1, ts, type, JsonSerializer.SerializeToElement(data));
}

public class RunMonitorTests
{
    [Fact]
    public void FoldsProgressMetricsAndResources()
    {
        var monitor = new RunMonitor();
        monitor.Apply([
            Events.Of("state", new { state = "Running" }, "2026-11-08T10:00:00Z"),
            Events.Of("progress", new { epoch = 1, epochs = 3, batch = 5, batches = 20 }),
            Events.Of("epoch_end", new { epoch = 1, epochs = 3, train_loss = 2.5, val_loss = 2.7, map50 = 0.3, map50_95 = 0.15, precision = 0.4, recall = 0.35, eta_seconds = 60, vram_peak_gb = 3.2 }),
            Events.Of("resource", new { gpu = new { percent = 91.0, vram_used_mb = 6144.0, vram_total_mb = 24576.0, temp_c = 71.0 }, cpu_percent = 40.0, ram_used_gb = 9.5, ram_total_gb = 31.0, disk_free_gb = 400.0 }, "2026-11-08T10:01:30Z")
        ]);

        Assert.Equal("Running", monitor.LastState);
        Assert.Equal(1, monitor.Epoch);
        Assert.Equal(20, monitor.Batch);
        Assert.Equal(0.3, monitor.Latest!.Map50);
        Assert.Equal(3.2, monitor.VramPeakGb);
        Assert.Equal(6, monitor.Resources!.VramUsedGb);
        Assert.Equal(TimeSpan.FromSeconds(90), monitor.Elapsed());
    }

    [Fact]
    public void ARedoneEpochReplacesInsteadOfDuplicating()
    {
        var monitor = new RunMonitor();
        monitor.Apply(Events.Of("epoch_end", new { epoch = 2, epochs = 3, map50 = 0.1 }));
        monitor.Apply(Events.Of("epoch_end", new { epoch = 2, epochs = 3, map50 = 0.2 }));
        Assert.Equal(0.2, Assert.Single(monitor.EpochHistory).Map50);
    }

    [Fact]
    public void WarningsAndErrorsKeepTheirLevelInTheLog()
    {
        var monitor = new RunMonitor();
        monitor.Apply([
            Events.Of("warning", new { code = "nan", message = "Loss NaN." }),
            Events.Of("export_result", new { format = "onnx", status = "failed", problems = new[] { "box lệch" } }),
            Events.Of("progress", new { epoch = 1, epochs = 3, batch = 1, batches = 2 })
        ]);
        Assert.Equal(1, monitor.WarningCount);
        Assert.Equal(1, monitor.ErrorCount);
        Assert.Equal("failed", monitor.ExportStatus);
        Assert.Contains("box lệch", monitor.Log[1].Text);
        Assert.Equal(2, monitor.Log.Count); // progress is shown as a bar, not as log lines
    }
}

public class DatasetReportTests
{
    [Fact]
    public void Parse_ReadsTheWorkerOutput()
    {
        var report = DatasetReport.Parse("""
            {"ok": false, "errors": [{"code": "no_val", "message": "Không có ảnh val."}],
             "warnings": [{"code": "few_labels", "message": "Class bang_dinh chỉ có 3 nhãn."}],
             "stats": {"names": ["dau_cos", "day_thit", "bang_dinh"], "splits": {"train": {"images": 10, "boxes": 40}},
                       "per_class": {"dau_cos": 20, "day_thit": 17, "bang_dinh": 3}, "imgsz": 640, "min_side_px_at_imgsz": null},
             "fingerprint": "abc", "synthetic": false}
            """);
        Assert.False(report.Ok);
        Assert.Equal("no_val", Assert.Single(report.Errors).Code);
        Assert.Equal(3, report.Classes.Count);
        Assert.Equal(new SplitCount("train", 10, 40), Assert.Single(report.Splits));
        Assert.Null(report.MinSideP5);
        Assert.False(report.Synthetic);
    }
}

public sealed class RunSummaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"summary-{Guid.NewGuid():N}");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (Workspace Workspace, ModelCatalog Catalog) New()
    {
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "data.yaml"), "train: a\n");
        return (Workspace.Create(Path.Combine(_root, "ws"), Path.Combine(data, "data.yaml")),
            ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog")));
    }

    private static RunFolder Run(Workspace workspace, ModelCatalog catalog, string model, int imgsz, string fingerprint, DateTime at)
    {
        var run = workspace.CreateRun(catalog.Find(model)!, new TrainOptions(imgsz, 3, 4, "cpu", 2, 0, 100, true, null), "cpu", "img", "sha", at);
        File.WriteAllText(Path.Combine(run.Path_, "dataset.json"), JsonSerializer.Serialize(new { fingerprint, synthetic = true }));
        return run;
    }

    [Fact]
    public void Read_TakesSettingsFromTheFrozenConfig()
    {
        var (workspace, catalog) = New();
        var run = Run(workspace, catalog, "yolo11n", 320, "f1", DateTime.UtcNow);
        File.WriteAllText(run.ResultFile, """{"precision": 0.5, "recall": 0.4, "map50": 0.45, "map50_95": 0.2, "classes": [{"name": "dau_cos", "precision": 0.5, "recall": 0.4, "map50": 0.45, "map50_95": 0.2}]}""");
        var summary = RunSummary.Read(run, RunStates.Completed);
        Assert.Equal(320, summary.Settings.Imgsz);
        Assert.Equal(3, summary.Settings.Epochs);
        Assert.True(summary.Synthetic);
        Assert.Equal(0.45, summary.Overall!.Map50);
        Assert.Single(summary.Classes);
        Assert.Null(summary.Export);
        Assert.False(summary.CanExport); // no best.pt yet
    }

    [Fact]
    public void Differences_NameWhatIsNotTheSame()
    {
        var (workspace, catalog) = New();
        var a = RunSummary.Read(Run(workspace, catalog, "yolo11n", 320, "f1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), RunStates.Completed);
        var b = RunSummary.Read(Run(workspace, catalog, "yolo26n", 640, "f2", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)), RunStates.Completed);
        var c = RunSummary.Read(Run(workspace, catalog, "yolov8n", 320, "f1", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)), RunStates.Completed);
        var reasons = RunSummary.Differences([a, b]);
        Assert.Contains(reasons, reason => reason.Contains("dataset"));
        Assert.Contains(reasons, reason => reason.Contains("imgsz"));
        Assert.Empty(RunSummary.Differences([a, c]));
    }

    [Fact]
    public void Tracker_ReadsOnlyNewLinesAndDerivesInterrupted()
    {
        var (workspace, catalog) = New();
        var run = Run(workspace, catalog, "yolo11n", 320, "f1", DateTime.UtcNow);
        File.WriteAllText(run.EventsFile, "{\"schema\":1,\"id\":1,\"run\":\"r\",\"ts\":\"t\",\"type\":\"state\",\"data\":{\"state\":\"Running\"}}\n");
        var tracker = new RunStateTracker();
        Assert.Equal(RunStates.Running, tracker.Refresh(workspace, new HashSet<string> { run.Id }).Single().State);
        Assert.Equal(RunStates.Interrupted, tracker.Refresh(workspace, new HashSet<string>()).Single().State);
        File.AppendAllText(run.EventsFile, "{\"schema\":1,\"id\":2,\"run\":\"r\",\"ts\":\"t\",\"type\":\"state\",\"data\":{\"state\":\"Completed\"}}\n");
        Assert.Equal(RunStates.Completed, tracker.Refresh(workspace, new HashSet<string>()).Single().State);
    }
}
