using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

public sealed record ChartSeries(string Name, IReadOnlyList<double?> Values);

public enum LogFilter
{
    All,
    Warnings,
    Errors
}

/// <summary>A filter choice. ToString is the label, which UI Automation reads for a ComboBox item.</summary>
public sealed record LogFilterOption(LogFilter Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class RunItem(string id, string model, string task = "detect") : ObservableObject
{
    public string Id { get; } = id;

    public string Task { get; } = task;

    public string Model { get; } = model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private string _state = RunStates.Created;

    [ObservableProperty]
    private bool _containerRunning;

    public string StateText => Labels.State(State);

    public override string ToString() => $"{Model} {Id}";
}

public sealed partial class TrainViewModel : PageViewModel
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private const int TicksPerListRefresh = 3;

    private readonly RunStateTracker _tracker = new();
    private readonly HashSet<string> _stopRequested = new(StringComparer.Ordinal);
    private EventLogReader? _reader;
    private RunMonitor _monitor = new();
    private int _ticks;
    private string? _pendingSelection;

    public TrainViewModel(TrainerSession session, TrainerServices services) : base(session, services)
    {
        LogFilters = [new(LogFilter.All, "Tất cả"), new(LogFilter.Warnings, "Cảnh báo"), new(LogFilter.Errors, "Lỗi")];
        _selectedLogFilter = LogFilters[0];
        session.RunStarted += id => _pendingSelection = id;
        session.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(TrainerSession.Workspace)) return;
            Runs.Clear();
            SelectedRun = null;
        };
    }

    public override string Title => "Train";

    public ObservableCollection<RunItem> Runs { get; } = [];

    public ObservableCollection<LogLine> VisibleLog { get; } = [];

    public IReadOnlyList<LogFilterOption> LogFilters { get; }

    [ObservableProperty]
    private LogFilterOption _selectedLogFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRun), nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand), nameof(ResumeCommand), nameof(OpenFolderCommand))]
    private RunItem? _selectedRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _elapsedText = "-";

    [ObservableProperty]
    private string _etaText = "-";

    [ObservableProperty]
    private string _trainLoss = "-";

    [ObservableProperty]
    private string _valLoss = "-";

    [ObservableProperty]
    private string _map50 = "-";

    [ObservableProperty]
    private string _map50To95 = "-";

    [ObservableProperty]
    private string _precision = "-";

    [ObservableProperty]
    private string _recall = "-";

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _lossSeries = [];

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _mapSeries = [];

    [ObservableProperty]
    private bool _hasEpochs;

    [ObservableProperty]
    private string _cpuText = "-";

    [ObservableProperty]
    private string _ramText = "-";

    [ObservableProperty]
    private string _gpuText = "-";

    [ObservableProperty]
    private string _vramText = "-";

    [ObservableProperty]
    private string _diskText = "-";

    [ObservableProperty]
    private string _warningSummary = "";

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isSynthetic;

    public bool HasRun => SelectedRun is not null;

    public string SelectedModel => SelectedRun?.Model ?? "";

    /// <summary>A segment run is judged by its masks, so its mAP says so.</summary>
    public string MapPrefix => SelectedRun?.Task == "segment" ? "MASK " : "";

    public string SelectedId => SelectedRun?.Id ?? "";

    public string SelectedState => SelectedRun?.State ?? RunStates.Created;

    public string SelectedStateText => SelectedRun?.StateText ?? "";

    private void NotifySelected()
    {
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(SelectedId));
        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(SelectedStateText));
    }

    public bool IsEmpty => !IsLoading && Runs.Count == 0;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    partial void OnNoticeChanged(string? value) => OnPropertyChanged(nameof(HasNotice));

    protected override void OnActivated()
    {
        _ = RefreshAsync();
        StartPolling(Tick, async () =>
        {
            _ticks++;
            if (_ticks % TicksPerListRefresh == 0) await RefreshAsync();
            else ReadSelected();
        });
    }

    partial void OnSelectedRunChanged(RunItem? value)
    {
        _reader = null;
        _monitor = new RunMonitor();
        VisibleLog.Clear();
        Notice = null;
        NotifySelected();
        OnPropertyChanged(nameof(MapPrefix));
        IsSynthetic = value is not null && Session.Workspace is { } workspace && SyntheticRun(workspace, value.Id);
        ReadSelected();
    }

    partial void OnSelectedLogFilterChanged(LogFilterOption value)
    {
        VisibleLog.Clear();
        foreach (var line in _monitor.Log.Where(Visible)) VisibleLog.Add(line);
    }

    /// <summary>Reads the run list and which runs have a container. Public so tests drive it without the timer.</summary>
    public async Task RefreshAsync()
    {
        if (Session.Workspace is not { } workspace) return;
        IsLoading = Runs.Count == 0;
        try
        {
            await TryAsync(async () =>
            {
                var running = await Services.Jobs.RunningRunsAsync();
                var states = _tracker.Refresh(workspace, running);
                Merge(states);
            }, refresh: true);
        }
        finally
        {
            IsLoading = false;
        }

        if (_pendingSelection is { } wanted && Runs.FirstOrDefault(run => run.Id == wanted) is { } found)
        {
            _pendingSelection = null;
            SelectedRun = found;
        }
        else if (SelectedRun is null && Runs.Count > 0)
        {
            SelectedRun = Runs[0];
        }

        ReadSelected();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (Session.Workspace is not { } workspace || SelectedRun is not { } item) return;
        try
        {
            workspace.Run(item.Id).RequestStop();
            _stopRequested.Add(item.Id);
            Notice = "Đã yêu cầu dừng. Worker sẽ dừng ở cuối epoch hiện tại, sau khi lưu checkpoint để train tiếp.";
            StopCommand.NotifyCanExecuteChanged();
        }
        catch (Exception problem) when (IsExpected(problem))
        {
            Error = problem.Message;
        }
    }

    private bool CanStop() =>
        SelectedRun is { ContainerRunning: true } item && !_stopRequested.Contains(item.Id) && item.State is RunStates.Preflight or RunStates.Running;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        if (Session.Workspace is not { } workspace || SelectedRun is not { } item) return;
        await TryAsync(async () =>
        {
            var run = workspace.Run(item.Id);
            await Services.Jobs.StartTrainAsync(workspace, run, resume: true);
            _stopRequested.Remove(item.Id);
            Notice = "Đang train tiếp từ checkpoint.";
            item.ContainerRunning = true;
            ResumeCommand.NotifyCanExecuteChanged();
        });
    }

    private bool CanResume() =>
        SelectedRun is { ContainerRunning: false } item && Session.Workspace is { } workspace
        && RunStates.CanResume(item.State, workspace.Run(item.Id).HasCheckpoint);

    [RelayCommand(CanExecute = nameof(HasRun))]
    private async Task OpenFolderAsync()
    {
        if (Session.Workspace is not { } workspace || SelectedRun is not { } item) return;
        await TryAsync(() => Services.Dialogs.OpenFolderAsync(workspace.Run(item.Id).Path_));
    }

    private void Merge(IReadOnlyList<RunState> states)
    {
        // Newest first: the run just started is the one people look for.
        var ordered = states.OrderByDescending(state => state.Run.Id, StringComparer.Ordinal).ToList();
        var ids = ordered.Select(state => state.Run.Id).ToList();
        if (!ids.SequenceEqual(Runs.Select(run => run.Id)))
        {
            var selected = SelectedRun?.Id;
            Runs.Clear();
            foreach (var state in ordered)
            {
                var record = state.Run.Record();
                Runs.Add(new RunItem(state.Run.Id, record.Model, record.Task));
            }

            if (selected is not null) SelectedRun = Runs.FirstOrDefault(run => run.Id == selected);
            OnPropertyChanged(nameof(IsEmpty));
        }

        foreach (var (item, state) in Runs.Zip(ordered))
        {
            item.State = state.State;
            item.ContainerRunning = state.ContainerRunning;
            if (RunStates.IsFinal(state.State) && !state.ContainerRunning) _stopRequested.Remove(item.Id);
        }

        NotifySelected();
        StopCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    private void ReadSelected()
    {
        if (Session.Workspace is not { } workspace || SelectedRun is not { } item) return;
        try
        {
            _reader ??= new EventLogReader(workspace.Run(item.Id).EventsFile);
            var events = _reader.ReadNew();
            foreach (var trainerEvent in events)
            {
                _monitor.Apply(trainerEvent);
                if (EventText.Describe(trainerEvent) is { } line && Visible(line)) VisibleLog.Add(line);
            }

            ShowMonitor(item);
        }
        catch (Exception problem) when (IsExpected(problem))
        {
            Error = problem.Message;
        }
    }

    private void ShowMonitor(RunItem item)
    {
        var monitor = _monitor;
        var epochs = Math.Max(monitor.Epochs, 1);
        var within = monitor.Batches > 0 ? Math.Clamp((double)monitor.Batch / monitor.Batches, 0, 1) : 0;
        var done = monitor.Epoch == 0 ? 0 : monitor.Epoch - 1 + within;
        if (item.State == RunStates.Completed) done = epochs;
        ProgressPercent = monitor.Epochs == 0 ? 0 : Math.Clamp(done / epochs * 100, 0, 100);
        ProgressText = monitor.Epochs == 0
            ? Labels.State(item.State)
            : $"Epoch {monitor.Epoch}/{monitor.Epochs}" + (monitor.Batches > 0 ? $", batch {monitor.Batch}/{monitor.Batches}" : "");
        ElapsedText = Labels.Duration(monitor.Elapsed());
        EtaText = item.State is RunStates.Running && monitor.EtaSeconds is { } eta ? Labels.Duration(TimeSpan.FromSeconds(eta)) : "-";

        var latest = monitor.Latest;
        TrainLoss = Labels.Metric(latest?.TrainLoss);
        ValLoss = Labels.Metric(latest?.ValLoss);
        Map50 = Labels.Metric(latest?.Map50);
        Map50To95 = Labels.Metric(latest?.Map50To95);
        Precision = Labels.Metric(latest?.Precision);
        Recall = Labels.Metric(latest?.Recall);

        var history = monitor.EpochHistory;
        HasEpochs = history.Count > 0;
        LossSeries = [new("Train loss", history.Select(point => point.TrainLoss).ToList()), new("Val loss", history.Select(point => point.ValLoss).ToList())];
        MapSeries = [new("mAP50", history.Select(point => point.Map50).ToList()), new("mAP50-95", history.Select(point => point.Map50To95).ToList())];

        var resources = monitor.Resources;
        CpuText = resources?.CpuPercent is { } cpu ? $"{cpu:0}%" : "-";
        RamText = resources?.RamUsedGb is { } used ? $"{used:0.#} / {Labels.Gb(resources.RamTotalGb)}" : "-";
        GpuText = resources?.GpuPercent is { } gpu ? $"{gpu:0}%" + (resources.GpuTempC is { } temp ? $", {temp:0} °C" : "") : "Không dùng GPU";
        VramText = resources?.VramUsedGb is { } vram ? $"{vram:0.#} / {Labels.Gb(resources.VramTotalGb)}" : monitor.VramPeakGb is { } peak ? $"đỉnh {Labels.Gb(peak)}" : "-";
        DiskText = Labels.Gb(resources?.DiskFreeGb);

        WarningSummary = monitor.WarningCount + monitor.ErrorCount == 0
            ? "Không có cảnh báo"
            : $"{monitor.WarningCount} cảnh báo, {monitor.ErrorCount} lỗi";

        if (item.State == RunStates.Interrupted && !_stopRequested.Contains(item.Id))
        {
            Notice = CanResume()
                ? "Container đã dừng mà chưa báo kết thúc (mất điện, bị kill, Docker dừng). Có checkpoint: bấm Train tiếp."
                : "Container đã dừng mà chưa báo kết thúc, trước khi có checkpoint. Hãy train lại từ đầu; xem train.log trong thư mục run.";
        }
        else if (item.State == RunStates.Failed)
        {
            Notice = "Run lỗi. Xem log bên dưới và train.log trong thư mục run.";
        }
        else if (item.State == RunStates.Cancelled && !item.ContainerRunning)
        {
            Notice = CanResume()
                ? $"Đã dừng ở cuối epoch {monitor.Epoch}, checkpoint đã lưu. Bấm Train tiếp để chạy nốt."
                : $"Đã dừng ở cuối epoch {monitor.Epoch}.";
        }
        else if (item.State == RunStates.Completed || (!item.ContainerRunning && !RunStates.IsFinal(item.State)))
        {
            Notice = null;
        }
    }

    private bool Visible(LogLine line) => SelectedLogFilter.Value switch
    {
        LogFilter.Warnings => line.Level != LogLevel.Info,
        LogFilter.Errors => line.Level == LogLevel.Error,
        _ => true
    };

    private static bool SyntheticRun(Workspace workspace, string id)
    {
        var dataset = JsonFiles.Read(Path.Combine(workspace.RunsFolder, id, "dataset.json"));
        return dataset is { } value && value.TryGetProperty("synthetic", out var flag) && flag.ValueKind == System.Text.Json.JsonValueKind.True;
    }
}
