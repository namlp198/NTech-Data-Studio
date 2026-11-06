using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

public sealed record Option<T>(string Label, T Value);

/// <summary>What every wizard step shares: background work with progress, cancel and error reporting.</summary>
public abstract partial class PrepareStepViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _resultMessage;

    protected PrepareStepViewModel(ProjectService projects)
    {
        Projects = projects;
    }

    protected ProjectService Projects { get; }

    /// <summary>Called when the step is shown, so it always reflects what earlier steps did.</summary>
    public abstract Task ReloadAsync();

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cancellation?.Cancel();

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread with its own store.
    /// It returns the update to apply on the UI thread when it finishes.
    /// </summary>
    protected async Task RunAsync(
        string label,
        Func<IPrepareStore, IProgress<PrepareProgress>, CancellationToken, Action> work)
    {
        ErrorMessage = null;
        ResultMessage = null;
        ProgressPercent = 0;
        ProgressText = label;
        IsBusy = true;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var progress = new Progress<PrepareProgress>(report =>
        {
            ProgressPercent = report.Total == 0 ? 0 : 100.0 * report.Done / report.Total;
            ProgressText = report.Current is null
                ? $"{label} {report.Done} / {report.Total}"
                : $"{label} {report.Done} / {report.Total}: {report.Current}";
        });

        try
        {
            var finish = await Task.Run(() =>
            {
                using var store = Projects.OpenPrepareStore();
                return work(store, progress, cancellation.Token);
            });
            finish();
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "Đã hủy. Phần đã xong được giữ lại; chạy lại sẽ làm tiếp phần còn thiếu.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
        }
    }
}
