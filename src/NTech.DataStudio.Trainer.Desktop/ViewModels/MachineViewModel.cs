using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>One judged criterion, with its reason, source and confidence always next to the grade.</summary>
public sealed record CriterionItem(string Name, Grade Grade, string Reason, string Source)
{
    public string GradeText => Grade.ToString();

    public static CriterionItem From(Criterion criterion) =>
        new(criterion.Name, criterion.Grade, criterion.Reason, $"Nguồn: {criterion.Source}, độ tin cậy: {Labels.Confidence(criterion.Confidence)}");
}

public sealed record KeyValueItem(string Key, string Value);

public sealed partial class MachineViewModel(TrainerSession session, TrainerServices services) : PageViewModel(session, services)
{
    public override string Title => "Máy";

    public ObservableCollection<CriterionItem> Criteria { get; } = [];

    public ObservableCollection<KeyValueItem> Details { get; } = [];

    public ObservableCollection<KeyValueItem> ContainerDetails { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(ProbeContainerCommand))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(IsEmpty))]
    private Grade? _overall;

    [ObservableProperty]
    private string _computeText = "";

    [ObservableProperty]
    private string _tensorRtText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProbeContainerCommand))]
    private bool _isProbingContainer;

    [ObservableProperty]
    private string? _containerError;

    public bool HasResult => Overall is not null;

    public bool IsEmpty => !IsLoading && Overall is null && !HasError;

    public string OverallMeaning => Overall is { } grade ? Labels.Meaning(grade) : "";

    protected override void OnActivated()
    {
        if (Overall is null && !IsLoading) RefreshCommand.Execute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await TryAsync(async () =>
            {
                Session.Machine = null;
                Show(await MachineAsync());
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRefresh() => !IsLoading;

    /// <summary>Level 3 of the check (design section 7.1): what the runtime sees from inside a container.</summary>
    [RelayCommand(CanExecute = nameof(CanProbeContainer))]
    private async Task ProbeContainerAsync()
    {
        IsProbingContainer = true;
        ContainerError = null;
        ContainerDetails.Clear();
        try
        {
            var machine = await MachineAsync();
            var compute = TrainPlanner.ComputeFor(machine);
            var image = JobService.ImageFor(compute);
            if (await Services.Containers.ImageDigestAsync(image) is null)
            {
                ContainerError = $"Không có image {image}. Build bằng docker/trainer/Dockerfile, xem trainer/README.md.";
                return;
            }

            var result = await Services.Containers.RunToEndAsync(JobService.ProbeSpec(image, compute == "cuda"));
            if (!result.Succeeded)
            {
                ContainerError = "Container không chạy được: " + result.Error.Trim();
                return;
            }

            using var document = JsonDocument.Parse(result.Output);
            foreach (var property in document.RootElement.EnumerateObject())
                ContainerDetails.Add(new KeyValueItem(property.Name, property.Value.ValueKind == JsonValueKind.Null ? "không có" : property.Value.ToString()));
        }
        catch (Exception problem) when (IsExpected(problem) || problem is JsonException)
        {
            ContainerError = problem.Message;
        }
        finally
        {
            IsProbingContainer = false;
        }
    }

    private bool CanProbeContainer() => !IsLoading && !IsProbingContainer;

    private void Show(HardwareSnapshot machine)
    {
        var result = Assessor.Machine(machine);
        Criteria.Clear();
        foreach (var criterion in result.Criteria) Criteria.Add(CriterionItem.From(criterion));

        Details.Clear();
        Details.Add(new KeyValueItem("Hệ điều hành", machine.Os));
        Details.Add(new KeyValueItem("CPU", $"{machine.CpuThreads} luồng"));
        Details.Add(new KeyValueItem("RAM", Labels.Gb(machine.RamGb)));
        foreach (var gpu in machine.Gpus)
            Details.Add(new KeyValueItem("GPU", $"{gpu.Name}, {Labels.Gb(gpu.VramGb)} (trống {Labels.Gb(gpu.VramFreeGb)}), driver {gpu.Driver}, SM {Labels.Number(gpu.ComputeCapability, "0.0")}"));
        if (machine.Gpus.Count == 0) Details.Add(new KeyValueItem("GPU", "nvidia-smi không thấy GPU NVIDIA"));
        Details.Add(new KeyValueItem("Docker", machine.Docker.Available
            ? $"{machine.Docker.Version}, {machine.Docker.Cpus} CPU, {Labels.Gb(machine.Docker.MemoryGb)}, runtime nvidia: {(machine.Docker.HasNvidiaRuntime ? "có" : "không")}"
            : machine.Docker.Problem ?? "không dùng được"));
        Details.Add(new KeyValueItem("Đĩa", $"{Labels.Gb(machine.DiskFreeGb)} trống tại {machine.DiskPath}"));

        var gpuUsed = Assessor.UsableGpu(machine);
        ComputeText = gpuUsed is null
            ? "Không có GPU dùng được: job sẽ chạy bằng CPU, chậm hơn GPU rất nhiều. Train bằng CPU phải xác nhận."
            : $"Job sẽ chạy trên {gpuUsed.Name}, image {JobService.ImageFor("cuda")}.";
        var tensorRt = Assessor.TensorRt(machine);
        TensorRtText = $"ONNX có trên mọi máy. TensorRT {(tensorRt.Eligible ? "đủ điều kiện" : "không khả dụng")}: {tensorRt.Reason}";
        Overall = result.Overall;
        OnPropertyChanged(nameof(OverallMeaning));
    }
}
