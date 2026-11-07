using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Exporters.Tests;

/// <summary>A scratch folder and hand-built entries, so exporters are tested without a project or any pixels.</summary>
internal sealed class ExporterFixture : IDisposable
{
    public static readonly ExportClass Red = new(0, 1, "red", "red");
    public static readonly ExportClass Blue = new(1, 2, "Đầu nối: xanh #2", "Dau_noi_xanh_2");

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ntech-exporters-");

    public string Root => _folder.FullName;

    public IReadOnlyList<ExportClass> Classes { get; } = new[] { Red, Blue };

    public static RenderedLabel Label(ExportClass item, ShapeKind kind, params (double X, double Y)[] points) =>
        new(Guid.NewGuid(), item.ClassId, kind, points.Select(point => new PixelPoint(point.X, point.Y)).ToList());

    public static RenderedLabel BoxLabel(ExportClass item, double left, double top, double right, double bottom) =>
        Label(item, ShapeKind.Box, (left, top), (right, top), (right, bottom), (left, bottom));

    public static ExportEntry Entry(
        string stem,
        SplitKind split,
        IReadOnlyList<RenderedLabel> labels,
        int width = 200,
        int height = 100,
        ExportClass? classify = null) =>
        new(1, 0, split, stem, ".jpg", width, height, labels, classify);

    public string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public bool Exists(string relativePath) =>
        File.Exists(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)))
        || Directory.Exists(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void Dispose()
    {
        try { _folder.Delete(true); }
        catch (IOException) { /* temp folder; the OS clears it eventually */ }
    }
}
