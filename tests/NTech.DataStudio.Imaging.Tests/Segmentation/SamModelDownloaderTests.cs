using System.Net;
using System.Security.Cryptography;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Segmentation;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

/// <summary>Decision 0016: the one download of the model files, against a fake server.</summary>
public class SamModelDownloaderTests : IDisposable
{
    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int Requests { get; private set; }
        public Func<CancellationToken, Task>? Gate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Gate is not null) await Gate(cancellationToken);
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[request.RequestUri!.AbsolutePath]) };
        }
    }

    private readonly TempFolder _temp = new();
    private readonly FakeServer _server = new();
    private readonly byte[] _first = Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251)).ToArray();
    private readonly byte[] _second = Enumerable.Range(0, 120_000).Select(index => (byte)(index % 199)).ToArray();

    public void Dispose() => _temp.Dispose();

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private SamModelDownloader Downloader(string? wrongHash = null)
    {
        _server.Files["/a.onnx"] = _first;
        _server.Files["/b.onnx"] = _second;
        return new SamModelDownloader(new HttpClient(_server), new[]
        {
            new SamModelDownloader.Source("a.onnx", "https://example.test/a.onnx", Sha(_first)),
            new SamModelDownloader.Source("b.onnx", "https://example.test/b.onnx", wrongHash ?? Sha(_second))
        });
    }

    private string Folder => Path.Combine(_temp.Path, "models");

    [Fact]
    public async Task Download_WritesBothFilesAndReportsProgressOverAllOfThem()
    {
        var reports = new List<ModelDownloadProgress>();

        await Downloader().DownloadAsync(Folder, new Progress<ModelDownloadProgress>(reports.Add), CancellationToken.None);
        await Task.Delay(100); // Progress<T> posts to the thread pool

        Assert.Equal(_first, await File.ReadAllBytesAsync(Path.Combine(Folder, "a.onnx")));
        Assert.Equal(_second, await File.ReadAllBytesAsync(Path.Combine(Folder, "b.onnx")));
        Assert.Empty(Directory.GetFiles(Folder, "*.partial"));
        Assert.Contains(reports, report => report.FileIndex == 1 && report.Received == _second.Length);
        Assert.All(reports, report => Assert.InRange(report.Fraction, 0, 1));
    }

    [Fact]
    public async Task Download_SkipsAFileThatIsAlreadyThereAndRight()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Combine(Folder, "a.onnx"), _first);

        await Downloader().DownloadAsync(Folder, new Progress<ModelDownloadProgress>(), CancellationToken.None);

        Assert.Equal(1, _server.Requests);
    }

    [Fact]
    public async Task Download_ReplacesAFileThatIsThereButWrong()
    {
        Directory.CreateDirectory(Folder);
        await File.WriteAllBytesAsync(Path.Combine(Folder, "a.onnx"), new byte[] { 1, 2, 3 });

        await Downloader().DownloadAsync(Folder, new Progress<ModelDownloadProgress>(), CancellationToken.None);

        Assert.Equal(_first, await File.ReadAllBytesAsync(Path.Combine(Folder, "a.onnx")));
    }

    [Fact]
    public async Task AFileWithTheWrongChecksum_IsRefusedAndNeverKeptUnderItsName()
    {
        var downloader = Downloader(wrongHash: new string('0', 64));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync(Folder, new Progress<ModelDownloadProgress>(), CancellationToken.None));

        Assert.Contains("b.onnx", exception.Message);
        Assert.Contains("SHA-256", exception.Message);
        Assert.False(File.Exists(Path.Combine(Folder, "b.onnx")));
        Assert.Empty(Directory.GetFiles(Folder, "*.partial"));
    }

    [Fact]
    public async Task AServerError_SaysWhichFileAndTheCode()
    {
        var downloader = Downloader();
        _server.Status = HttpStatusCode.NotFound;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            downloader.DownloadAsync(Folder, new Progress<ModelDownloadProgress>(), CancellationToken.None));

        Assert.Contains("a.onnx", exception.Message);
        Assert.Contains("404", exception.Message);
    }

    [Fact]
    public async Task Cancelling_LeavesNothingBehind()
    {
        var downloader = Downloader();
        using var cancel = new CancellationTokenSource();
        _server.Gate = async token =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            downloader.DownloadAsync(Folder, new Progress<ModelDownloadProgress>(), cancel.Token));

        Assert.Empty(Directory.GetFiles(Folder));
    }

    // ---- the service the Settings screen talks to ----

    private sealed class FakeSegmenter : ISmartSegmenter
    {
        public bool Ready { get; set; }
        public string? Unavailable => Ready ? null : "missing";
        public Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PixelPoint>?> SegmentAsync(string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PixelPoint>?>(null);
    }

    private sealed class FakeDownloader : IModelDownloader
    {
        public FakeSegmenter Segmenter { get; init; } = null!;
        public Exception? Failure { get; set; }
        public TaskCompletionSource Release { get; } = new();

        public async Task DownloadAsync(string folder, IProgress<ModelDownloadProgress> progress, CancellationToken cancellationToken)
        {
            progress.Report(new ModelDownloadProgress(0, 2, "a.onnx", 10, 100));
            await Release.Task.WaitAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            Segmenter.Ready = true;
        }
    }

    [Fact]
    public async Task Service_GoesFromMissingToDownloadingToReady()
    {
        var segmenter = new FakeSegmenter();
        var downloader = new FakeDownloader { Segmenter = segmenter };
        var service = new SamModelService(segmenter, downloader, "folder");
        var changes = 0;
        service.Changed += (_, _) => changes++;
        Assert.False(service.IsReady);

        var task = service.DownloadAsync();
        Assert.True(service.IsDownloading);
        await service.DownloadAsync(); // a second press while one runs does nothing
        downloader.Release.SetResult();
        await task;

        Assert.False(service.IsDownloading);
        Assert.True(service.IsReady);
        Assert.Null(service.Error);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task Service_AFailureBecomesAnErrorTheScreenShowsAndTheNextTryStartsClean()
    {
        var segmenter = new FakeSegmenter();
        var downloader = new FakeDownloader { Segmenter = segmenter, Failure = new InvalidOperationException("mất mạng") };
        var service = new SamModelService(segmenter, downloader, "folder");
        downloader.Release.SetResult();

        await service.DownloadAsync();

        Assert.Equal("mất mạng", service.Error);
        Assert.False(service.IsReady);
        Assert.False(service.IsDownloading);

        downloader.Failure = null;
        await service.DownloadAsync();
        Assert.Null(service.Error);
        Assert.True(service.IsReady);
    }

    [Fact]
    public async Task Service_CancelStopsTheDownloadAndSaysSo()
    {
        var segmenter = new FakeSegmenter();
        var downloader = new FakeDownloader { Segmenter = segmenter };
        var service = new SamModelService(segmenter, downloader, "folder");

        var task = service.DownloadAsync();
        service.Cancel();
        await task;

        Assert.Equal("Đã hủy tải model.", service.Error);
        Assert.False(service.IsDownloading);
        Assert.False(service.IsReady);
    }
}
