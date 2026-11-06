namespace NTech.DataStudio.Domain.Profiles;

public enum TaskKind
{
    Detect,
    Segment,
    Obb,
    Classify,
    Anomaly
}

/// <summary>A target model's data requirements, loaded from profiles/*.yaml.</summary>
public sealed record ModelProfile(
    string Id,
    string Family,
    TaskKind Task,
    int ImgszDefault,
    IReadOnlyList<int> ImgszOptions,
    int Stride,
    int MinObjectPx,
    IReadOnlyList<string> ExportFormats);
