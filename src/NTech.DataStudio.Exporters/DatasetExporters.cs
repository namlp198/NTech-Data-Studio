using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Exporters.Coco;
using NTech.DataStudio.Exporters.Yolo;

namespace NTech.DataStudio.Exporters;

public static class DatasetExporters
{
    /// <summary>One exporter per <see cref="Application.Export.ExportKind"/>.</summary>
    public static IReadOnlyList<IDatasetExporter> All() => new IDatasetExporter[]
    {
        new YoloDetectExporter(),
        new YoloSegmentExporter(),
        new YoloObbExporter(),
        new YoloClassifyExporter(),
        new CocoExporter(segmentation: false),
        new CocoExporter(segmentation: true)
    };
}
