using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;

namespace NTech.DataStudio.Application.Annotate;

/// <param name="Annotations">Polygon labels with <see cref="AnnotationSource.Model"/>, ready for <see cref="AnnotateService.Execute"/>.</param>
/// <param name="SkippedClasses">Class names the model found that the project does not have; those objects are left out.</param>
public sealed record AutoLabelResult(IReadOnlyList<Annotation> Annotations, IReadOnlyList<string> SkippedClasses);

/// <summary>Turns what a model found into labels of the project's classes. Classes are matched by name, never guessed.</summary>
public sealed class AutoLabelService
{
    public const double DefaultMinScore = 0.4;

    private readonly IAutoLabeler _labeler;
    private readonly Func<DateTimeOffset> _clock;

    public AutoLabelService(IAutoLabeler labeler, Func<DateTimeOffset>? clock = null)
    {
        _labeler = labeler;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Null when the model can label this project; otherwise what is wrong, in words for the user.</summary>
    public string? Check(IReadOnlyList<LabelClass> classes)
    {
        if (_labeler.Unavailable is { } unavailable) return unavailable;

        var known = classes.Select(item => Normalize(item.Name)).ToHashSet();
        if (!_labeler.ClassNames.Any(name => known.Contains(Normalize(name))))
        {
            return "Không class nào của model trùng tên với class của project. "
                   + $"Model có: {string.Join(", ", _labeler.ClassNames)}. Project có: {string.Join(", ", classes.Select(item => item.Name))}.";
        }

        return null;
    }

    public async Task<AutoLabelResult> LabelAsync(
        string imagePath, long imageId, IReadOnlyList<LabelClass> classes, double minScore, CancellationToken cancellationToken = default)
    {
        if (Check(classes) is { } problem)
            throw new InvalidOperationException(problem);

        var byName = new Dictionary<string, LabelClass>();
        foreach (var item in classes) byName.TryAdd(Normalize(item.Name), item);

        var found = await _labeler.DetectAsync(imagePath, minScore, cancellationToken);
        var now = _clock();
        var annotations = new List<Annotation>();
        var skipped = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in found.OrderByDescending(label => label.Score))
        {
            if (!byName.TryGetValue(Normalize(label.ClassName), out var target))
            {
                skipped.Add(label.ClassName);
                continue;
            }

            if (label.Outline.Count < 3) continue;
            annotations.Add(new Annotation(
                Guid.NewGuid(), imageId, target.Id, ShapeKind.Polygon, label.Outline, AnnotationSource.Model, now, now));
        }

        return new AutoLabelResult(annotations, skipped.ToList());
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();
}
