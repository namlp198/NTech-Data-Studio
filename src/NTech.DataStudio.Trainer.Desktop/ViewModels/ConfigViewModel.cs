using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>What the model draws: boxes or masks. ToString is the label, which UI Automation reads.</summary>
public sealed record TaskItem(string Id)
{
    public string Label => Id == ModelFamily.Segment ? "Segment (mask theo polygon)" : "Detect (box)";

    public override string ToString() => Label;
}

/// <summary>A YOLO version in the list. ToString is the name, which screen readers and UI Automation read.</summary>
public sealed record FamilyItem(ModelFamily Family)
{
    public string Name => Family.DisplayName;

    public string StatusText => Family.Status == ModelStatus.Experimental ? "Thử nghiệm" : "Hỗ trợ";

    public bool IsExperimental => Family.Status == ModelStatus.Experimental;

    public string License => Family.License;

    public override string ToString() => Name;
}

public sealed partial class ConfigViewModel : PageViewModel
{
    private const string FlipLr = "fliplr";
    private const string FlipUd = "flipud";
    private const string Hue = "hsv_h";

    private HardwareSnapshot? _defaultsFrom;

    public ConfigViewModel(TrainerSession session, TrainerServices services) : base(session, services)
    {
        foreach (var task in services.Catalog.Tasks) Tasks.Add(new TaskItem(task));
        SelectedTask = Tasks.FirstOrDefault();
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(TrainerSession.Machine) or nameof(TrainerSession.Workspace)) Reassess();
        };
    }

    public override string Title => "Cấu hình";

    public ObservableCollection<TaskItem> Tasks { get; } = [];

    public ObservableCollection<FamilyItem> Families { get; } = [];

    public ObservableCollection<string> Sizes { get; } = [];

    public ObservableCollection<CriterionItem> Workload { get; } = [];

    public ObservableCollection<string> GateReasons { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSegment), nameof(TaskNote))]
    private TaskItem? _selectedTask;

    [ObservableProperty]
    private FamilyItem? _selectedFamily;

    [ObservableProperty]
    private string? _selectedSize;

    [ObservableProperty]
    private int _imgsz = TrainPlanner.DefaultImgsz;

    [ObservableProperty]
    private int _epochs = TrainPlanner.DefaultEpochs;

    [ObservableProperty]
    private bool _autoBatch = true;

    [ObservableProperty]
    private int _batch = TrainPlanner.CpuBatch;

    [ObservableProperty]
    private int _workers = 4;

    [ObservableProperty]
    private int _seed;

    [ObservableProperty]
    private int _patience = TrainPlanner.DefaultPatience;

    [ObservableProperty]
    private bool _preflight = true;

    [ObservableProperty]
    private bool _noFlipLeftRight;

    [ObservableProperty]
    private bool _noFlipUpDown;

    [ObservableProperty]
    private bool _keepHue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _riskAccepted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isStarting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlocked), nameof(NeedsConfirmation), nameof(GateText))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private GateKind? _gate;

    [ObservableProperty]
    private string _computeText = "Đang đọc cấu hình máy...";

    [ObservableProperty]
    private bool _isCuda;

    [ObservableProperty]
    private Grade? _workloadGrade;

    public bool IsSegment => SelectedTask?.Id == ModelFamily.Segment;

    public bool HasManyTasks => Tasks.Count > 1;

    public string TaskNote => IsSegment
        ? "Segment cần dataset polygon cho mọi vật thể (box thì bị từ chối lúc bắt đầu train). mAP và metric tính trên mask; box vẫn được báo riêng. ONNX có hai output: box kèm hệ số mask, và proto mask."
        : "";

    public bool IsBlocked => Gate == GateKind.Blocked;

    public bool NeedsConfirmation => Gate == GateKind.NeedsConfirmation;

    public string GateText => Gate switch
    {
        GateKind.Blocked => "Không train được trên máy này:",
        GateKind.NeedsConfirmation => "Cần xác nhận trước khi train:",
        GateKind.Allowed => "Máy đáp ứng cấu hình này.",
        _ => ""
    };

    public string LicenseText => SelectedFamily is { } family
        ? $"{family.Name} dùng license {family.License}. Theo Ultralytics, model train bằng gói này thuộc AGPL-3.0 kể cả dùng nội bộ, trừ khi có Enterprise License. Cần kết luận license trước khi đưa model vào sản xuất hoặc bán."
        : "";

    public ModelVariant? Model => SelectedFamily is { } family && SelectedSize is { } size ? Services.Catalog.Find(family.Family.Id + size) : null;

    public string ModelName => Model?.Name ?? "";

    protected override void OnActivated()
    {
        if (Session.Machine is null) _ = TryAsync(async () => await MachineAsync());
        Reassess();
    }

    partial void OnSelectedTaskChanged(TaskItem? value)
    {
        Families.Clear();
        if (value is not null)
            foreach (var family in Services.Catalog.Visible.Where(item => item.Task == value.Id)) Families.Add(new FamilyItem(family));
        SelectedFamily = Families.FirstOrDefault();
    }

    partial void OnSelectedFamilyChanged(FamilyItem? value)
    {
        Sizes.Clear();
        if (value is not null)
            foreach (var size in value.Family.Sizes) Sizes.Add(size);
        SelectedSize = Sizes.Contains("s") ? "s" : Sizes.FirstOrDefault();
        OnPropertyChanged(nameof(LicenseText));
        // The size may stay "s" from one version to the next, which raises no change of its own.
        Reassess();
    }

    partial void OnSelectedSizeChanged(string? value) => Reassess();

    partial void OnImgszChanged(int value) => Reassess();

    /// <summary>The same rules as the CLI (design section 7.2): blocked only for what cannot work, a confirmation for estimates.</summary>
    private void Reassess()
    {
        OnPropertyChanged(nameof(ModelName));
        RiskAccepted = false;
        Workload.Clear();
        GateReasons.Clear();
        if (Session.Machine is not { } machine || Model is not { } model || Imgsz <= 0)
        {
            Gate = null;
            WorkloadGrade = null;
            return;
        }

        if (!ReferenceEquals(machine, _defaultsFrom))
        {
            // Defaults follow the machine once; later edits by the user are kept.
            _defaultsFrom = machine;
            IsCuda = TrainPlanner.ComputeFor(machine) == "cuda";
            AutoBatch = IsCuda;
            Workers = TrainPlanner.DefaultWorkers(machine);
        }

        ComputeText = Assessor.UsableGpu(machine) is { } gpu
            ? $"Train trên {gpu.Name} ({Labels.Gb(gpu.VramGb)} VRAM)."
            : "Train bằng CPU: không có GPU dùng được.";

        var workload = Assessor.Workload(machine, model, Imgsz);
        foreach (var criterion in workload.Criteria) Workload.Add(CriterionItem.From(criterion));
        WorkloadGrade = workload.Overall;

        var gate = Assessor.TrainGate(machine, model, Imgsz);
        foreach (var reason in gate.Reasons) GateReasons.Add(reason);
        Gate = gate.Kind;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (Session.Workspace is not { } workspace || Session.Machine is not { } machine || Model is not { } model) return;
        if (Imgsz is < 32 or > 8192 || Epochs < 1 || Workers < 0 || Patience < 1 || (!AutoBatch && Batch < 1))
        {
            Error = "Kiểm tra lại các ô số: imgsz 32 đến 8192, epoch và patience từ 1, batch từ 1.";
            return;
        }

        IsStarting = true;
        try
        {
            await TryAsync(async () =>
            {
                var compute = TrainPlanner.ComputeFor(machine);
                var image = JobService.ImageFor(compute);
                var digest = await Services.Containers.ImageDigestAsync(image)
                    ?? throw new TrainerException($"Không có image {image}. Build bằng docker/trainer/Dockerfile, xem trainer/README.md.");
                var options = new TrainOptions(
                    Imgsz,
                    Epochs,
                    AutoBatch && compute == "cuda" ? TrainPlanner.AutoBatch : Batch,
                    TrainPlanner.DeviceFor(compute),
                    Workers,
                    Seed,
                    Patience,
                    Preflight,
                    Overrides());
                var run = workspace.CreateRun(model, options, compute, image, digest, DateTime.UtcNow);
                await Services.Jobs.StartTrainAsync(workspace, run, resume: false);
                RiskAccepted = false;
                Session.AnnounceRun(run.Id);
            });
        }
        finally
        {
            IsStarting = false;
        }
    }

    private bool CanStart() =>
        Session.HasWorkspace && Model is not null && !IsStarting
        && Gate is GateKind.Allowed or GateKind.NeedsConfirmation
        && (Gate == GateKind.Allowed || RiskAccepted);

    /// <summary>Suggestions of design section 6.3, applied only when ticked. Numbers stay numbers for the engine.</summary>
    public IReadOnlyDictionary<string, object>? Overrides()
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (NoFlipLeftRight) values[FlipLr] = 0.0;
        if (NoFlipUpDown) values[FlipUd] = 0.0;
        if (KeepHue) values[Hue] = 0.0;
        return values.Count == 0 ? null : values;
    }
}
