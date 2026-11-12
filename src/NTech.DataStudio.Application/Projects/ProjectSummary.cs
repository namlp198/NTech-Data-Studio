using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Projects;

/// <param name="ImageCount">Images not rejected.</param>
/// <param name="TargetTaskList">What the project is for (decisions 0014 and 0019); null or empty in a project that has not chosen yet.</param>
/// <param name="AnnotatedImageCount">Images finished with labeling: Labeled, Reviewed or Null.</param>
public sealed record ProjectSummary(
    string Name,
    string RootPath,
    int ImageCount,
    int AnnotatedImageCount,
    int ClassCount,
    int VersionCount,
    IReadOnlyList<TaskKind>? TargetTaskList = null)
{
    /// <summary>The tasks the project aims at, in the order of <see cref="TargetTasks.All"/>; empty when it has not chosen (every drawing tool works).</summary>
    public IReadOnlyList<TaskKind> Tasks => TargetTaskList ?? Array.Empty<TaskKind>();

    /// <summary>Two summaries are equal when everything is, the tasks compared by value (a list has no value equality of its own).</summary>
    public bool Equals(ProjectSummary? other) =>
        other is not null && Name == other.Name && RootPath == other.RootPath && ImageCount == other.ImageCount
        && AnnotatedImageCount == other.AnnotatedImageCount && ClassCount == other.ClassCount && VersionCount == other.VersionCount
        && Tasks.SequenceEqual(other.Tasks);

    public override int GetHashCode() => HashCode.Combine(Name, RootPath, ImageCount, AnnotatedImageCount, ClassCount, VersionCount, Tasks.Count);
}
