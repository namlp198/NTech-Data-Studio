using NTech.DataStudio.Application.Abstractions;

namespace NTech.DataStudio.Application.Annotate;

/// <param name="Received">Bytes of the current file received so far.</param>
/// <param name="Total">Size of the current file when the server said it.</param>
public sealed record ModelDownloadProgress(int FileIndex, int FileCount, string FileName, long Received, long? Total)
{
    /// <summary>0 to 1 over all files, counting the current one by what it has received.</summary>
    public double Fraction => Total is > 0
        ? (FileIndex + Math.Clamp((double)Received / Total.Value, 0, 1)) / FileCount
        : (double)FileIndex / FileCount;
}

/// <summary>Fetches the model files into a folder (decision 0016). Must leave no half-written file under its final name.</summary>
public interface IModelDownloader
{
    /// <exception cref="InvalidOperationException">A download failed or a file did not match its checksum.</exception>
    /// <exception cref="OperationCanceledException">Stopped by the user; nothing is left behind.</exception>
    Task DownloadAsync(string folder, IProgress<ModelDownloadProgress> progress, CancellationToken cancellationToken);
}

/// <summary>
/// Whether the model files of the Magic polygon tool are there, and the one download of them the user can start
/// (decision 0016). The app never downloads by itself: the user presses a button on the Settings screen.
/// </summary>
public sealed class SamModelService
{
    private readonly ISmartSegmenter _segmenter;
    private readonly IModelDownloader _downloader;
    private CancellationTokenSource? _cancel;

    public SamModelService(ISmartSegmenter segmenter, IModelDownloader downloader, string folder)
    {
        _segmenter = segmenter;
        _downloader = downloader;
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>Raised when readiness, progress or the error changes; on the thread that started the download.</summary>
    public event EventHandler? Changed;

    public bool IsReady => _segmenter.Unavailable is null;

    /// <summary>Why the model is not usable (files missing), or null when it is.</summary>
    public string? Unavailable => _segmenter.Unavailable;

    public bool IsDownloading { get; private set; }

    public ModelDownloadProgress? Progress { get; private set; }

    /// <summary>Why the last download failed; cleared when a new one starts.</summary>
    public string? Error { get; private set; }

    public async Task DownloadAsync()
    {
        if (IsDownloading) return;

        _cancel = new CancellationTokenSource();
        IsDownloading = true;
        Error = null;
        Progress = null;
        Raise();
        try
        {
            await _downloader.DownloadAsync(Folder, new Progress<ModelDownloadProgress>(value =>
            {
                Progress = value;
                Raise();
            }), _cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Error = "Đã hủy tải model.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            Error = exception.Message;
        }
        finally
        {
            IsDownloading = false;
            Progress = null;
            _cancel.Dispose();
            _cancel = null;
            Raise();
        }
    }

    public void Cancel() => _cancel?.Cancel();

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
