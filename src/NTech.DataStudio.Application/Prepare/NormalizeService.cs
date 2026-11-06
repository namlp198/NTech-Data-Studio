using System.Text.Json;
using System.Text.Json.Serialization;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Prepare;

public enum ConfigChange
{
    /// <summary>No masters yet, or the same settings as before: only missing masters get made.</summary>
    None,

    /// <summary>Different settings and no labels: existing masters would be regenerated.</summary>
    MastersWillBeRebuilt,

    /// <summary>Different settings but labels exist on the masters, so they must stay.</summary>
    BlockedByAnnotations
}

public sealed record NormalizeResult(
    int Processed,
    int AlreadyDone,
    int MasterCount,
    long SourceBytes,
    long MasterBytes,
    IReadOnlyList<ImageFailure> Failures);

public sealed class NormalizeService
{
    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IImageCodec _codec;

    public NormalizeService(IImageCodec codec)
    {
        _codec = codec;
    }

    public static ConfigChange CheckConfigChange(IPrepareStore store, NormalizeOptions options)
    {
        var previous = store.ReadNormalizeConfig();
        if (previous is null || previous == Serialize(options) || store.CountMasters() == 0)
            return ConfigChange.None;

        return store.CountAnnotations() > 0 ? ConfigChange.BlockedByAnnotations : ConfigChange.MastersWillBeRebuilt;
    }

    /// <exception cref="ProjectException">The settings changed but labels already sit on the current masters.</exception>
    public NormalizeResult Normalize(
        IPrepareStore store,
        NormalizeOptions options,
        IProgress<PrepareProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        switch (CheckConfigChange(store, options))
        {
            case ConfigChange.BlockedByAnnotations:
                throw new ProjectException("Project đã có nhãn trên ảnh master hiện tại, nên không đổi được cấu hình chuẩn hóa. Hãy dùng lại cấu hình cũ.");
            case ConfigChange.MastersWillBeRebuilt:
                DeleteMasters(store);
                break;
        }

        store.WriteNormalizeConfig(Serialize(options));
        Directory.CreateDirectory(store.MasterFolder);
        Directory.CreateDirectory(store.ThumbsFolder);

        var active = store.LoadSources().Where(source => !source.IsRejected).ToList();
        var done = store.SourcesWithMasters();
        var pending = active.Where(source => !done.Contains(source.Id)).ToList();

        var failures = new List<ImageFailure>();
        if (pending.Count > 0)
        {
            var perImage = pending.Max(source => ParallelismPolicy.EstimateBytesPerImage(
                source.Width, source.Height, source.Channels, source.BitDepth, source.FileSize));
            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = ParallelismPolicy.ThreadsForCurrentMachine(perImage),
                CancellationToken = cancellationToken
            };

            var finished = 0;
            Parallel.ForEach(pending, parallel, source =>
            {
                try
                {
                    var masters = _codec.Normalize(
                        Path.Combine(store.ProjectRoot, source.StoredPath),
                        options,
                        store.MasterFolder,
                        store.ThumbsFolder,
                        FileStem(source));
                    store.AddMasters(source.Id, masters);
                }
                catch (ImageReadException exception)
                {
                    store.MarkCorrupt(source.Id, exception.Message);
                    lock (failures) failures.Add(new ImageFailure(source.FileName, exception.Message));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    lock (failures) failures.Add(new ImageFailure(source.FileName, exception.Message));
                }

                var count = Interlocked.Increment(ref finished);
                progress?.Report(new PrepareProgress(count, pending.Count, source.FileName));
            });
        }

        var withMasters = store.SourcesWithMasters();
        return new NormalizeResult(
            pending.Count - failures.Count,
            active.Count - pending.Count,
            store.CountMasters(),
            active.Where(source => withMasters.Contains(source.Id)).Sum(source => source.FileSize),
            store.SumMasterBytes(),
            failures);
    }

    public static string Serialize(NormalizeOptions options) => JsonSerializer.Serialize(options, ConfigJson);

    private static string FileStem(SourceImage source) =>
        $"{source.Id:D6}_{Path.GetFileNameWithoutExtension(source.OriginalPath)}";

    private static void DeleteMasters(IPrepareStore store)
    {
        foreach (var relative in store.ClearMasters())
            File.Delete(Path.Combine(store.ProjectRoot, relative));
    }
}
