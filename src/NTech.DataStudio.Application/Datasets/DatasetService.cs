using System.Text.Json;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Application.Datasets;

/// <summary>One cell of the preview grid: a real output of the version, with its labels in output pixels.</summary>
public sealed record PreviewTile(
    DatasetImage Image,
    SplitKind? Split,
    int CopyIndex,
    RenderedImage? Picture,
    IReadOnlyList<RenderedLabel> Labels,
    string? Error);

/// <summary>Loads what a version is built from, builds and deletes versions, and renders preview cells.</summary>
public sealed class DatasetService
{
    public const int PreviewEdge = 640;

    private readonly ProjectService _projects;
    private readonly IAugmentationEngine _engine;

    public DatasetService(ProjectService projects, IAugmentationEngine engine)
    {
        _projects = projects;
        _engine = engine;
    }

    /// <exception cref="ProjectException">No project is open.</exception>
    public DatasetWorkspace Load()
    {
        using var store = _projects.OpenDatasetStore();
        return new DatasetWorkspace(store.ProjectRoot, store.LoadImages(), store.LoadClasses(), store.LoadVersions());
    }

    /// <exception cref="ArgumentException">The config is invalid.</exception>
    /// <exception cref="InvalidOperationException">The name is empty or taken, or an image has gone.</exception>
    public DatasetVersion CreateVersion(string name, DatasetConfig config, VersionPlan plan)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException("Tên version không được để trống.");
        if (plan.Assignments.Count == 0)
            throw new InvalidOperationException("Version không có ảnh nào. Hãy gán nhãn trước.");
        if (config.Validate() is { } error)
            throw new ArgumentException(error, nameof(config));

        using var store = _projects.OpenDatasetStore();
        return store.CreateVersion(trimmed, config, plan.Assignments);
    }

    public void DeleteVersion(long versionId)
    {
        using var store = _projects.OpenDatasetStore();
        store.DeleteVersion(versionId);
    }

    /// <summary>The split a saved version froze, for showing its numbers again.</summary>
    public IReadOnlyList<SplitAssignment> LoadAssignments(long versionId)
    {
        using var store = _projects.OpenDatasetStore();
        return store.LoadAssignments(versionId);
    }

    /// <exception cref="JsonException">The version was written by something this app cannot read.</exception>
    public static DatasetConfig ConfigOf(DatasetVersion version) =>
        DatasetConfig.FromJson(version.PrepJson, version.SplitJson, version.AugmentJson, version.Seed);

    /// <summary>
    /// Renders up to <paramref name="count"/> cells. Each is what the export of this config
    /// would write for that image and copy, so a label that is off here is off in the dataset.
    /// <paramref name="shuffle"/> picks other images and other copies.
    /// </summary>
    public IReadOnlyList<PreviewTile> RenderPreview(
        DatasetWorkspace workspace,
        DatasetConfig config,
        VersionPlan plan,
        int count,
        int shuffle,
        CancellationToken cancellationToken = default)
    {
        var splitOf = plan.Assignments.ToDictionary(assignment => assignment.ImageId, assignment => assignment.Split);
        var byId = workspace.Images.ToDictionary(image => image.Id);

        // Augmentation only touches train, so that is where its preview belongs.
        var pool = plan.Assignments
            .Where(assignment => assignment.Split == SplitKind.Train)
            .Select(assignment => byId[assignment.ImageId])
            .ToList();
        if (pool.Count == 0)
            pool = plan.Assignments.Select(assignment => byId[assignment.ImageId]).ToList();
        if (pool.Count == 0)
            return Array.Empty<PreviewTile>();

        var ordered = pool
            .OrderBy(image => DeterministicRandom.From(config.Seed, shuffle, image.Id).NextUInt64())
            .ThenBy(image => image.Id)
            .ToList();

        var copies = Math.Max(1, config.Augment.Multiplier - 1);
        var mapper = new ClassMapper(config.Preprocess.ClassMap);
        var options = new LabelTransformOptions
        {
            ClassMap = mapper.HasChanges ? mapper.Resolve : null,
            MinVisiblePercent = config.Augment.MinVisiblePercent,
            CutoutDropPercent = config.Augment.CutoutDropLabelPercent
        };

        using var store = _projects.OpenDatasetStore();
        var tiles = new List<PreviewTile>(count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var image = ordered[index % ordered.Count];
            var round = index / ordered.Count;
            var copyIndex = config.Augment.IsEnabled ? 1 + (shuffle + round) % copies : 0;
            var split = splitOf.TryGetValue(image.Id, out var kind) ? kind : (SplitKind?)null;

            try
            {
                var augment = AugmentSampler.Sample(config.Augment, config.Seed, image.Id, copyIndex);
                var renderPlan = RenderPlanner.Plan(image.Width, image.Height, config.Preprocess, augment).ScaledTo(PreviewEdge);
                var picture = _engine.Render(Path.Combine(workspace.ProjectRoot, image.MasterPath), renderPlan, RenderFormat.Jpeg, 90);
                var labels = LabelTransformer.Transform(store.LoadAnnotations(image.Id), renderPlan, options);
                tiles.Add(new PreviewTile(image, split, copyIndex, picture, labels, null));
            }
            catch (ImageReadException exception)
            {
                tiles.Add(new PreviewTile(image, split, copyIndex, null, Array.Empty<RenderedLabel>(), exception.Message));
            }
        }

        return tiles;
    }
}
