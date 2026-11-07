using System.Globalization;
using System.Text;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Exporters.Yolo;

/// <summary>
/// The Ultralytics layout: <c>images/{split}/x.jpg</c> next to <c>labels/{split}/x.txt</c> and a data.yaml.
/// Coordinates are divided by the picture size, so they fall in 0..1.
/// </summary>
public abstract class YoloLabelExporter : IDatasetExporter
{
    public abstract ExportKind Kind { get; }

    public string ImagePath(ExportEntry entry) =>
        $"images/{ExportText.SplitFolder(entry.Split, false)}/{entry.Stem}{entry.Extension}";

    public void WriteLabels(ExportOutput output)
    {
        var byClass = ExportText.ByClassId(output.Classes);
        foreach (var entry in output.Entries)
        {
            var text = new StringBuilder();
            foreach (var label in entry.Labels)
            {
                if (!byClass.TryGetValue(label.ClassId, out var item)) continue;
                if (Line(entry, label, item.Index) is { } line)
                    text.Append(line).Append('\n');
            }

            // An empty file is how Ultralytics is told a picture holds no object.
            ExportText.WriteFile(output.Root, $"labels/{ExportText.SplitFolder(entry.Split, false)}/{entry.Stem}.txt", text.ToString());
        }

        ExportText.WriteFile(output.Root, "data.yaml", DataYaml(output));
    }

    /// <summary>One label line, or null when the label cannot be written in this layout.</summary>
    protected abstract string? Line(ExportEntry entry, RenderedLabel label, int classIndex);

    protected static string Format(int classIndex, IEnumerable<double> values) =>
        string.Join(' ', values.Select(ExportText.Number).Prepend(classIndex.ToString(CultureInfo.InvariantCulture)));

    protected static IEnumerable<double> Normalized(IEnumerable<PixelPoint> points, ExportEntry entry)
    {
        foreach (var point in points)
        {
            yield return Math.Clamp(point.X / entry.Width, 0, 1);
            yield return Math.Clamp(point.Y / entry.Height, 0, 1);
        }
    }

    private static string DataYaml(ExportOutput output)
    {
        var has = output.Entries.Select(entry => entry.Split).Distinct().ToHashSet();
        var yaml = new StringBuilder();
        yaml.AppendLine("# NTech Data Studio. Relative paths resolve from the folder this file is in.");
        yaml.AppendLine("train: images/train");
        if (has.Contains(SplitKind.Val)) yaml.AppendLine("val: images/val");
        if (has.Contains(SplitKind.Test)) yaml.AppendLine("test: images/test");
        yaml.AppendLine("names:");
        foreach (var item in output.Classes)
            yaml.AppendLine($"  {item.Index}: {ExportText.YamlString(item.Name)}");
        return yaml.ToString();
    }
}

/// <summary><c>cls cx cy w h</c></summary>
public sealed class YoloDetectExporter : YoloLabelExporter
{
    public override ExportKind Kind => ExportKind.YoloDetect;

    protected override string? Line(ExportEntry entry, RenderedLabel label, int classIndex)
    {
        if (LabelShapes.Box(label, entry.Width, entry.Height) is not { } box) return null;
        return Format(classIndex, new[]
        {
            (box.X + box.Width / 2) / entry.Width,
            (box.Y + box.Height / 2) / entry.Height,
            box.Width / entry.Width,
            box.Height / entry.Height
        });
    }
}

/// <summary><c>cls x1 y1 x2 y2 ...</c></summary>
public sealed class YoloSegmentExporter : YoloLabelExporter
{
    public override ExportKind Kind => ExportKind.YoloSegment;

    protected override string? Line(ExportEntry entry, RenderedLabel label, int classIndex) =>
        LabelShapes.Polygon(label, entry.Width, entry.Height) is { } polygon
            ? Format(classIndex, Normalized(polygon, entry))
            : null;
}

/// <summary><c>cls x1 y1 x2 y2 x3 y3 x4 y4</c></summary>
public sealed class YoloObbExporter : YoloLabelExporter
{
    public override ExportKind Kind => ExportKind.YoloObb;

    protected override string? Line(ExportEntry entry, RenderedLabel label, int classIndex) =>
        LabelShapes.Oriented(label, entry.Width, entry.Height) is { } corners
            ? Format(classIndex, Normalized(corners, entry))
            : null;
}

/// <summary><c>{split}/{class}/picture.jpg</c>: the folder is the label, so there are no label files.</summary>
public sealed class YoloClassifyExporter : IDatasetExporter
{
    public ExportKind Kind => ExportKind.YoloClassify;

    public string ImagePath(ExportEntry entry)
    {
        var folder = entry.ClassifyClass?.FolderName
            ?? throw new InvalidOperationException("Ảnh phân loại phải có class.");
        return $"{ExportText.SplitFolder(entry.Split, false)}/{folder}/{entry.Stem}{entry.Extension}";
    }

    public void WriteLabels(ExportOutput output)
    {
        // A class with no picture in a split would be missing from that split's tree, and the
        // trainer would then count classes differently per split. Keep the empty folder.
        foreach (var split in output.Entries.Select(entry => entry.Split).Distinct())
        {
            foreach (var item in output.Classes)
                Directory.CreateDirectory(Path.Combine(output.Root, ExportText.SplitFolder(split, false), item.FolderName));
        }
    }
}
