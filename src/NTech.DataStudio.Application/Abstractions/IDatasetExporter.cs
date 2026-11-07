using NTech.DataStudio.Application.Export;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>
/// Writes one dataset layout. The service renders the pictures and puts each where
/// <see cref="ImagePath"/> says; the exporter then writes labels and the files that
/// describe the dataset (data.yaml, annotation json).
/// </summary>
public interface IDatasetExporter
{
    ExportKind Kind { get; }

    /// <summary>Where the picture goes, relative to the dataset folder, with '/' as separator.</summary>
    string ImagePath(ExportEntry entry);

    void WriteLabels(ExportOutput output);
}
