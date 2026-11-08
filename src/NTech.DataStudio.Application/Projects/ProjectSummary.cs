using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Projects;

/// <param name="ImageCount">Images not rejected.</param>
/// <param name="TargetTask">What the project is for (decision 0014); null in a project that has not chosen yet.</param>
/// <param name="AnnotatedImageCount">Images finished with labeling: Labeled, Reviewed or Null.</param>
public sealed record ProjectSummary(
    string Name,
    string RootPath,
    int ImageCount,
    int AnnotatedImageCount,
    int ClassCount,
    int VersionCount,
    TaskKind? TargetTask = null);
