using NTech.DataStudio.Trainer.Desktop.ViewModels;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.Tests;

public sealed class ShellTests : IDisposable
{
    private readonly TestWorld _world = new();

    public void Dispose() => _world.Dispose();

    [Fact]
    public void WithoutWorkspace_EveryTabButMachineAsksForOne()
    {
        var shell = _world.Shell();
        shell.Start();
        Assert.Same(shell.Machine, shell.CurrentPage);
        Assert.False(shell.NeedsWorkspace);
        shell.Dataset.IsSelected = true;
        Assert.True(shell.NeedsWorkspace);
        Assert.False(shell.Machine.IsSelected);
    }

    [Fact]
    public void Start_ReopensTheLastWorkspace()
    {
        var workspace = _world.CreateWorkspace();
        var shell = _world.Shell(workspace);
        shell.Start();
        Assert.Equal(workspace.Root, shell.WorkspacePath);
    }

    [Fact]
    public async Task CreateWorkspace_ReferencesTheDatasetWithoutTouchingIt()
    {
        var before = File.ReadAllBytes(_world.DataYaml);
        _world.Dialogs.DataYaml = _world.DataYaml;
        _world.Dialogs.Folders.Enqueue(Path.Combine(_world.Root, "Mới"));
        var shell = _world.Shell();
        shell.Start();
        await shell.CreateWorkspaceCommand.ExecuteAsync(null);
        Assert.True(shell.HasWorkspace);
        Assert.Equal(before, File.ReadAllBytes(_world.DataYaml));
        Assert.Equal(shell.WorkspacePath, _world.Settings.LastWorkspace);
    }

    [Fact]
    public async Task CreateWorkspace_InsideTheDataset_ShowsWhy()
    {
        _world.Dialogs.DataYaml = _world.DataYaml;
        _world.Dialogs.Folders.Enqueue(Path.Combine(_world.Dataset, "ws"));
        var shell = _world.Shell();
        await shell.CreateWorkspaceCommand.ExecuteAsync(null);
        Assert.False(shell.HasWorkspace);
        Assert.Contains("dataset", shell.Error);
    }
}

public sealed class ConfigTests : IDisposable
{
    private readonly TestWorld _world;

    public ConfigTests() => _world = new TestWorld();

    public void Dispose() => _world.Dispose();

    private async Task<ShellViewModel> Open(bool gpu)
    {
        _world.Runner.NvidiaSmi = gpu ? TestWorld.RtxLine : null;
        var shell = _world.Shell(_world.CreateWorkspace());
        shell.Start();
        await shell.Machine.RefreshCommand.ExecuteAsync(null);
        shell.Config.IsSelected = true;
        return shell;
    }

    [Fact]
    public async Task Cpu_NeedsTheConfirmationBeforeStart()
    {
        var shell = await Open(gpu: false);
        var config = shell.Config;
        Assert.Equal(GateKind.NeedsConfirmation, config.Gate);
        Assert.Contains(config.GateReasons, reason => reason.Contains("CPU"));
        Assert.False(config.StartCommand.CanExecute(null));
        config.RiskAccepted = true;
        Assert.True(config.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChangingTheModel_AsksForTheConfirmationAgain()
    {
        var config = (await Open(gpu: false)).Config;
        config.RiskAccepted = true;
        config.SelectedSize = "m";
        Assert.False(config.RiskAccepted);
        Assert.False(config.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task StrongGpu_StartsWithoutConfirmationAndUsesAutoBatch()
    {
        var config = (await Open(gpu: true)).Config;
        config.SelectedFamily = config.Families.Single(item => item.Family.Id == "yolo26");
        Assert.Equal(GateKind.Allowed, config.Gate);
        Assert.True(config.AutoBatch);
        Assert.True(config.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Experimental_AlwaysAsks()
    {
        var config = (await Open(gpu: true)).Config;
        config.SelectedFamily = config.Families.Single(item => item.Family.Id == "yolo12");
        Assert.Equal(GateKind.NeedsConfirmation, config.Gate);
    }

    [Fact]
    public async Task NoDocker_Blocks()
    {
        _world.Containers.Info = new(false, null, false, 0, 0, "Docker chưa chạy hoặc không có quyền truy cập.");
        var config = (await Open(gpu: true)).Config;
        Assert.True(config.IsBlocked);
        config.RiskAccepted = true;
        Assert.False(config.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_CreatesTheRunWithTheChosenSettingsAndOpensTrain()
    {
        var shell = await Open(gpu: false);
        var config = shell.Config;
        config.SelectedFamily = config.Families.Single(item => item.Family.Id == "yolo11");
        config.SelectedSize = "n";
        config.Imgsz = 320;
        config.Epochs = 3;
        config.NoFlipLeftRight = true;
        config.RiskAccepted = true;
        await config.StartCommand.ExecuteAsync(null);

        Assert.Null(config.Error);
        var spec = Assert.Single(_world.Containers.Started);
        Assert.Null(spec.Gpus);
        var run = Assert.Single(shell.Session.Workspace!.Runs());
        var yaml = File.ReadAllText(Path.Combine(run.Path_, "config.yaml"));
        Assert.Contains("model: yolo11n", yaml);
        Assert.Contains("imgsz: 320", yaml);
        Assert.Contains("fliplr: 0", yaml);
        Assert.Contains("device: cpu", yaml);
        Assert.Same(shell.Train, shell.CurrentPage);
    }

    [Fact]
    public async Task Start_WithoutImage_SaysHowToBuildIt()
    {
        _world.Containers.Digest = null;
        var config = (await Open(gpu: true)).Config;
        await config.StartCommand.ExecuteAsync(null);
        Assert.Contains("docker/trainer/Dockerfile", config.Error);
        Assert.Empty(config.Session.Workspace!.Runs());
    }
}

public sealed class TrainTests : IDisposable
{
    private readonly TestWorld _world = new();

    public void Dispose() => _world.Dispose();

    private async Task<(ShellViewModel Shell, Workspace Workspace)> Open(Action<Workspace> setup)
    {
        var workspace = _world.CreateWorkspace();
        setup(workspace);
        var shell = _world.Shell(workspace);
        shell.Start();
        shell.Train.IsSelected = true;
        await shell.Train.RefreshAsync();
        return (shell, workspace);
    }

    [Fact]
    public async Task FinishedRun_ShowsMetricsCurvesAndWarnings()
    {
        var (shell, _) = await Open(workspace => _world.AddRun(workspace, "yolo26n", new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun()));
        var train = shell.Train;
        Assert.Equal(RunStates.Completed, train.SelectedRun!.State);
        Assert.Equal(100, train.ProgressPercent);
        Assert.Equal("0.570", train.Map50);
        Assert.Equal(3, train.MapSeries[0].Values.Count);
        Assert.Equal("1 cảnh báo, 0 lỗi", train.WarningSummary);
        Assert.True(train.IsSynthetic);
        Assert.Equal("00:04:30", train.ElapsedText);
        Assert.False(train.StopCommand.CanExecute(null));
        Assert.False(train.ResumeCommand.CanExecute(null));

        train.SelectedLogFilter = train.LogFilters.Single(option => option.Value == LogFilter.Warnings);
        Assert.Equal("mAP không cải thiện sau 2 epoch.", Assert.Single(train.VisibleLog).Text);
    }

    [Fact]
    public async Task RunningRun_CanBeStoppedAtTheEndOfTheEpoch()
    {
        RunFolder? run = null;
        var (shell, _) = await Open(workspace =>
        {
            run = _world.AddRun(workspace, "yolo11n", DateTime.UtcNow, TestWorld.FinishedRun(final: RunStates.Running));
            _world.Containers.Running.Add(run.Id);
        });
        var train = shell.Train;
        Assert.Equal(RunStates.Running, train.SelectedRun!.State);
        Assert.True(train.StopCommand.CanExecute(null));
        train.StopCommand.Execute(null);
        Assert.Contains("true", File.ReadAllText(Path.Combine(run!.Path_, "control.json")));
        Assert.False(train.StopCommand.CanExecute(null));
        Assert.NotNull(train.Notice);
    }

    [Fact]
    public async Task WorkerGoneAfterACheckpoint_IsInterruptedAndResumes()
    {
        var (shell, _) = await Open(workspace =>
            _world.AddRun(workspace, "yolov8n", DateTime.UtcNow, TestWorld.FinishedRun(final: RunStates.Running), checkpoint: true));
        var train = shell.Train;
        Assert.Equal(RunStates.Interrupted, train.SelectedRun!.State);
        Assert.Contains("Train tiếp", train.Notice);
        Assert.True(train.ResumeCommand.CanExecute(null));
        await train.ResumeCommand.ExecuteAsync(null);
        Assert.Contains("--resume", Assert.Single(_world.Containers.Started).Command);
    }

    [Fact]
    public async Task WorkerGoneBeforeACheckpoint_SaysToStartOver()
    {
        var (shell, _) = await Open(workspace =>
            _world.AddRun(workspace, "yolov8n", DateTime.UtcNow, [TestWorld.Event(1, "state", new { state = RunStates.Preflight })]));
        Assert.Equal(RunStates.Interrupted, shell.Train.SelectedRun!.State);
        Assert.False(shell.Train.ResumeCommand.CanExecute(null));
        Assert.Contains("train lại từ đầu", shell.Train.Notice);
    }

    [Fact]
    public async Task NewestRunComesFirst_AndAStartedRunIsSelected()
    {
        var (shell, workspace) = await Open(workspace =>
        {
            _world.AddRun(workspace, "yolo26n", new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun());
            _world.AddRun(workspace, "yolo11n", new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun());
        });
        Assert.Equal("yolo11n", shell.Train.Runs[0].Model);
        var older = shell.Train.Runs[1].Id;
        shell.Session.AnnounceRun(older);
        await shell.Train.RefreshAsync();
        Assert.Equal(older, shell.Train.SelectedRun!.Id);
    }
}

public sealed class ResultsTests : IDisposable
{
    private readonly TestWorld _world = new();

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Rows_CarryMetricsPerClassAndTheOnnxCheck()
    {
        var workspace = _world.CreateWorkspace();
        _world.AddRun(workspace, "yolo11n", new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun(), result: true, export: "passed");
        var shell = _world.Shell(workspace);
        shell.Start();
        shell.Results.IsSelected = true;
        await shell.Results.RefreshAsync();

        var results = shell.Results;
        var row = Assert.Single(results.Rows);
        Assert.Equal("0.580", row.Map50);
        Assert.Equal("Đạt", row.OnnxText);
        Assert.Equal(3, results.Classes.Count);
        Assert.Contains(results.ExportDetails, line => line.Contains("6000 box"));
        Assert.True(results.AnySynthetic);
        Assert.True(results.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task DifferentImgsz_IsFlaggedAsNotComparable()
    {
        var workspace = _world.CreateWorkspace();
        _world.AddRun(workspace, "yolo11n", new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun(), result: true, imgsz: 320);
        _world.AddRun(workspace, "yolo26n", new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc), TestWorld.FinishedRun(), result: true, imgsz: 640);
        var shell = _world.Shell(workspace);
        shell.Start();
        shell.Results.IsSelected = true;
        await shell.Results.RefreshAsync();
        Assert.Contains(shell.Results.Differences, reason => reason.Contains("imgsz"));
    }

    [Fact]
    public async Task Export_StartsTheExportContainer()
    {
        var workspace = _world.CreateWorkspace();
        _world.AddRun(workspace, "yolo26n", DateTime.UtcNow, TestWorld.FinishedRun(), result: true);
        var shell = _world.Shell(workspace);
        shell.Start();
        shell.Results.IsSelected = true;
        await shell.Results.RefreshAsync();
        await shell.Results.ExportCommand.ExecuteAsync(null);
        Assert.Equal("export", Assert.Single(_world.Containers.Started).Command[0]);
        Assert.True(shell.Results.SelectedRow!.ExportRunning);
        Assert.False(shell.Results.ExportCommand.CanExecute(null));
    }
}

public sealed class ErrorTests : IDisposable
{
    private readonly TestWorld _world = new();

    public void Dispose() => _world.Dispose();

    /// <summary>Found on Linux: the 2-second refresh cleared the export error before anyone saw it.</summary>
    [Fact]
    public async Task AFailedExport_StaysOnScreenAcrossRefreshes()
    {
        var workspace = _world.CreateWorkspace();
        _world.AddRun(workspace, "yolo26n", DateTime.UtcNow, TestWorld.FinishedRun(), result: true);
        File.Delete(_world.DataYaml);
        var shell = _world.Shell(workspace);
        shell.Start();
        shell.Results.IsSelected = true;
        await shell.Results.RefreshAsync();

        await shell.Results.ExportCommand.ExecuteAsync(null);
        Assert.Contains("Không thấy dataset", shell.Results.Error);
        await shell.Results.RefreshAsync();
        await shell.Results.RefreshAsync();
        Assert.Contains("Không thấy dataset", shell.Results.Error);
        Assert.Empty(_world.Containers.Started);
    }
}

public sealed class DatasetTests : IDisposable
{
    private readonly TestWorld _world = new();

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task Check_ShowsCountsWarningsAndTheSyntheticFlag()
    {
        _world.Containers.ValidateOutput = TestWorld.ValidateJson();
        var shell = _world.Shell(_world.CreateWorkspace());
        shell.Start();
        shell.Dataset.IsSelected = true;
        await shell.Dataset.CheckCommand.ExecuteAsync(null);
        var dataset = shell.Dataset;
        Assert.True(dataset.Report!.Ok);
        Assert.True(dataset.Report.Synthetic);
        Assert.Equal(3, dataset.Classes.Count);
        Assert.Equal(1.0, dataset.Classes[0].Share);
        Assert.Contains("14.2", dataset.SizeText.Replace(',', '.'));
        var spec = _world.Containers.Started.Count == 0 ? null : _world.Containers.Started[0];
        Assert.Null(spec); // validate runs to the end, nothing is left running
    }

    [Fact]
    public async Task WorkerWithoutOutput_IsAnErrorNotACrash()
    {
        _world.Containers.ValidateOutput = "";
        var shell = _world.Shell(_world.CreateWorkspace());
        shell.Start();
        await shell.Dataset.CheckCommand.ExecuteAsync(null);
        Assert.True(shell.Dataset.HasError);
        Assert.Null(shell.Dataset.Report);
    }
}
