using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Desktop.Services;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>
/// One screen. A page that polls runs its timer only while shown: the loop is started by
/// Activate on the UI thread, so each tick resumes there and never overlaps the previous one.
/// </summary>
public abstract partial class PageViewModel : ObservableObject, IActivatable
{
    private CancellationTokenSource? _polling;

    protected PageViewModel(TrainerSession session, TrainerServices services)
    {
        Session = session;
        Services = services;
    }

    public TrainerSession Session { get; }

    protected TrainerServices Services { get; }

    public abstract string Title { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsActive { get; private set; }

    public void Activate()
    {
        IsActive = true;
        OnActivated();
    }

    public void Deactivate()
    {
        IsActive = false;
        StopPolling();
        OnDeactivated();
    }

    protected virtual void OnActivated()
    {
    }

    protected virtual void OnDeactivated()
    {
    }

    /// <summary>Tests turn the timer off and call each page's refresh themselves.</summary>
    protected void StartPolling(TimeSpan interval, Func<Task> tick)
    {
        StopPolling();
        if (!Services.Polling) return;
        var cancel = new CancellationTokenSource();
        _polling = cancel;
        _ = PollAsync(interval, tick, cancel.Token);
    }

    protected void StopPolling()
    {
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    private bool _errorFromRefresh;

    /// <summary>
    /// Runs an action and turns the failures a user can act on into the page's error text. A
    /// periodic refresh only clears an error it raised itself: a failed button press must stay on
    /// screen until the next press, not vanish at the next tick.
    /// </summary>
    protected async Task<bool> TryAsync(Func<Task> action, bool refresh = false)
    {
        try
        {
            await action();
            if (!refresh || _errorFromRefresh) Error = null;
            return true;
        }
        catch (Exception problem) when (IsExpected(problem))
        {
            Error = problem.Message;
            _errorFromRefresh = refresh;
            return false;
        }
    }

    /// <summary>Reads the machine once per session; a page that needs it before Machine was opened gets it here.</summary>
    protected async Task<HardwareSnapshot> MachineAsync()
    {
        if (Session.Machine is { } known) return known;
        var path = Session.Workspace?.Root ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var machine = await Services.Probe.ReadAsync(path);
        Session.Machine = machine;
        return machine;
    }

    public static bool IsExpected(Exception problem) =>
        problem is TrainerException or ContainerException or IOException or UnauthorizedAccessException or InvalidOperationException;

    private static async Task PollAsync(TimeSpan interval, Func<Task> tick, CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation))
                await tick();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
