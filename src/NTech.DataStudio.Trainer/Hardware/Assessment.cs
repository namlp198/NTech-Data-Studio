using NTech.DataStudio.Trainer.Catalog;

namespace NTech.DataStudio.Trainer.Hardware;

public enum Grade
{
    Excellent,
    Good,
    Moderate,
    Unknown,
    Weak,
    Inadequate
}

public enum Confidence
{
    Assumed,
    Reported,
    Measured
}

/// <summary>One judged criterion. The source and the confidence stay visible next to the grade.</summary>
public sealed record Criterion(string Name, Grade Grade, string Reason, string Source, Confidence Confidence);

public sealed record AssessmentResult(Grade Overall, IReadOnlyList<Criterion> Criteria)
{
    public static AssessmentResult From(IReadOnlyList<Criterion> criteria) =>
        new(criteria.Count == 0 ? Grade.Unknown : criteria.Max(criterion => criterion.Grade), criteria);
}

public enum GateKind
{
    Allowed,
    NeedsConfirmation,
    Blocked
}

public sealed record Gate(GateKind Kind, IReadOnlyList<string> Reasons);

public sealed record TensorRtAvailability(bool Eligible, string Reason);

/// <summary>
/// Every threshold here is an initial assumption (design sections 7.2 and 7.3), to be calibrated
/// with what preflight measures. The rules stay pure so the tests can walk every machine type.
/// </summary>
public static class Assessor
{
    /// <summary>The CUDA image follows current PyTorch wheels, which need Turing or newer.</summary>
    public const double MinComputeCapability = 7.5;

    public const double BlockBelowDiskGb = 5;

    /// <summary>The GPU the job would use, or null when it would have to run on CPU.</summary>
    public static GpuInfo? UsableGpu(HardwareSnapshot machine) =>
        machine.Gpus
            .Where(gpu => gpu.ComputeCapability is >= MinComputeCapability)
            .OrderByDescending(gpu => gpu.VramGb)
            .FirstOrDefault();

    public static AssessmentResult Machine(HardwareSnapshot machine)
    {
        return AssessmentResult.From([
            GpuCriterion(machine),
            Threshold("CPU", machine.CpuThreads, [16, 8, 6, 4], $"{machine.CpuThreads} luồng", "hệ điều hành"),
            RamCriterion(machine),
            Threshold("Đĩa", machine.DiskFreeGb, [100, 50, 20, BlockBelowDiskGb],
                $"còn {machine.DiskFreeGb:0.#} GB tại {machine.DiskPath}", "hệ điều hành"),
            machine.Docker.Available
                ? new Criterion("Docker", Grade.Good, $"phiên bản {machine.Docker.Version}", "docker", Confidence.Reported)
                : new Criterion("Docker", Grade.Inadequate, machine.Docker.Problem ?? "không dùng được", "docker", Confidence.Reported)
        ]);
    }

    public static AssessmentResult Workload(HardwareSnapshot machine, ModelVariant model, int imgsz)
    {
        var gpu = UsableGpu(machine);
        if (gpu is null)
        {
            return AssessmentResult.From([
                new Criterion(model.Name, Grade.Inadequate, "Sẽ train bằng CPU: chậm hơn GPU rất nhiều.", "nvidia-smi", Confidence.Reported)
            ]);
        }

        var need = model.VramAt640;
        if (need is null)
        {
            return AssessmentResult.From([
                new Criterion(model.Name, Grade.Unknown, "Phiên bản này chưa có số đo VRAM. Bước chạy thử sẽ đo.", "danh mục", Confidence.Assumed)
            ]);
        }

        var scale = Math.Pow(imgsz / 640.0, 2);
        var minimum = need.Min * scale;
        var recommended = need.Recommended * scale;
        var grade = gpu.VramGb >= recommended ? Grade.Good
            : gpu.VramGb >= minimum ? Grade.Moderate
            : gpu.VramGb >= minimum * 0.75 ? Grade.Weak
            : Grade.Inadequate;
        var reason = $"VRAM {gpu.VramGb:0.#} GB; ước tính ở imgsz {imgsz}: tối thiểu {minimum:0.#} GB, khuyến nghị {recommended:0.#} GB.";
        return AssessmentResult.From([new Criterion(model.Name, grade, reason, "danh mục", Confidence.Assumed)]);
    }

    /// <summary>
    /// Blocks only what certainly cannot work. Estimates are assumptions, so they ask for a
    /// confirmation instead of refusing. Training on CPU always asks.
    /// </summary>
    public static Gate TrainGate(HardwareSnapshot machine, ModelVariant model, int imgsz)
    {
        var blocked = new List<string>();
        if (!machine.Docker.Available) blocked.Add(machine.Docker.Problem ?? "Docker không dùng được.");
        if (machine.DiskFreeGb < BlockBelowDiskGb) blocked.Add($"Đĩa chỉ còn {machine.DiskFreeGb:0.#} GB, cần từ {BlockBelowDiskGb} GB.");
        if (model.Family.Status == ModelStatus.Hidden) blocked.Add($"{model.Name} không có trong danh mục dùng được.");
        if (blocked.Count > 0) return new Gate(GateKind.Blocked, blocked);

        var confirm = new List<string>();
        if (UsableGpu(machine) is null) confirm.Add(CpuWarning(machine));
        else
        {
            foreach (var criterion in Workload(machine, model, imgsz).Criteria)
                if (criterion.Grade == Grade.Inadequate) confirm.Add(criterion.Reason);
        }

        if (model.Family.Status == ModelStatus.Experimental)
            confirm.Add($"{model.Name} đang ở mức thử nghiệm: chưa được kiểm thử đầy đủ.");

        return confirm.Count > 0 ? new Gate(GateKind.NeedsConfirmation, confirm) : new Gate(GateKind.Allowed, []);
    }

    public static TensorRtAvailability TensorRt(HardwareSnapshot machine)
    {
        var gpu = machine.Gpus.OrderByDescending(item => item.ComputeCapability ?? 0).FirstOrDefault();
        if (gpu is null) return new TensorRtAvailability(false, "Cần GPU NVIDIA.");
        if (gpu.ComputeCapability is not >= MinComputeCapability)
            return new TensorRtAvailability(false, $"{gpu.Name} có SM {gpu.ComputeCapability:0.0}, cần từ {MinComputeCapability:0.0}.");
        return new TensorRtAvailability(true, "Máy đủ điều kiện. Tính năng này chưa được làm (mốc M5).");
    }

    private static string CpuWarning(HardwareSnapshot machine)
    {
        if (machine.Gpus.Count == 0) return "Không có GPU NVIDIA: sẽ train bằng CPU, chậm hơn rất nhiều.";
        var gpu = machine.Gpus[0];
        return $"{gpu.Name} (SM {gpu.ComputeCapability:0.0}) cũ hơn mức image hỗ trợ (từ {MinComputeCapability:0.0}): sẽ train bằng CPU, chậm hơn rất nhiều.";
    }

    private static Criterion GpuCriterion(HardwareSnapshot machine)
    {
        var gpu = UsableGpu(machine);
        if (gpu is null) return new Criterion("GPU", Grade.Inadequate, CpuWarning(machine), "nvidia-smi", Confidence.Reported);
        return Threshold("GPU", gpu.VramGb, [16, 8, 6, 4],
            $"{gpu.Name}, {gpu.VramGb:0.#} GB VRAM, SM {gpu.ComputeCapability:0.0}", "nvidia-smi");
    }

    private static Criterion RamCriterion(HardwareSnapshot machine)
    {
        // What the container can really use, which on Docker Desktop can be far below the host.
        var usable = machine.Docker.Available && machine.Docker.MemoryGb > 0
            ? Math.Min(machine.RamGb, machine.Docker.MemoryGb)
            : machine.RamGb;
        var reason = usable < machine.RamGb
            ? $"Docker được dùng {usable:0.#} GB trên {machine.RamGb:0.#} GB của máy"
            : $"{usable:0.#} GB";
        // A "16 GB" machine reports about 15.8, so each limit sits just under the marketed size.
        return Threshold("RAM", usable, [31, 15, 11, 7], reason, "docker");
    }

    private static Criterion Threshold(string name, double value, double[] limits, string reason, string source)
    {
        var grade = value >= limits[0] ? Grade.Excellent
            : value >= limits[1] ? Grade.Good
            : value >= limits[2] ? Grade.Moderate
            : value >= limits[3] ? Grade.Weak
            : Grade.Inadequate;
        return new Criterion(name, grade, reason, source, Confidence.Reported);
    }
}
