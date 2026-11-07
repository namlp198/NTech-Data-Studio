using System.Text;
using System.Text.Json;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Exporters.Coco;

/// <summary>
/// COCO json in the Roboflow folder layout RF-DETR reads: <c>train/</c>, <c>valid/</c>, <c>test/</c>, each with
/// its pictures and an <c>_annotations.coco.json</c>. Boxes are <c>[x, y, w, h]</c> in pixels; category ids start at 1.
/// </summary>
public sealed class CocoExporter : IDatasetExporter
{
    public const string AnnotationFile = "_annotations.coco.json";

    private readonly bool _segmentation;

    public CocoExporter(bool segmentation)
    {
        _segmentation = segmentation;
    }

    public ExportKind Kind => _segmentation ? ExportKind.CocoSegment : ExportKind.CocoDetect;

    public string ImagePath(ExportEntry entry) =>
        $"{ExportText.SplitFolder(entry.Split, true)}/{entry.Stem}{entry.Extension}";

    public void WriteLabels(ExportOutput output)
    {
        var byClass = ExportText.ByClassId(output.Classes);
        foreach (var group in output.Entries.GroupBy(entry => entry.Split))
        {
            var folder = ExportText.SplitFolder(group.Key, true);
            ExportText.WriteFile(output.Root, $"{folder}/{AnnotationFile}", Document(group.ToList(), output.Classes, byClass));
        }
    }

    private string Document(IReadOnlyList<ExportEntry> entries, IReadOnlyList<ExportClass> classes, Dictionary<long, ExportClass> byClass)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("info");
            writer.WriteString("description", "NTech Data Studio");
            writer.WriteEndObject();

            writer.WriteStartArray("categories");
            foreach (var item in classes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", item.Index + 1);
                writer.WriteString("name", item.Name);
                writer.WriteString("supercategory", "none");
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("images");
            for (var index = 0; index < entries.Count; index++)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", index + 1);
                writer.WriteString("file_name", entries[index].Stem + entries[index].Extension);
                writer.WriteNumber("width", entries[index].Width);
                writer.WriteNumber("height", entries[index].Height);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("annotations");
            var annotationId = 0;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                foreach (var label in entry.Labels)
                {
                    if (!byClass.TryGetValue(label.ClassId, out var item)) continue;
                    if (LabelShapes.Box(label, entry.Width, entry.Height) is not { } box) continue;

                    IReadOnlyList<PixelPoint>? polygon = null;
                    if (_segmentation)
                    {
                        polygon = LabelShapes.Polygon(label, entry.Width, entry.Height);
                        if (polygon is null) continue;
                    }

                    writer.WriteStartObject();
                    writer.WriteNumber("id", ++annotationId);
                    writer.WriteNumber("image_id", index + 1);
                    writer.WriteNumber("category_id", item.Index + 1);
                    writer.WritePropertyName("bbox");
                    writer.WriteStartArray();
                    foreach (var value in new[] { box.X, box.Y, box.Width, box.Height })
                        writer.WriteNumberValue(Round(value));
                    writer.WriteEndArray();
                    writer.WriteNumber("area", Round(polygon is null ? box.Area : PolygonMath.Area(polygon)));
                    writer.WriteNumber("iscrowd", 0);

                    writer.WritePropertyName("segmentation");
                    writer.WriteStartArray();
                    if (polygon is not null)
                    {
                        writer.WriteStartArray();
                        foreach (var point in polygon)
                        {
                            writer.WriteNumberValue(Round(point.X));
                            writer.WriteNumberValue(Round(point.Y));
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Thousandths of a pixel: far below anything a label can mean, and keeps the file small.</summary>
    private static double Round(double value) => Math.Round(value, 3);
}
