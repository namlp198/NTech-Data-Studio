using System.Runtime.InteropServices;
using System.Text.Json;
using NTech.DataStudio.Trainer.Processes;

namespace NTech.DataStudio.Trainer.Containers;

public sealed record Mount(string HostPath, string ContainerPath, bool ReadOnly);

public sealed record ContainerSpec(
    string Name,
    string Image,
    IReadOnlyList<Mount> Mounts,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string> Labels,
    string? Gpus = null,
    string ShmSize = "2g",
    string? User = null);

public sealed record DockerInfo(bool Available, string? Version, bool HasNvidiaRuntime, int Cpus, double MemoryGb, string? Problem);

public interface IContainerRuntime
{
    Task<DockerInfo> InfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the image is not on this machine.</summary>
    Task<string?> ImageDigestAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>Starts and returns at once: the job keeps running if this app closes.</summary>
    Task StartDetachedAsync(ContainerSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Runs to the end and returns the output, for short jobs such as a probe.</summary>
    Task<ProcessResult> RunToEndAsync(ContainerSpec spec, CancellationToken cancellationToken = default);

    Task<bool> IsRunningAsync(string label, string value, CancellationToken cancellationToken = default);

    Task StopAsync(string name, CancellationToken cancellationToken = default);
}

public static class DockerArguments
{
    public static List<string> Run(ContainerSpec spec, bool detached)
    {
        var arguments = new List<string> { "run", "--rm", "--init", "--name", spec.Name, "--shm-size", spec.ShmSize };
        if (detached) arguments.Add("-d");
        if (spec.Gpus is not null) arguments.AddRange(["--gpus", spec.Gpus]);
        if (spec.User is not null) arguments.AddRange(["--user", spec.User, "-e", "HOME=/tmp"]);
        foreach (var (key, value) in spec.Labels) arguments.AddRange(["--label", $"{key}={value}"]);
        foreach (var mount in spec.Mounts)
        {
            // --mount, because -v splits on ':' and a Windows path has one.
            var text = $"type=bind,source={mount.HostPath},target={mount.ContainerPath}";
            arguments.AddRange(["--mount", mount.ReadOnly ? text + ",readonly" : text]);
        }

        arguments.Add(spec.Image);
        arguments.AddRange(spec.Command);
        return arguments;
    }
}

public sealed class DockerCli(IProcessRunner runner) : IContainerRuntime
{
    private const string Docker = "docker";

    public async Task<DockerInfo> InfoAsync(CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(Docker, ["info", "--format", "{{json .}}"], cancellationToken);
        if (result.ExitCode == ProcessRunner.NotFound)
            return new DockerInfo(false, null, false, 0, 0, "Chưa cài Docker.");
        if (!result.Succeeded)
            return new DockerInfo(false, null, false, 0, 0, "Docker chưa chạy hoặc không có quyền truy cập.");

        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            if (root.TryGetProperty("ServerErrors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                return new DockerInfo(false, null, false, 0, 0, "Docker chưa chạy hoặc không có quyền truy cập.");

            var hasNvidia = root.TryGetProperty("Runtimes", out var runtimes) && runtimes.TryGetProperty("nvidia", out _);
            return new DockerInfo(
                true,
                root.GetProperty("ServerVersion").GetString(),
                hasNvidia,
                root.GetProperty("NCPU").GetInt32(),
                root.GetProperty("MemTotal").GetInt64() / Hardware.Units.BytesPerGb,
                null);
        }
        catch (Exception problem) when (problem is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new DockerInfo(false, null, false, 0, 0, "Không đọc được thông tin Docker: " + problem.Message);
        }
    }

    public async Task<string?> ImageDigestAsync(string image, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(Docker, ["image", "inspect", "--format", "{{.Id}}", image], cancellationToken);
        return result.Succeeded ? result.Output.Trim() : null;
    }

    public async Task StartDetachedAsync(ContainerSpec spec, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(Docker, DockerArguments.Run(spec, detached: true), cancellationToken);
        if (!result.Succeeded) throw new ContainerException($"Không chạy được container: {result.Error.Trim()}");
    }

    public Task<ProcessResult> RunToEndAsync(ContainerSpec spec, CancellationToken cancellationToken = default) =>
        runner.RunAsync(Docker, DockerArguments.Run(spec, detached: false), cancellationToken);

    public async Task<bool> IsRunningAsync(string label, string value, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(Docker, ["ps", "--filter", $"label={label}={value}", "--format", "{{.ID}}"], cancellationToken);
        return result.Succeeded && !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task StopAsync(string name, CancellationToken cancellationToken = default) =>
        await runner.RunAsync(Docker, ["stop", name], cancellationToken);
}

public sealed class ContainerException(string message) : Exception(message);

/// <summary>On Linux a container defaults to root, which would leave result files the user cannot delete.</summary>
public static class HostUser
{
    public static string? ContainerUser() =>
        OperatingSystem.IsLinux() ? $"{Native.geteuid()}:{Native.getegid()}" : null;

    private static class Native
    {
        [DllImport("libc")]
        public static extern uint geteuid();

        [DllImport("libc")]
        public static extern uint getegid();
    }
}
