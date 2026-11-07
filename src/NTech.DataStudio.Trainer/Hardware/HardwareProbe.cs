using System.Globalization;
using System.Runtime.InteropServices;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Processes;

namespace NTech.DataStudio.Trainer.Hardware;

public static class Units
{
    /// <summary>1024^3, the figure Windows and nvidia-smi show. Mixing it with 10^9 made 15.8 GB read as 17.</summary>
    public const double BytesPerGb = 1024d * 1024 * 1024;
}

public sealed record GpuInfo(string Name, double VramGb, double VramFreeGb, string Driver, double? ComputeCapability);

public sealed record HardwareSnapshot(
    string Os,
    int CpuThreads,
    double RamGb,
    double DiskFreeGb,
    string DiskPath,
    IReadOnlyList<GpuInfo> Gpus,
    DockerInfo Docker);

public static class NvidiaSmi
{
    public static readonly string[] QueryArguments =
        ["--query-gpu=name,memory.total,memory.free,driver_version,compute_cap", "--format=csv,noheader,nounits"];

    /// <summary>Expects lines of the query above. A line it cannot read is skipped.</summary>
    public static List<GpuInfo> Parse(string output)
    {
        var gpus = new List<GpuInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 5) continue;
            if (!TryNumber(parts[1], out var total) || !TryNumber(parts[2], out var free)) continue;
            gpus.Add(new GpuInfo(
                parts[0],
                Math.Round(total / 1024, 1),
                Math.Round(free / 1024, 1),
                parts[3],
                TryNumber(parts[4], out var capability) ? capability : null));
        }

        return gpus;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

public sealed class HardwareProbe(IProcessRunner runner, IContainerRuntime containers)
{
    public async Task<HardwareSnapshot> ReadAsync(string diskPath, CancellationToken cancellationToken = default)
    {
        var smi = await runner.RunAsync("nvidia-smi", NvidiaSmi.QueryArguments, cancellationToken);
        var gpus = smi.Succeeded ? NvidiaSmi.Parse(smi.Output) : [];
        var docker = await containers.InfoAsync(cancellationToken);

        return new HardwareSnapshot(
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / Units.BytesPerGb, 1),
            DiskFree(diskPath),
            diskPath,
            gpus,
            docker);
    }

    private static double DiskFree(string path)
    {
        var existing = Path.GetFullPath(path);
        while (!Directory.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null) break;
            existing = parent;
        }

        return Math.Round(new DriveInfo(existing).AvailableFreeSpace / Units.BytesPerGb, 1);
    }
}
