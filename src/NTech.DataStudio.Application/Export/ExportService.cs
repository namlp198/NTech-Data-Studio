using System.Collections.Concurrent;
using System.IO.Compression;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Export;

/// <summary>The checks found errors, so nothing was written.</summary>
public sealed class ExportBlockedException : Exception
{
    public ExportBlockedException(ValidationReport report)
        : base($"Dataset có {report.ErrorCount} lỗi, chưa xuất được.")
    {
        Report = report;
    }

    public ValidationReport Report { get; }
}

/// <summary>
/// Module 4: checks a version against a profile and writes the dataset. Every picture is rebuilt from its
/// master, the version's config and seed (decision 0007); labels are read as they are now.
/// The folder is written beside its final name and renamed at the end, so a stopped or failed
/// export leaves nothing that looks finished.
/// </summary>
public sealed class ExportService
{
    private const string MarkerFile = "version.json";

    private readonly ProjectService _projects;
    private readonly IAugmentationEngine _engine;
    private readonly IReadOnlyList<IDatasetExporter> _exporters;

    public ExportService(ProjectService projects, IAugmentationEngine engine, IEnumerable<IDatasetExporter> exporters)
    {
        _projects = projects;
        _engine = engine;
        _exporters = exporters.ToList();
    }

    public bool Supports(ModelProfile profile) =>
        ExportKinds.For(profile) is { } kind && _exporters.Any(exporter => exporter.Kind == kind);

    /// <summary>The folder an export of this version and profile goes to by default.</summary>
    public string DefaultFolder(DatasetVersion version, ModelProfile profile, string? parent = null)
    {
        var root = _projects.OpenDatasetStore().Use(store => store.ProjectRoot);
        var kind = ExportKinds.For(profile);
        return Path.Combine(parent ?? Path.Combine(root, "exports"), $"{ExportNames.Slug(version.Name)}_{kind?.Id() ?? "none"}");
    }

    /// <exception cref="InvalidOperationException">The version is gone.</exception>
    public ValidationReport Validate(long versionId, ModelProfile profile, int imgsz)
    {
        using var store = _projects.OpenDatasetStore();
        var data = Load(store, versionId);
        return ValidateLoaded(store.ProjectRoot, data, profile, imgsz);
    }

    /// <exception cref="ExportBlockedException">The checks found errors.</exception>
    /// <exception cref="InvalidOperationException">The folder exists and may not be replaced, or the profile cannot be exported.</exception>
    /// <exception cref="OperationCanceledException">Stopped by the user; nothing is left behind.</exception>
    public ExportResult Export(ExportRequest request, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var kind = ExportKinds.For(request.Profile)
            ?? throw new InvalidOperationException($"Profile {request.Profile.Id} không có định dạng xuất.");
        var exporter = _exporters.FirstOrDefault(candidate => candidate.Kind == kind)
            ?? throw new InvalidOperationException($"Chưa có bộ xuất cho {kind.Id()}.");

        using var store = _projects.OpenDatasetStore();
        progress?.Report(new ExportProgress(ExportPhase.Checking, 0, 0));
        var data = Load(store, request.VersionId);
        var report = ValidateLoaded(store.ProjectRoot, data, request.Profile, request.Imgsz);
        if (report.HasErrors)
            throw new ExportBlockedException(report);

        var final = DefaultFolder(data.Version, request.Profile, request.OutputFolder);
        var zipPath = final + ".zip";
        PrepareTarget(final, zipPath, request);

        var staging = final + ".partial";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        var committed = false;
        try
        {
            var classes = BuildClasses(data);
            var jobs = BuildJobs(data, kind, classes);
            var entries = RenderAll(store.ProjectRoot, data, kind, exporter, classes, jobs, staging, request, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ExportProgress(ExportPhase.WritingLabels, 0, entries.Count));
            var output = new ExportOutput(staging, classes, entries);
            exporter.WriteLabels(output);

            var skipped = CountSkipped(kind, entries);
            var imagesPerSplit = new int[3];
            var labelsPerSplit = new int[3];
            foreach (var entry in entries)
            {
                imagesPerSplit[(int)entry.Split]++;
                labelsPerSplit[(int)entry.Split] += kind.IsClassify() ? 1 : entry.Labels.Count;
            }

            ExportDocuments.Write(staging, data.Version, data.Config, request, kind, classes, entries, report, skipped, labelsPerSplit);

            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(staging, final);
            committed = true;

            string? zip = null;
            if (request.Zip)
            {
                progress?.Report(new ExportProgress(ExportPhase.Packing, 0, 0));
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(final, zipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
                zip = zipPath;
            }

            return new ExportResult(final, zip, imagesPerSplit, labelsPerSplit, skipped, report);
        }
        finally
        {
            if (!committed && Directory.Exists(staging))
            {
                try { Directory.Delete(staging, true); }
                catch (IOException) { /* a file still held open; the next export clears the leftover */ }
            }
        }
    }

    private static void PrepareTarget(string final, string zipPath, ExportRequest request)
    {
        if (Directory.Exists(final))
        {
            if (!request.Overwrite)
                throw new InvalidOperationException($"Thư mục {final} đã tồn tại. Chọn ghi đè hoặc xóa thư mục đó.");
            if (!File.Exists(Path.Combine(final, MarkerFile)))
                throw new InvalidOperationException($"Thư mục {final} không phải bản xuất của app (thiếu {MarkerFile}), nên không ghi đè.");
        }

        if (request.Zip && File.Exists(zipPath) && !request.Overwrite)
            throw new InvalidOperationException($"File {zipPath} đã tồn tại. Chọn ghi đè hoặc xóa file đó.");
    }

    // ---- loading and checking ----

    private sealed record Loaded(
        DatasetVersion Version,
        DatasetConfig Config,
        IReadOnlyList<DatasetImage> Images,
        IReadOnlyDictionary<long, SplitKind> Split,
        IReadOnlyDictionary<long, IReadOnlyList<Annotation>> Annotations,
        IReadOnlyList<Domain.Classes.LabelClass> Classes);

    private static Loaded Load(IDatasetStore store, long versionId)
    {
        var version = store.LoadVersions().FirstOrDefault(candidate => candidate.Id == versionId)
            ?? throw new InvalidOperationException("Version không còn trong project.");
        var config = DatasetService.ConfigOf(version);
        var split = store.LoadAssignments(versionId).ToDictionary(item => item.ImageId, item => item.Split);

        // Images that left the project (rejected after the version was made) have no master to export.
        var byId = store.LoadImages().ToDictionary(image => image.Id);
        var images = split.Keys.Order().Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        var annotations = new Dictionary<long, IReadOnlyList<Annotation>>(images.Count);
        foreach (var image in images)
            annotations[image.Id] = store.LoadAnnotations(image.Id);

        return new Loaded(version, config, images, split, annotations, store.LoadClasses());
    }

    private static ValidationReport ValidateLoaded(string root, Loaded data, ModelProfile profile, int imgsz) =>
        DatasetValidator.Validate(new ValidationInput(
            data.Images,
            data.Split,
            imageId => data.Annotations.TryGetValue(imageId, out var list) ? list : Array.Empty<Annotation>(),
            data.Classes,
            data.Config,
            profile,
            imgsz,
            master => File.Exists(Path.Combine(root, master))));

    // ---- building the picture list ----

    private sealed record Job(DatasetImage Image, SplitKind Split, int Copy, string Stem, ExportClass? ClassifyClass);

    /// <summary>Classes that survive the class map, in class order.</summary>
    private static IReadOnlyList<ExportClass> BuildClasses(Loaded data)
    {
        var mapper = new ClassMapper(data.Config.Preprocess.ClassMap);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classes = new List<ExportClass>();
        foreach (var item in data.Classes.Where(item => mapper.Resolve(item.Id) == item.Id).OrderBy(item => item.Order).ThenBy(item => item.Id))
            classes.Add(new ExportClass(classes.Count, item.Id, item.Name, ExportNames.Unique(ExportNames.Slug(item.Name), taken)));
        return classes;
    }

    private static List<Job> BuildJobs(Loaded data, ExportKind kind, IReadOnlyList<ExportClass> classes)
    {
        var mapper = new ClassMapper(data.Config.Preprocess.ClassMap);
        var byClass = classes.ToDictionary(item => item.ClassId);
        var copies = data.Config.Augment.IsEnabled ? data.Config.Augment.Multiplier : 1;
        var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jobs = new List<Job>();
        foreach (var image in data.Images)
        {
            var split = data.Split[image.Id];
            ExportClass? tag = null;
            if (kind.IsClassify())
            {
                var mapped = image.TagClassIds.Select(mapper.Resolve).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
                tag = byClass[mapped.Single()];
            }

            var baseName = ExportNames.Slug(Path.GetFileNameWithoutExtension(image.MasterPath));
            var count = split == SplitKind.Train ? copies : 1;
            for (var copy = 0; copy < count; copy++)
            {
                var stem = ExportNames.Unique(copy == 0 ? baseName : $"{baseName}_a{copy}", stems);
                jobs.Add(new Job(image, split, copy, stem, tag));
            }
        }

        return jobs;
    }

    private IReadOnlyList<ExportEntry> RenderAll(
        string root,
        Loaded data,
        ExportKind kind,
        IDatasetExporter exporter,
        IReadOnlyList<ExportClass> classes,
        List<Job> jobs,
        string staging,
        ExportRequest request,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var mapper = new ClassMapper(data.Config.Preprocess.ClassMap);
        var options = new LabelTransformOptions
        {
            ClassMap = mapper.HasChanges ? mapper.Resolve : null,
            MinVisiblePercent = data.Config.Augment.MinVisiblePercent,
            CutoutDropPercent = data.Config.Augment.CutoutDropLabelPercent
        };

        var widest = data.Images.Max(image => (long)image.Width * image.Height);
        var side = (int)Math.Sqrt(widest);
        var threads = ParallelismPolicy.ThreadsForCurrentMachine(ParallelismPolicy.EstimateBytesPerImage(side, side, 3, 8, 0));

        var entries = new ConcurrentBag<ExportEntry>();
        var done = 0;
        progress?.Report(new ExportProgress(ExportPhase.Rendering, 0, jobs.Count));

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = cancellationToken };
        try
        {
            Parallel.ForEach(jobs, parallel, job =>
        {
            var image = job.Image;
            var augment = AugmentSampler.Sample(data.Config.Augment, data.Config.Seed, image.Id, job.Copy);
            var plan = RenderPlanner.Plan(image.Width, image.Height, data.Config.Preprocess, augment);
            var masterPath = Path.Combine(root, image.MasterPath);

            var (extension, width, height, bytes) = Picture(masterPath, plan, image, request);
            var labels = kind.IsClassify()
                ? Array.Empty<RenderedLabel>()
                : LabelTransformer.Transform(data.Annotations[image.Id], plan, options);
            var entry = new ExportEntry(image.Id, job.Copy, job.Split, job.Stem, extension, width, height, labels, job.ClassifyClass);

            var destination = Path.Combine(staging, exporter.ImagePath(entry).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (bytes is null) File.Copy(masterPath, destination, true);
            else File.WriteAllBytes(destination, bytes);

            entries.Add(entry);
            var finished = Interlocked.Increment(ref done);
            progress?.Report(new ExportProgress(ExportPhase.Rendering, finished, jobs.Count));
            });
        }
        catch (AggregateException exception) when (exception.InnerExceptions.Count > 0)
        {
            // Parallel.ForEach wraps what a worker threw; callers expect the original (an unreadable master, say).
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerExceptions[0]).Throw();
        }

        return entries.OrderBy(entry => entry.Split).ThenBy(entry => entry.Stem, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The master itself when nothing changes it (no re-encoding, no loss), otherwise the rendered picture.
    /// Null bytes mean "copy the master".
    /// </summary>
    private (string Extension, int Width, int Height, byte[]? Bytes) Picture(
        string masterPath, RenderPlan plan, DatasetImage image, ExportRequest request)
    {
        var extension = Path.GetExtension(masterPath).ToLowerInvariant();
        if (plan.IsIdentity && extension is ".jpg" or ".png")
            return (extension, image.Width, image.Height, null);

        var format = request.ImageFormat == ExportImageFormat.Png ? RenderFormat.Png : RenderFormat.Jpeg;
        var rendered = _engine.Render(masterPath, plan, format, request.JpegQuality);
        return (format == RenderFormat.Png ? ".png" : ".jpg", rendered.Width, rendered.Height, rendered.Data);
    }

    /// <summary>Labels the layout cannot hold (points, and shapes that collapse), which the exporter leaves out.</summary>
    public static int CountSkipped(ExportKind kind, IEnumerable<ExportEntry> entries)
    {
        if (kind.IsClassify()) return 0;

        var skipped = 0;
        foreach (var entry in entries)
        {
            foreach (var label in entry.Labels)
            {
                var writable = kind switch
                {
                    ExportKind.YoloObb => LabelShapes.Oriented(label, entry.Width, entry.Height) is not null,
                    ExportKind.YoloSegment or ExportKind.CocoSegment => LabelShapes.Polygon(label, entry.Width, entry.Height) is not null,
                    _ => LabelShapes.Box(label, entry.Width, entry.Height) is not null
                };
                if (!writable) skipped++;
            }
        }

        return skipped;
    }
}

internal static class StoreExtensions
{
    /// <summary>Runs <paramref name="read"/> on a store and disposes it.</summary>
    public static T Use<T>(this IDatasetStore store, Func<IDatasetStore, T> read)
    {
        using (store) return read(store);
    }
}
