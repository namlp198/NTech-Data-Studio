using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>A class row; Share is its part of the largest class, for the bar.</summary>
public sealed record ClassCountItem(string Name, int Boxes, double Share);

public sealed partial class DatasetViewModel : PageViewModel
{
    public DatasetViewModel(TrainerSession session, TrainerServices services) : base(session, services)
    {
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(TrainerSession.Workspace)) return;
            Clear();
            OnPropertyChanged(nameof(DataYaml));
            CheckCommand.NotifyCanExecuteChanged();
        };
    }

    public override string Title => "Dataset";

    public ObservableCollection<DatasetIssue> Errors { get; } = [];

    public ObservableCollection<DatasetIssue> Warnings { get; } = [];

    public ObservableCollection<SplitCount> Splits { get; } = [];

    public ObservableCollection<ClassCountItem> Classes { get; } = [];

    public string DataYaml => Session.Workspace?.File_.DataYaml ?? "";

    [ObservableProperty]
    private int _imgsz = TrainPlanner.DefaultImgsz;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(IsEmpty), nameof(Verdict), nameof(SizeText), nameof(Failed), nameof(Synthetic), nameof(Fingerprint))]
    private DatasetReport? _report;

    public bool Failed => Report is { Ok: false };

    public bool Synthetic => Report is { Synthetic: true };

    public string Fingerprint => Report?.Fingerprint ?? "";

    public bool HasReport => Report is not null;

    public bool IsEmpty => Report is null && !IsChecking;

    public string Verdict => Report is null ? "" : Report.Ok ? "Dataset đạt: train được." : "Dataset chưa đạt: sửa các lỗi dưới đây rồi kiểm tra lại.";

    public string SizeText => Report?.MinSideP5 is { } p5
        ? $"Cạnh ngắn của vật thể ở imgsz {Report.Imgsz}: 5% nhỏ nhất dưới {p5:0.#} px, trung vị {Report.MinSideP50:0.#} px."
        : "Không đo được kích thước vật thể.";

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        if (Session.Workspace is not { } workspace) return;
        if (Imgsz is < 32 or > 8192)
        {
            Error = "imgsz phải trong khoảng 32 đến 8192.";
            return;
        }

        IsChecking = true;
        try
        {
            await TryAsync(async () =>
            {
                var machine = await MachineAsync();
                Show(await Services.Datasets.CheckAsync(workspace, JobService.ImageFor(TrainPlanner.ComputeFor(machine)), Imgsz));
            });
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanCheck() => Session.HasWorkspace && !IsChecking;

    private void Show(DatasetReport report)
    {
        Clear();
        foreach (var item in report.Errors) Errors.Add(item);
        // The synthetic flag has its own notice on the screen; listing it again adds nothing.
        foreach (var item in report.Warnings.Where(item => item.Code != "synthetic")) Warnings.Add(item);
        foreach (var item in report.Splits) Splits.Add(item);
        var largest = Math.Max(1, report.Classes.Select(item => item.Boxes).DefaultIfEmpty(0).Max());
        foreach (var item in report.Classes) Classes.Add(new ClassCountItem(item.Name, item.Boxes, (double)item.Boxes / largest));
        Report = report;
        Session.DatasetReport = report;
    }

    private void Clear()
    {
        Errors.Clear();
        Warnings.Clear();
        Splits.Clear();
        Classes.Clear();
        Report = null;
        Error = null;
    }
}
