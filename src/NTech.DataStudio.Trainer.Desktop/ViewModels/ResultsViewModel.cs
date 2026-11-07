using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

public sealed record ResultRow(RunSummary Summary, bool ExportRunning)
{
    public string Id => Summary.Id;

    public string Model => Summary.Record.Model;

    public string StateText => ExportRunning ? "Đang export" : Labels.State(Summary.State);

    public string Map50 => Labels.Metric(Summary.Overall?.Map50);

    public string Map50To95 => Labels.Metric(Summary.Overall?.Map50To95);

    public string Precision => Labels.Metric(Summary.Overall?.Precision);

    public string Recall => Labels.Metric(Summary.Overall?.Recall);

    public string OnnxText => ExportRunning ? "Đang export" : Labels.ExportStatus(Summary.Export?.Status);

    public string LatencyText => Summary.Export?.LatencyMsMedian is { } ms ? Labels.Number(ms, "0.#") + " ms" : "-";

    public string FileText => Summary.Export?.FileMb is { } mb ? Labels.Number(mb, "0.#") + " MB" : "-";

    public override string ToString() => $"{Model} {Id}";
}

public sealed record ClassRow(string Name, string Precision, string Recall, string Map50, string Map50To95);

public sealed partial class ResultsViewModel : PageViewModel
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(2);

    public ResultsViewModel(TrainerSession session, TrainerServices services) : base(session, services)
    {
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(TrainerSession.Workspace)) return;
            Rows.Clear();
            SelectedRow = null;
            _signature = null;
        };
    }

    public override string Title => "Kết quả";

    public ObservableCollection<ResultRow> Rows { get; } = [];

    public ObservableCollection<ClassRow> Classes { get; } = [];

    public ObservableCollection<string> Differences { get; } = [];

    public ObservableCollection<string> ExportDetails { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRow), nameof(ClassTitle))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand), nameof(OpenExportsCommand))]
    private ResultRow? _selectedRow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _anySynthetic;

    [ObservableProperty]
    private string _tensorRtText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isStartingExport;

    public bool HasRow => SelectedRow is not null;

    public string ClassTitle => SelectedRow is { } row ? $"Theo class: {row.Model}" : "";

    public bool HasClasses => Classes.Count > 0;

    public bool HasExportDetails => ExportDetails.Count > 0;

    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    protected override void OnActivated()
    {
        _ = RefreshAsync();
        StartPolling(Tick, RefreshAsync);
    }

    public async Task RefreshAsync()
    {
        if (Session.Workspace is not { } workspace) return;
        IsLoading = Rows.Count == 0;
        try
        {
            await TryAsync(async () =>
            {
                var running = await Services.Jobs.RunningRunsAsync();
                var rows = new List<ResultRow>();
                foreach (var run in workspace.Runs().OrderByDescending(item => item.Id, StringComparer.Ordinal))
                {
                    if (!File.Exists(Path.Combine(run.Path_, "run.json"))) continue;
                    var live = running.Contains(run.Id);
                    var state = RunStates.Derive(run.LastState(), live);
                    var summary = RunSummary.Read(run, state);
                    // A container on a finished run is its export job.
                    rows.Add(new ResultRow(summary, live && RunStates.IsFinal(state)));
                }

                Replace(rows);
            });
        }
        finally
        {
            IsLoading = false;
        }

        TensorRtText = Session.Machine is { } machine
            ? "TensorRT: " + Assessor.TensorRt(machine).Reason
            : "TensorRT: mở màn hình Máy để đánh giá.";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (Session.Workspace is not { } workspace || SelectedRow is not { } row) return;
        IsStartingExport = true;
        try
        {
            if (await TryAsync(() => Services.Jobs.StartExportAsync(workspace, workspace.Run(row.Id))))
                await RefreshAsync();
        }
        finally
        {
            IsStartingExport = false;
        }
    }

    private bool CanExport() =>
        SelectedRow is { ExportRunning: false } row && row.Summary.CanExport && !IsStartingExport;

    [RelayCommand(CanExecute = nameof(HasRow))]
    private async Task OpenExportsAsync()
    {
        if (Session.Workspace is not { } workspace || SelectedRow is not { } row) return;
        var run = workspace.Run(row.Id);
        var exports = Path.Combine(run.Path_, "exports");
        await TryAsync(() => Services.Dialogs.OpenFolderAsync(Directory.Exists(exports) ? exports : run.Path_));
    }

    partial void OnSelectedRowChanged(ResultRow? value)
    {
        Classes.Clear();
        ExportDetails.Clear();
        OnPropertyChanged(nameof(HasClasses));
        OnPropertyChanged(nameof(HasExportDetails));
        if (value is null) return;
        foreach (var item in value.Summary.Classes)
            Classes.Add(new ClassRow(item.Name, Labels.Metric(item.Metrics.Precision), Labels.Metric(item.Metrics.Recall),
                Labels.Metric(item.Metrics.Map50), Labels.Metric(item.Metrics.Map50To95)));

        if (value.Summary.Export is { } export)
        {
            ExportDetails.Add($"Kiểm chứng: {Labels.ExportStatus(export.Status)}. So {export.BoxesCompared} box với PyTorch, {export.BoxesWithoutPair} box không có cặp"
                              + (export.HighestScoreWithoutPair is { } score ? $" (điểm cao nhất {Labels.Number(score, "0.###")})" : "") + ".");
            if (export.MinBoxIou is { } iou) ExportDetails.Add($"IoU thấp nhất của các cặp: {Labels.Number(iou, "0.#####")}.");
            ExportDetails.Add($"Độ trễ trung vị {LatencyOf(export)} trên {export.LatencyDevice ?? "không rõ"}; file {Labels.Number(export.FileMb, "0.#")} MB.");
            ExportDetails.Add("Bên dùng model phải cắt box về khung đầu vào: xem model.meta.json.");
            foreach (var problem in export.Problems) ExportDetails.Add("Vấn đề: " + problem);
        }

        OnPropertyChanged(nameof(HasClasses));
        OnPropertyChanged(nameof(HasExportDetails));
    }

    /// <summary>Replaces the rows only when something shown changed, so the list keeps its scroll and selection.</summary>
    private void Replace(List<ResultRow> rows)
    {
        var signature = string.Join("\n", rows.Select(Signature));
        if (signature == _signature) return;
        _signature = signature;

        var selected = SelectedRow?.Id;
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        SelectedRow = Rows.FirstOrDefault(row => row.Id == selected) ?? Rows.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));

        AnySynthetic = rows.Any(row => row.Summary.Synthetic);
        Differences.Clear();
        foreach (var reason in RunSummary.Differences(rows.Where(row => row.Summary.Overall is not null).Select(row => row.Summary)))
            Differences.Add(reason + " Bảng vẫn hiện nhưng không so sánh trực tiếp được.");
    }

    private string? _signature;

    private static string Signature(ResultRow row) =>
        $"{row.Id}|{row.StateText}|{row.OnnxText}|{row.Map50}|{row.Map50To95}|{row.LatencyText}|{row.Summary.HasBest}|{row.Summary.Synthetic}";

    private static string LatencyOf(ExportSummary export) =>
        export.LatencyMsMedian is { } ms ? Labels.Number(ms, "0.#") + " ms" : "-";
}
