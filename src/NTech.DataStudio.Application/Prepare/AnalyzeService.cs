using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Prepare;

public sealed record AnalyzeResult(int Analyzed, int AlreadyAnalyzed, int Corrupt);

/// <summary>Decodes each not yet analyzed image once. Results are saved image by image, so a re-run resumes.</summary>
public sealed class AnalyzeService
{
    private readonly IImageCodec _codec;

    public AnalyzeService(IImageCodec codec)
    {
        _codec = codec;
    }

    public static string ThumbnailFileName(long sourceId) => $"source_{sourceId:D6}.jpg";

    public AnalyzeResult Analyze(
        IPrepareStore store,
        IProgress<PrepareProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sources = store.LoadSources();
        var pending = sources.Where(source => !source.IsAnalyzed && source.Reject != RejectReason.Corrupt).ToList();
        var skipped = sources.Count(source => source.IsAnalyzed);
        if (pending.Count == 0) return new AnalyzeResult(0, skipped, 0);

        Directory.CreateDirectory(store.ThumbsFolder);
        var perImage = pending.Max(source => ParallelismPolicy.EstimateBytesPerImage(
            source.Width, source.Height, source.Channels, source.BitDepth, source.FileSize));
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = ParallelismPolicy.ThreadsForCurrentMachine(perImage),
            CancellationToken = cancellationToken
        };

        var done = 0;
        var corrupt = 0;
        Parallel.ForEach(pending, options, source =>
        {
            try
            {
                var analysis = _codec.Analyze(
                    Path.Combine(store.ProjectRoot, source.StoredPath),
                    Path.Combine(store.ThumbsFolder, ThumbnailFileName(source.Id)));
                store.SaveAnalysis(source.Id, analysis);
            }
            catch (ImageReadException exception)
            {
                store.MarkCorrupt(source.Id, exception.Message);
                Interlocked.Increment(ref corrupt);
            }

            var finished = Interlocked.Increment(ref done);
            progress?.Report(new PrepareProgress(finished, pending.Count, source.FileName));
        });

        return new AnalyzeResult(done - corrupt, skipped, corrupt);
    }
}
