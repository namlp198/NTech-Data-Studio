using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Export;

/// <summary>Which layout a dataset is written in. Each maps to one <see cref="Abstractions.IDatasetExporter"/>.</summary>
public enum ExportKind
{
    YoloDetect,
    YoloSegment,
    YoloObb,
    YoloClassify,
    CocoDetect,
    CocoSegment
}

public static class ExportKinds
{
    /// <summary>The layout a profile asks for, or null when it has none (anomaly profiles train on good images only).</summary>
    public static ExportKind? For(ModelProfile profile)
    {
        var format = profile.ExportFormats.FirstOrDefault()?.Trim().ToLowerInvariant();
        return (format, profile.Task) switch
        {
            ("yolo", TaskKind.Detect) => ExportKind.YoloDetect,
            ("yolo", TaskKind.Segment) => ExportKind.YoloSegment,
            ("yolo", TaskKind.Obb) => ExportKind.YoloObb,
            ("yolo", TaskKind.Classify) => ExportKind.YoloClassify,
            ("coco", TaskKind.Detect) => ExportKind.CocoDetect,
            ("coco", TaskKind.Segment) => ExportKind.CocoSegment,
            _ => null
        };
    }

    public static bool IsClassify(this ExportKind kind) => kind == ExportKind.YoloClassify;

    public static bool NeedsPolygons(this ExportKind kind) => kind is ExportKind.YoloSegment or ExportKind.CocoSegment;

    public static string Id(this ExportKind kind) => kind switch
    {
        ExportKind.YoloDetect => "yolo26_detect",
        ExportKind.YoloSegment => "yolo26_segment",
        ExportKind.YoloObb => "yolo26_obb",
        ExportKind.YoloClassify => "yolo26_classify",
        ExportKind.CocoDetect => "coco_detect",
        ExportKind.CocoSegment => "coco_segment",
        _ => kind.ToString()
    };
}

/// <summary>A class of the exported dataset. <see cref="Index"/> is dense from 0, in class order.</summary>
public sealed record ExportClass(int Index, long ClassId, string Name, string FolderName);

/// <summary>One picture of the dataset, with the labels as they sit on it.</summary>
/// <param name="Stem">File name without extension; unique in the dataset.</param>
/// <param name="Labels">In output pixels, after preprocessing and augmentation.</param>
/// <param name="ClassifyClass">The tag the picture belongs to; only for classification layouts.</param>
public sealed record ExportEntry(
    long ImageId,
    int CopyIndex,
    SplitKind Split,
    string Stem,
    string Extension,
    int Width,
    int Height,
    IReadOnlyList<RenderedLabel> Labels,
    ExportClass? ClassifyClass);

public sealed record ExportOutput(string Root, IReadOnlyList<ExportClass> Classes, IReadOnlyList<ExportEntry> Entries);

public enum ExportImageFormat
{
    Jpeg,
    Png
}

/// <param name="Imgsz">The size the training will use; only checked against object sizes and written into the sample command.</param>
/// <param name="TargetTasks">The target tasks of the project (decisions 0014 and 0019); the service fills them in and they go into version.json.</param>
/// <param name="OutputFolder">Parent folder of the dataset folder; the project's <c>exports</c> folder when null.</param>
public sealed record ExportRequest(
    long VersionId,
    ModelProfile Profile,
    int Imgsz,
    ExportImageFormat ImageFormat = ExportImageFormat.Jpeg,
    int JpegQuality = 95,
    bool Zip = false,
    bool Overwrite = false,
    string? OutputFolder = null,
    IReadOnlyList<TaskKind>? TargetTasks = null);

public enum ExportPhase
{
    Checking,
    Rendering,
    WritingLabels,
    Packing
}

public sealed record ExportProgress(ExportPhase Phase, int Done, int Total);

public sealed record ExportResult(
    string Folder,
    string? ZipPath,
    int[] ImagesPerSplit,
    int[] LabelsPerSplit,
    int SkippedLabels,
    ValidationReport Report);
