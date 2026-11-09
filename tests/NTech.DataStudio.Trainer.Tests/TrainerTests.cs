using System.Text;
using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Processes;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Tests;

internal static class Machines
{
    public static readonly DockerInfo DockerOk = new(true, "29.8.0", true, 16, 64, null);
    public static readonly GpuInfo Rtx = new("RTX 4090", 24, 23, "560", 8.9);
    public static readonly GpuInfo Maxwell = new("Quadro M2200", 4, 3.5, "573", 5.2);

    public static HardwareSnapshot With(GpuInfo? gpu = null, DockerInfo? docker = null, double disk = 500, double ram = 64) =>
        new("test", 32, ram, disk, "/work", gpu is null ? [] : [gpu], docker ?? DockerOk);
}

public class CatalogTests
{
    private static ModelCatalog Load() => ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog"));

    [Theory]
    [InlineData("yolo26s", "yolo26", "yolo26s.pt")]
    [InlineData("yolo11m", "yolo11", "yolo11m.pt")]
    [InlineData("yolov8x", "yolov8", "yolov8x.pt")]
    [InlineData("yolo12n", "yolo12", "yolo12n.pt")]
    public void Find_ResolvesEveryChosenVersionFromTheRealCatalog(string name, string family, string weights)
    {
        var model = Load().Find(name);
        Assert.NotNull(model);
        Assert.Equal(family, model.Family.Id);
        Assert.Equal(weights, model.Weights);
    }

    [Theory]
    [InlineData("yolo26q")]
    [InlineData("yolo99s")]
    [InlineData("rf-detr")]
    public void Find_RejectsUnknownNames(string name) => Assert.Null(Load().Find(name));

    [Fact]
    public void ChosenSet_HasTheAgreedStatusAndExportMode()
    {
        var catalog = Load();
        Assert.Equal(ModelStatus.Supported, catalog.Find("yolo26s")!.Family.Status);
        Assert.Equal(ModelStatus.Supported, catalog.Find("yolo11s")!.Family.Status);
        Assert.Equal(ModelStatus.Supported, catalog.Find("yolov8s")!.Family.Status);
        Assert.Equal(ModelStatus.Experimental, catalog.Find("yolo12s")!.Family.Status);
        Assert.All(catalog.Visible, family => Assert.True(family.OnnxNms)); // one output shape for every version
    }

    [Fact]
    public void AnUnmeasuredVersion_HasNoVramNumbers() => Assert.Null(Load().Find("yolo12s")!.VramAt640);

    [Theory]
    [InlineData("yolo26segm", "yolo26seg", "yolo26m-seg.pt", "YOLO26")]
    [InlineData("yolo11segn", "yolo11seg", "yolo11n-seg.pt", "YOLO11")]
    [InlineData("yolov8segx", "yolov8seg", "yolov8x-seg.pt", "YOLOv8")]
    public void Find_ResolvesSegmentVersions_AndLeavesTheDetectFamilyOfTheSameVersionAlone(string name, string family, string weights, string display)
    {
        var catalog = Load();
        var model = catalog.Find(name);
        Assert.NotNull(model);
        Assert.Equal(family, model.Family.Id);
        Assert.Equal("segment", model.Family.Task);
        Assert.Equal(weights, model.Weights);
        Assert.Equal(display, model.Family.DisplayName);
        Assert.Equal("detect", catalog.Find(name.Replace("seg", "", StringComparison.Ordinal))!.Family.Task);
    }

    [Fact]
    public void Tasks_ListDetectFirst_AndSegmentFamiliesAreUnmeasured()
    {
        var catalog = Load();
        Assert.Equal(["detect", "segment"], catalog.Tasks);
        Assert.Equal("YOLO26", catalog.Find("yolo26s")!.Family.DisplayName);
        Assert.Null(catalog.Find("yolo26segs")!.VramAt640);
    }
}

public class NvidiaSmiTests
{
    [Fact]
    public void Parse_ReadsTheRealOutputShape()
    {
        var gpu = Assert.Single(NvidiaSmi.Parse("Quadro M2200, 4096, 3500, 573.22, 5.2\r\n"));
        Assert.Equal("Quadro M2200", gpu.Name);
        Assert.Equal(4, gpu.VramGb);
        Assert.Equal(5.2, gpu.ComputeCapability);
    }

    [Fact]
    public void Parse_ToleratesUnknownCapabilityAndGarbage()
    {
        var gpus = NvidiaSmi.Parse("RTX, 8192, 8000, 560, [N/A]\nnot a gpu line\n");
        Assert.Null(Assert.Single(gpus).ComputeCapability);
    }
}

public class AssessorTests
{
    private static readonly ModelCatalog Catalog = ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog"));

    private static ModelVariant Model(string name) => Catalog.Find(name)!;

    [Fact]
    public void NoGpu_NeedsConfirmationAndRunsOnCpu()
    {
        var machine = Machines.With();
        Assert.Null(Assessor.UsableGpu(machine));
        var gate = Assessor.TrainGate(machine, Model("yolo26s"), 640);
        Assert.Equal(GateKind.NeedsConfirmation, gate.Kind);
        Assert.Contains(gate.Reasons, reason => reason.Contains("CPU"));
    }

    [Fact]
    public void GpuOlderThanTuring_IsTreatedAsNoGpu()
    {
        var machine = Machines.With(Machines.Maxwell);
        Assert.Null(Assessor.UsableGpu(machine));
        Assert.Equal(Grade.Inadequate, Assessor.Machine(machine).Criteria.Single(item => item.Name == "GPU").Grade);
        Assert.Contains("5.2", Assert.Single(Assessor.TrainGate(machine, Model("yolo26s"), 640).Reasons));
    }

    [Fact]
    public void StrongMachine_IsAllowedWithoutConfirmation()
    {
        Assert.Equal(GateKind.Allowed, Assessor.TrainGate(Machines.With(Machines.Rtx), Model("yolo11m"), 640).Kind);
    }

    [Fact]
    public void Vram_GrowsWithTheSquareOfImgsz()
    {
        var gpu = new GpuInfo("RTX", 12, 12, "560", 8.6);
        var at640 = Assessor.Workload(Machines.With(gpu), Model("yolo26m"), 640);
        var at1280 = Assessor.Workload(Machines.With(gpu), Model("yolo26m"), 1280);
        Assert.Equal(Grade.Good, at640.Overall);
        Assert.Equal(Grade.Inadequate, at1280.Overall); // needs 24 GB
        Assert.Equal(GateKind.NeedsConfirmation, Assessor.TrainGate(Machines.With(gpu), Model("yolo26m"), 1280).Kind);
    }

    [Fact]
    public void UnmeasuredVersion_IsUnknownNotGuessed()
    {
        var result = Assessor.Workload(Machines.With(Machines.Rtx), Model("yolo12s"), 640);
        Assert.Equal(Grade.Unknown, result.Overall);
        Assert.Equal(Confidence.Assumed, result.Criteria[0].Confidence);
    }

    [Fact]
    public void ExperimentalVersion_AlwaysAsksForConfirmation()
    {
        var gate = Assessor.TrainGate(Machines.With(Machines.Rtx), Model("yolo12s"), 640);
        Assert.Equal(GateKind.NeedsConfirmation, gate.Kind);
    }

    [Fact]
    public void NoDockerOrNoDisk_Blocks()
    {
        var noDocker = new DockerInfo(false, null, false, 0, 0, "Chưa cài Docker.");
        Assert.Equal(GateKind.Blocked, Assessor.TrainGate(Machines.With(Machines.Rtx, noDocker), Model("yolo26s"), 640).Kind);
        Assert.Equal(GateKind.Blocked, Assessor.TrainGate(Machines.With(Machines.Rtx, disk: 2), Model("yolo26s"), 640).Kind);
    }

    [Fact]
    public void Ram_IsJudgedByWhatDockerMayUse()
    {
        var limited = Machines.DockerOk with { MemoryGb = 8.2 };
        var ram = Assessor.Machine(Machines.With(Machines.Rtx, limited, ram: 64)).Criteria.Single(item => item.Name == "RAM");
        Assert.Equal(Grade.Weak, ram.Grade);
        Assert.Contains("8.2", ram.Reason.Replace(',', '.'));
    }

    [Fact]
    public void Overall_IsTheWorstCriterion()
    {
        var overall = Assessor.Machine(Machines.With(Machines.Rtx, disk: 6, ram: 64)).Overall;
        Assert.Equal(Grade.Weak, overall);
    }

    [Fact]
    public void TensorRt_IsOfferedOnlyFromTuring()
    {
        Assert.True(Assessor.TensorRt(Machines.With(Machines.Rtx)).Eligible);
        var old = Assessor.TensorRt(Machines.With(Machines.Maxwell));
        Assert.False(old.Eligible);
        Assert.Contains("7.5", old.Reason.Replace(',', '.'));
        Assert.False(Assessor.TensorRt(Machines.With()).Eligible);
    }
}

public class DockerArgumentsTests
{
    private static ContainerSpec Spec(string dataset) => new(
        "name",
        "image",
        [new Mount(dataset, "/data", ReadOnly: true), new Mount(@"C:\run", "/run_dir", ReadOnly: false)],
        ["train", "--run-dir", "/run_dir"],
        new Dictionary<string, string> { ["ntech.run"] = "abc" },
        Gpus: "all",
        User: "1000:1000");

    [Fact]
    public void Dataset_IsMountedReadOnlyAndRunReadWrite()
    {
        var arguments = DockerArguments.Run(Spec(@"C:\data"), detached: true);
        Assert.Contains(@"type=bind,source=C:\data,target=/data,readonly", arguments);
        Assert.Contains(@"type=bind,source=C:\run,target=/run_dir", arguments);
    }

    [Fact]
    public void SpecialCharactersStayOneArgument()
    {
        var path = @"C:\dữ liệu; rm -rf \data";
        var arguments = DockerArguments.Run(Spec(path), detached: true);
        Assert.Single(arguments, item => item.Contains(path, StringComparison.Ordinal));
    }

    [Fact]
    public void CarriesWhatTrainingNeeds()
    {
        var arguments = DockerArguments.Run(Spec("/data"), detached: true);
        Assert.Contains("--shm-size", arguments);
        Assert.Contains("--gpus", arguments);
        Assert.Contains("ntech.run=abc", arguments);
        Assert.Contains("1000:1000", arguments);
        Assert.Contains("-d", arguments);
        Assert.DoesNotContain("--privileged", arguments);
        Assert.Equal("train", arguments[arguments.IndexOf("image") + 1]);
    }
}

public sealed class EventLogReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"events-{Guid.NewGuid():N}.jsonl");

    public void Dispose() => File.Delete(_path);

    private static string Line(int id, string state) =>
        $"{{\"schema\":1,\"id\":{id},\"run\":\"r\",\"ts\":\"t\",\"type\":\"state\",\"data\":{{\"state\":\"{state}\"}}}}\n";

    [Fact]
    public void ContinuesFromTheOffset()
    {
        File.WriteAllText(_path, Line(1, "Preflight"));
        var reader = new EventLogReader(_path);
        Assert.Single(reader.ReadNew());
        Assert.Empty(reader.ReadNew());
        File.AppendAllText(_path, Line(2, "Running"));
        Assert.Equal("Running", Assert.Single(reader.ReadNew()).Text("state"));
    }

    [Fact]
    public void WaitsForAHalfWrittenLine()
    {
        var whole = Line(2, "Running");
        File.WriteAllText(_path, Line(1, "Preflight") + whole[..10]);
        var reader = new EventLogReader(_path);
        Assert.Single(reader.ReadNew());
        File.AppendAllText(_path, whole[10..]);
        Assert.Equal("Running", Assert.Single(reader.ReadNew()).Text("state"));
    }

    [Fact]
    public void KeepsVietnameseAcrossReads()
    {
        File.WriteAllText(_path, "{\"schema\":1,\"id\":1,\"run\":\"r\",\"ts\":\"t\",\"type\":\"warning\",\"data\":{\"message\":\"Hết bộ nhớ\"}}\n", new UTF8Encoding(false));
        Assert.Equal("Hết bộ nhớ", Assert.Single(new EventLogReader(_path).ReadNew()).Text("message"));
    }

    [Fact]
    public void SkipsATornLineAndNewerSchema()
    {
        File.WriteAllText(_path, "{\"schema\":1,\"id\n{\"schema\":99,\"id\":2,\"ts\":\"t\",\"type\":\"x\",\"data\":{}}\n" + Line(3, "Running"));
        Assert.Equal(3, Assert.Single(new EventLogReader(_path).ReadNew()).Id);
    }

    [Fact]
    public void MissingFile_IsEmpty() => Assert.Empty(new EventLogReader(_path).ReadNew());
}

public class RunStatesTests
{
    [Theory]
    [InlineData(null, false, "Created")]
    [InlineData(null, true, "Created")]
    [InlineData("Running", true, "Running")]
    [InlineData("Running", false, "Interrupted")]
    [InlineData("Preflight", false, "Interrupted")]
    [InlineData("Evaluating", false, "Interrupted")]
    [InlineData("Completed", false, "Completed")]
    [InlineData("Failed", false, "Failed")]
    [InlineData("Cancelled", false, "Cancelled")]
    public void Derive(string? last, bool running, string expected) => Assert.Equal(expected, RunStates.Derive(last, running));

    [Theory]
    [InlineData("Interrupted", true, true)]
    [InlineData("Cancelled", true, true)]
    [InlineData("Interrupted", false, false)]
    [InlineData("Completed", true, false)]
    [InlineData("Failed", true, false)]
    public void CanResume(string state, bool checkpoint, bool expected) => Assert.Equal(expected, RunStates.CanResume(state, checkpoint));
}

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ws-{Guid.NewGuid():N}");
    private readonly string _data;

    public WorkspaceTests()
    {
        var dataset = Path.Combine(_root, "bộ dữ liệu");
        Directory.CreateDirectory(dataset);
        _data = Path.Combine(dataset, "data.yaml");
        File.WriteAllText(_data, "train: images/train\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TrainOptions Options() => new(640, 3, 4, "cpu", 2, 0, 100, true, null);

    private static ModelVariant Model(string name) =>
        ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog")).Find(name)!;

    [Fact]
    public void Create_RefusesAFolderInsideTheDataset()
    {
        var inside = Path.Combine(Path.GetDirectoryName(_data)!, "ws");
        Assert.Throws<TrainerException>(() => Workspace.Create(inside, _data));
    }

    [Fact]
    public void Create_DoesNotTouchTheDataset()
    {
        var before = File.ReadAllBytes(_data);
        Workspace.Create(Path.Combine(_root, "ws"), _data);
        Assert.Equal(before, File.ReadAllBytes(_data));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(_data)!));
    }

    [Fact]
    public void Open_ReadsBackWhatCreateWrote()
    {
        var created = Workspace.Create(Path.Combine(_root, "ws"), _data);
        var opened = Workspace.Open(created.Root);
        Assert.Equal(_data, opened.File_.DataYaml);
        Assert.Equal("/data/data.yaml", opened.ContainerDataYaml);
    }

    [Fact]
    public void OneWorkspace_HoldsRunsOfSeveralVersions()
    {
        var workspace = Workspace.Create(Path.Combine(_root, "ws"), _data);
        var time = new DateTime(2026, 11, 7, 1, 2, 3, DateTimeKind.Utc);
        workspace.CreateRun(Model("yolo26s"), Options(), "cpu", "image", "sha:1", time);
        workspace.CreateRun(Model("yolo11s"), Options(), "cpu", "image", "sha:1", time);

        var runs = workspace.Runs();
        Assert.Equal(2, runs.Count);
        Assert.Equal("yolo11", runs[0].Record().ModelFamily);
        Assert.Equal("yolo26", runs[1].Record().ModelFamily);
    }

    [Fact]
    public void Config_CarriesTheTask()
    {
        Assert.Contains("task: detect", Workspace.ConfigYaml("id", Model("yolo26s"), Options(), "/data/data.yaml", "sha:1"));
        var segment = Workspace.ConfigYaml("id", Model("yolo26segs"), Options(), "/data/data.yaml", "sha:1");
        Assert.Contains("task: segment", segment);
        Assert.Contains("weights: yolo26s-seg.pt", segment);
        Assert.Contains("nms: true", segment);
    }

    [Fact]
    public void ARunRecord_KeepsItsTask_AndOneWrittenBeforeSegmentReadsAsDetect()
    {
        var workspace = Workspace.Create(Path.Combine(_root, "ws-task"), _data);
        var run = workspace.CreateRun(Model("yolo11segs"), Options(), "cpu", "image", "sha:1", DateTime.UtcNow);
        Assert.Equal("segment", run.Record().Task);

        var old = Directory.CreateDirectory(Path.Combine(workspace.RunsFolder, "20260101-000000-yolo26s"));
        File.WriteAllText(
            Path.Combine(old.FullName, "run.json"),
            "{\"schema\":1,\"id\":\"20260101-000000-yolo26s\",\"model\":\"yolo26s\",\"model_family\":\"yolo26\",\"weights\":\"yolo26s.pt\",\"license\":\"AGPL-3.0\",\"compute\":\"cpu\",\"image\":\"image\",\"image_digest\":\"sha:1\",\"created_at\":\"2026-01-01T00:00:00Z\"}");
        Assert.Equal("detect", workspace.Run("20260101-000000-yolo26s").Record().Task);
    }

    [Fact]
    public void Config_CarriesWhatTheWorkerReads()
    {
        var yaml = Workspace.ConfigYaml("id", Model("yolo26s"), Options(), "/data/data.yaml", "sha:1");
        Assert.Contains("nms: true", yaml);
        Assert.Contains("weights: yolo26s.pt", yaml);
        Assert.Contains("data: /data/data.yaml", yaml);
        Assert.Contains("image_digest: sha:1", yaml);
    }

    [Fact]
    public void Stop_IsAFileTheWorkerReads()
    {
        var run = Workspace.Create(Path.Combine(_root, "ws"), _data).CreateRun(Model("yolo26s"), Options(), "cpu", "image", "sha:1", DateTime.UtcNow);
        run.RequestStop();
        Assert.Contains("true", File.ReadAllText(Path.Combine(run.Path_, "control.json")));
        run.ClearStop();
        Assert.Contains("false", File.ReadAllText(Path.Combine(run.Path_, "control.json")));
    }
}

public sealed class JobServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jobs-{Guid.NewGuid():N}");

    private sealed class FakeContainers : IContainerRuntime
    {
        public bool Running { get; set; }
        public string? Digest { get; set; } = "sha:1";
        public List<ContainerSpec> Started { get; } = [];

        public Task<DockerInfo> InfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(Machines.DockerOk);
        public Task<string?> ImageDigestAsync(string image, CancellationToken cancellationToken = default) => Task.FromResult(Digest);
        public Task StartDetachedAsync(ContainerSpec spec, CancellationToken cancellationToken = default) { Started.Add(spec); return Task.CompletedTask; }
        public Task<ProcessResult> RunToEndAsync(ContainerSpec spec, CancellationToken cancellationToken = default) => Task.FromResult(new ProcessResult(0, "", ""));
        public Task<bool> IsRunningAsync(string label, string value, CancellationToken cancellationToken = default) => Task.FromResult(Running);
        public Task<IReadOnlySet<string>> RunningLabelValuesAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task StopAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private (Workspace Workspace, RunFolder Run) NewRun(string compute = "cpu")
    {
        var dataset = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataset);
        var yaml = Path.Combine(dataset, "data.yaml");
        File.WriteAllText(yaml, "train: images/train\n");
        var workspace = Workspace.Create(Path.Combine(_root, "ws"), yaml);
        var model = ModelCatalog.LoadFolder(Path.Combine(AppContext.BaseDirectory, "catalog")).Find("yolo26s")!;
        var run = workspace.CreateRun(model, new TrainOptions(640, 3, 4, "cpu", 2, 0, 100, true, null), compute, "image", "sha:1", DateTime.UtcNow);
        return (workspace, run);
    }

    [Fact]
    public async Task Start_MountsTheDatasetReadOnly()
    {
        var (workspace, run) = NewRun();
        var containers = new FakeContainers();
        await new JobService(containers).StartTrainAsync(workspace, run, resume: false);
        var spec = Assert.Single(containers.Started);
        Assert.True(spec.Mounts.Single(mount => mount.ContainerPath == "/data").ReadOnly);
        Assert.False(spec.Mounts.Single(mount => mount.ContainerPath == "/run_dir").ReadOnly);
        Assert.Null(spec.Gpus);
    }

    [Fact]
    public async Task Start_UsesGpuOnlyForTheCudaImage()
    {
        var (workspace, run) = NewRun("cuda");
        var containers = new FakeContainers();
        await new JobService(containers).StartTrainAsync(workspace, run, resume: false);
        Assert.Equal("all", Assert.Single(containers.Started).Gpus);
    }

    [Fact]
    public async Task Start_RefusesARunAlreadyGoing()
    {
        var (workspace, run) = NewRun();
        var containers = new FakeContainers { Running = true };
        await Assert.ThrowsAsync<TrainerException>(() => new JobService(containers).StartTrainAsync(workspace, run, resume: false));
        Assert.Empty(containers.Started);
    }

    [Fact]
    public async Task Resume_RefusesAChangedImage()
    {
        var (workspace, run) = NewRun();
        var containers = new FakeContainers { Digest = "sha:2" };
        await Assert.ThrowsAsync<TrainerException>(() => new JobService(containers).StartTrainAsync(workspace, run, resume: true));
        Assert.Empty(containers.Started);
    }

    [Fact]
    public async Task Resume_ClearsAnEarlierStopRequest()
    {
        var (workspace, run) = NewRun();
        run.RequestStop();
        var containers = new FakeContainers();
        await new JobService(containers).StartTrainAsync(workspace, run, resume: true);
        Assert.Contains("false", File.ReadAllText(Path.Combine(run.Path_, "control.json")));
        Assert.Contains("--resume", Assert.Single(containers.Started).Command);
    }

    [Fact]
    public async Task MissingDataset_IsRefusedBeforeDocker()
    {
        var (workspace, run) = NewRun();
        File.Delete(workspace.File_.DataYaml); // as a Windows path seen from Linux
        var containers = new FakeContainers();
        var problem = await Assert.ThrowsAsync<TrainerException>(() => new JobService(containers).StartTrainAsync(workspace, run, resume: false));
        Assert.Contains("Không thấy dataset", problem.Message);
        Assert.Empty(containers.Started);
    }

    [Fact]
    public async Task Export_NeedsABestCheckpoint()
    {
        var (workspace, run) = NewRun();
        await Assert.ThrowsAsync<TrainerException>(() => new JobService(new FakeContainers()).StartExportAsync(workspace, run));
    }

    [Fact]
    public async Task State_WorkerGoneWithoutFinishing_IsInterrupted()
    {
        var (_, run) = NewRun();
        File.WriteAllText(run.EventsFile, "{\"schema\":1,\"id\":1,\"run\":\"r\",\"ts\":\"t\",\"type\":\"state\",\"data\":{\"state\":\"Running\"}}\n");
        Assert.Equal(RunStates.Interrupted, await new JobService(new FakeContainers()).StateAsync(run));
        Assert.Equal(RunStates.Running, await new JobService(new FakeContainers { Running = true }).StateAsync(run));
    }
}
