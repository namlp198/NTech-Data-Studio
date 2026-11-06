namespace NTech.DataStudio.Application.Projects;

/// <param name="ImageCount">Images not rejected.</param>
/// <param name="AnnotatedImageCount">Images finished with labeling: Labeled, Reviewed or Null.</param>
public sealed record ProjectSummary(
    string Name,
    string RootPath,
    int ImageCount,
    int AnnotatedImageCount,
    int ClassCount,
    int VersionCount);
