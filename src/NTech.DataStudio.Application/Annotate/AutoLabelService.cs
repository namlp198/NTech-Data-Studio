using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Annotate;

/// <param name="Annotations">Polygon labels with <see cref="AnnotationSource.Model"/>, ready for <see cref="AnnotateService.Execute"/>.</param>
/// <param name="SkippedClasses">Class names the model found that the project does not have; those objects are left out.</param>
/// <param name="BoxOutlineCount">How many of <paramref name="Annotations"/> are only a detected box because SAM could not cut the object out.</param>
public sealed record AutoLabelResult(IReadOnlyList<Annotation> Annotations, IReadOnlyList<string> SkippedClasses, int BoxOutlineCount = 0);

/// <summary>Turns what a model found into labels of the project's classes. Classes are matched by name, never guessed.</summary>
public sealed class AutoLabelService
{
    public const double DefaultMinScore = 0.4;

    private readonly Func<TaskKind, IAutoLabeler> _labelers;
    private readonly Func<DateTimeOffset> _clock;

    public AutoLabelService(IAutoLabeler labeler, Func<DateTimeOffset>? clock = null)
        : this(_ => labeler, clock)
    {
    }

    /// <param name="labelers">The labeler that does each task (decision 0019).</param>
    public AutoLabelService(Func<TaskKind, IAutoLabeler> labelers, Func<DateTimeOffset>? clock = null)
    {
        _labelers = labelers;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The tasks a project can label automatically: Detect and Segment among its tasks. A project that aims at neither (or at none yet)
    /// is offered both, since the user knows best what the labels are for.
    /// </summary>
    public static IReadOnlyList<TaskKind> TasksFor(IReadOnlyList<TaskKind> projectTasks)
    {
        var own = new[] { TaskKind.Detect, TaskKind.Segment }.Where(projectTasks.Contains).ToList();
        return own.Count > 0 ? own : new[] { TaskKind.Detect, TaskKind.Segment };
    }

    /// <summary>The shape the automatic labels of a task have.</summary>
    public static ShapeKind ShapeOf(TaskKind task) => task == TaskKind.Detect ? ShapeKind.Box : ShapeKind.Polygon;

    /// <summary>Null when the model can label this project; otherwise what is wrong, in words for the user.</summary>
    public string? Check(IReadOnlyList<LabelClass> classes, TaskKind task = TaskKind.Segment)
    {
        var labeler = _labelers(task);
        if (labeler.Unavailable is { } unavailable) return unavailable;

        var known = classes.Select(item => Normalize(item.Name)).ToHashSet();
        if (!labeler.ClassNames.Any(name => known.Contains(Normalize(name))))
        {
            return "Không class nào của model trùng tên với class của project. "
                   + $"Model có: {string.Join(", ", labeler.ClassNames)}. Project có: {string.Join(", ", classes.Select(item => item.Name))}.";
        }

        return null;
    }

    public Task<AutoLabelResult> LabelAsync(
        string imagePath, long imageId, IReadOnlyList<LabelClass> classes, double minScore, CancellationToken cancellationToken = default) =>
        LabelAsync(imagePath, imageId, classes, minScore, TaskKind.Segment, cancellationToken);

    public async Task<AutoLabelResult> LabelAsync(
        string imagePath, long imageId, IReadOnlyList<LabelClass> classes, double minScore, TaskKind task, CancellationToken cancellationToken = default)
    {
        if (Check(classes, task) is { } problem)
            throw new InvalidOperationException(problem);
        var labeler = _labelers(task);

        var byName = new Dictionary<string, LabelClass>();
        foreach (var item in classes) byName.TryAdd(Normalize(item.Name), item);

        var found = await labeler.DetectAsync(imagePath, minScore, cancellationToken);
        var now = _clock();
        var annotations = new List<Annotation>();
        var skipped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var boxOutlines = 0;
        foreach (var label in found.OrderByDescending(label => label.Score))
        {
            if (!byName.TryGetValue(Normalize(label.ClassName), out var target))
            {
                skipped.Add(label.ClassName);
                continue;
            }

            if (label.Outline.Count < 3) continue;
            if (label.IsBoxOutline) boxOutlines++;
            annotations.Add(new Annotation(
                Guid.NewGuid(), imageId, target.Id, label.Shape, label.Outline, AnnotationSource.Model, now, now));
        }

        return new AutoLabelResult(annotations, skipped.ToList(), boxOutlines);
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
