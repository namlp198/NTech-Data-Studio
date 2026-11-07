using NTech.DataStudio.Trainer.Containers;

namespace NTech.DataStudio.Trainer.Runs;

/// <summary>Turns workspace operations into containers. Holds no state: everything lives in the run folder.</summary>
public sealed class JobService(IContainerRuntime containers)
{
    public const string RunLabel = "ntech.run";
    private const string JobLabel = "ntech.job";

    public static string ImageFor(string compute) => $"ntech-trainer:dev-{compute}";

    public static string TrainContainerName(string runId) => "ntech-train-" + runId;

    public static string ExportContainerName(string runId) => "ntech-export-" + runId;

    public static ContainerSpec TrainSpec(Workspace workspace, RunFolder run, RunRecord record, bool resume)
    {
        var command = new List<string> { "train", "--run-dir", "/run_dir" };
        if (resume) command.Add("--resume");
        return BaseSpec(workspace, run, record, TrainContainerName(run.Id), "train", command, record.Compute == "cuda");
    }

    public static ContainerSpec ExportSpec(Workspace workspace, RunFolder run, RunRecord record) =>
        BaseSpec(workspace, run, record, ExportContainerName(run.Id), "export", ["export", "--run-dir", "/run_dir"], false);

    public static ContainerSpec ValidateSpec(Workspace workspace, string image, int imgsz) => new(
        "ntech-validate-" + Guid.NewGuid().ToString("N")[..8],
        image,
        [new Mount(workspace.DatasetFolder, "/data", ReadOnly: true)],
        ["validate", "--data", workspace.ContainerDataYaml, "--imgsz", imgsz.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        new Dictionary<string, string> { [JobLabel] = "validate" },
        User: HostUser.ContainerUser());

    public static ContainerSpec ProbeSpec(string image, bool gpu) => new(
        "ntech-probe-" + Guid.NewGuid().ToString("N")[..8],
        image,
        [],
        ["probe"],
        new Dictionary<string, string> { [JobLabel] = "probe" },
        Gpus: gpu ? "all" : null,
        User: HostUser.ContainerUser());

    public async Task StartTrainAsync(Workspace workspace, RunFolder run, bool resume, CancellationToken cancellationToken = default)
    {
        workspace.RequireDataset();
        var record = run.Record();
        if (await containers.IsRunningAsync(RunLabel, run.Id, cancellationToken))
            throw new TrainerException($"Run {run.Id} đang chạy.");
        await EnsureSameImageAsync(record, resume, cancellationToken);
        run.ClearStop();
        await containers.StartDetachedAsync(TrainSpec(workspace, run, record, resume), cancellationToken);
    }

    public async Task StartExportAsync(Workspace workspace, RunFolder run, CancellationToken cancellationToken = default)
    {
        if (!run.HasBest) throw new TrainerException($"Run {run.Id} chưa có best.pt để export.");
        workspace.RequireDataset(); // export measures ONNX against PyTorch on val images
        if (await containers.IsRunningAsync(RunLabel, run.Id, cancellationToken))
            throw new TrainerException($"Run {run.Id} đang chạy.");
        await containers.StartDetachedAsync(ExportSpec(workspace, run, run.Record()), cancellationToken);
    }

    public async Task<string> StateAsync(RunFolder run, CancellationToken cancellationToken = default) =>
        RunStates.Derive(run.LastState(), await containers.IsRunningAsync(RunLabel, run.Id, cancellationToken));

    public Task<bool> IsRunningAsync(RunFolder run, CancellationToken cancellationToken = default) =>
        containers.IsRunningAsync(RunLabel, run.Id, cancellationToken);

    /// <summary>Ids of the runs that have a container now, train or export.</summary>
    public Task<IReadOnlySet<string>> RunningRunsAsync(CancellationToken cancellationToken = default) =>
        containers.RunningLabelValuesAsync(RunLabel, cancellationToken);

    /// <summary>A checkpoint continues only in the image that started it (design section 5.1).</summary>
    private async Task EnsureSameImageAsync(RunRecord record, bool resume, CancellationToken cancellationToken)
    {
        var digest = await containers.ImageDigestAsync(record.Image, cancellationToken)
            ?? throw new TrainerException($"Không có image {record.Image}. Build bằng docker/trainer/Dockerfile.");
        if (resume && digest != record.ImageDigest)
            throw new TrainerException($"Image {record.Image} đã đổi so với lúc bắt đầu run này, không train tiếp được.");
    }

    private static ContainerSpec BaseSpec(Workspace workspace, RunFolder run, RunRecord record, string name, string job, IReadOnlyList<string> command, bool gpu) => new(
        name,
        record.Image,
        [
            new Mount(workspace.DatasetFolder, "/data", ReadOnly: true),
            new Mount(run.Path_, "/run_dir", ReadOnly: false),
            new Mount(workspace.CacheFolder, "/cache", ReadOnly: false)
        ],
        command,
        new Dictionary<string, string> { [RunLabel] = run.Id, [JobLabel] = job },
        Gpus: gpu ? "all" : null,
        User: HostUser.ContainerUser());
}
