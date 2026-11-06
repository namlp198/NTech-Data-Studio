using System.Collections.Concurrent;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Prepare;

public enum ImportMode
{
    CopyIntoProject,
    ReferenceInPlace
}

public sealed record ImportResult(int Added, int AlreadyImported, int Corrupt, IReadOnlyList<ImageFailure> Failures);

/// <summary>Scans a folder and registers its images. Reads headers only; pixels are decoded later by analysis.</summary>
public sealed class ImportService
{
    private const int HeaderThreads = 4;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".tif", ".tiff", ".png", ".jpg", ".jpeg", ".webp"
    };

    private readonly IImageCodec _codec;

    public ImportService(IImageCodec codec)
    {
        _codec = codec;
    }

    public ImportResult Import(
        IPrepareStore store,
        string folder,
        ImportMode mode,
        IProgress<PrepareProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(folder);
        var known = store.LoadSources()
            .Select(source => Key(source.OriginalPath, source.FileSize, source.ModifiedTicks))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !IsInside(path, store.ProjectRoot))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .ToList();

        var fresh = files.Where(file => !known.Contains(Key(file.FullName, file.Length, file.LastWriteTimeUtc.Ticks))).ToList();

        var headers = ReadHeaders(fresh, cancellationToken);

        // Sequential: ids then follow the sorted order, and copying large files one by one is kinder to the disk.
        var added = 0;
        var corrupt = 0;
        var failures = new List<ImageFailure>();
        for (var index = 0; index < fresh.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = fresh[index];
            progress?.Report(new PrepareProgress(index, fresh.Count, file.Name));

            try
            {
                var (header, error) = headers[index];
                var stored = header is not null && mode == ImportMode.CopyIntoProject
                    ? CopyIn(file, store, cancellationToken)
                    : file.FullName;

                var source = new SourceImage(
                    0,
                    file.FullName,
                    stored,
                    !Path.IsPathRooted(stored),
                    file.Length,
                    file.LastWriteTimeUtc.Ticks,
                    header?.Format ?? "?",
                    header?.Width ?? 0,
                    header?.Height ?? 0,
                    header?.BitDepth ?? 0,
                    header?.Channels ?? 0,
                    GroupKeyFor(root, file.FullName));

                var id = store.AddSource(source);
                added++;
                if (error is not null)
                {
                    store.MarkCorrupt(id, error);
                    corrupt++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new ImageFailure(file.Name, exception.Message));
            }
        }

        progress?.Report(new PrepareProgress(fresh.Count, fresh.Count));
        return new ImportResult(added, files.Count - fresh.Count, corrupt, failures);
    }

    private (ImageHeader? Header, string? Error)[] ReadHeaders(IReadOnlyList<FileInfo> files, CancellationToken cancellationToken)
    {
        var results = new (ImageHeader?, string?)[files.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = HeaderThreads, CancellationToken = cancellationToken };
        Parallel.For(0, files.Count, options, index =>
        {
            try
            {
                results[index] = (_codec.ReadHeader(files[index].FullName), null);
            }
            catch (ImageReadException exception)
            {
                results[index] = (null, exception.Message);
            }
        });
        return results;
    }

    /// <summary>The unique prefix keeps same-named files from different folders apart.</summary>
    private static string CopyIn(FileInfo file, IPrepareStore store, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(store.SourceFolder);
        var name = $"{Guid.NewGuid():N}"[..8] + "_" + file.Name;
        var target = Path.Combine(store.SourceFolder, name);
        var partial = target + ".partial";
        File.Copy(file.FullName, partial);
        File.Move(partial, target);
        return Path.GetRelativePath(store.ProjectRoot, target);
    }

    /// <summary>Images from one subfolder usually come from one session, so they must stay in one split.</summary>
    private static string GroupKeyFor(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path) ?? root);
        return relative == "." ? Path.GetFileName(root) : relative.Replace('\\', '/');
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Key(string path, long size, long ticks) => $"{path}|{size}|{ticks}";
}
