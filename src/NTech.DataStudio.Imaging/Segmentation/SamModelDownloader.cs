using System.Security.Cryptography;
using NTech.DataStudio.Application.Annotate;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>
/// Downloads the two MobileSAM files from Hugging Face (Acly/MobileSAM, MIT) into the model folder. Each file is written
/// beside its final name, checked against the SHA-256 of the file that was tested with the app, and only then renamed, so
/// a cut connection or a changed file upstream never leaves something the segmenter would try to load.
/// </summary>
public sealed class SamModelDownloader : IModelDownloader
{
    /// <summary>One file to fetch and the SHA-256 it must have.</summary>
    public sealed record Source(string FileName, string Url, string Sha256);

    public static IReadOnlyList<Source> DefaultSources { get; } = new Source[]
    {
        new(SamSegmenter.EncoderFile,
            "https://huggingface.co/Acly/MobileSAM/resolve/main/mobile_sam_image_encoder.onnx",
            "580f5fb648ea1062c0aabc26217aed56921985f03f0cbbd852bba81d760cc749"),
        new(SamSegmenter.DecoderFile,
            "https://huggingface.co/Acly/MobileSAM/resolve/main/sam_mask_decoder_single.onnx",
            "93915fc7c993ab9d59ab8c9ccd3bce37f7509c81ab4150a74abd4d2abbd8570d")
    };

    private readonly HttpClient _http;
    private readonly IReadOnlyList<Source> Files;

    public SamModelDownloader(HttpClient? http = null, IReadOnlyList<Source>? sources = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        Files = sources ?? DefaultSources;
    }

    public async Task DownloadAsync(string folder, IProgress<ModelDownloadProgress> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        for (var index = 0; index < Files.Count; index++)
        {
            var source = Files[index];
            var target = Path.Combine(folder, source.FileName);
            if (File.Exists(target) && await HashOfAsync(target, cancellationToken) == source.Sha256) continue;

            await DownloadOneAsync(source, target, index, progress, cancellationToken);
        }

        progress.Report(new ModelDownloadProgress(Files.Count, Files.Count, string.Empty, 0, null));
    }

    private async Task DownloadOneAsync(Source source, string target, int index, IProgress<ModelDownloadProgress> progress, CancellationToken cancellationToken)
    {
        var partial = target + ".partial";
        try
        {
            using var response = await _http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Không tải được {source.FileName}: máy chủ trả về {(int)response.StatusCode}.");

            var total = response.Content.Headers.ContentLength;
            using var hash = SHA256.Create();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    received += read;
                    progress.Report(new ModelDownloadProgress(index, Files.Count, source.FileName, received, total));
                }

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            }

            var actual = Convert.ToHexString(hash.Hash!).ToLowerInvariant();
            if (actual != source.Sha256)
                throw new InvalidOperationException(
                    $"{source.FileName} tải về không khớp bản đã kiểm (SHA-256 khác). File có thể đã bị đổi ở nguồn; app không dùng nó.");

            File.Move(partial, target, overwrite: true);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"Không tải được {source.FileName}: {exception.Message}", exception);
        }
        finally
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (IOException) { /* still open; the next download overwrites it */ }
        }
    }

    private static async Task<string> HashOfAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
