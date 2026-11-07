using System.Globalization;
using System.Text.Json;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Application.Import;

/// <summary>A label read from a file written by another tool (or by this app), in pixels of its picture.</summary>
/// <param name="ClassKey">Index in a YOLO file, category id in a COCO file.</param>
public sealed record ParsedLabel(int ClassKey, ShapeKind Kind, IReadOnlyList<PixelPoint> Points);

public sealed class LabelFormatException : Exception
{
    public LabelFormatException(string message) : base(message)
    {
    }
}

public enum YoloLabelKind
{
    Detect,
    Segment,
    Obb
}

/// <summary>Reads Ultralytics label files and data.yaml class names.</summary>
public static class YoloLabelParser
{
    /// <exception cref="LabelFormatException">A line is not a label; the message gives its number.</exception>
    public static IReadOnlyList<ParsedLabel> Parse(string text, YoloLabelKind kind, int width, int height)
    {
        var labels = new List<ParsedLabel>();
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new double[parts.Length - 1];
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var classKey))
                throw new LabelFormatException($"Dòng {lineNumber}: class \"{parts[0]}\" không phải số nguyên.");
            for (var index = 1; index < parts.Length; index++)
            {
                if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index - 1])
                    || !double.IsFinite(numbers[index - 1]))
                    throw new LabelFormatException($"Dòng {lineNumber}: \"{parts[index]}\" không phải số.");
            }

            labels.Add(kind switch
            {
                YoloLabelKind.Detect => Detect(numbers, classKey, width, height, lineNumber),
                YoloLabelKind.Obb => Obb(numbers, classKey, width, height, lineNumber),
                _ => Segment(numbers, classKey, width, height, lineNumber)
            });
        }

        return labels;
    }

    private static ParsedLabel Detect(double[] values, int classKey, int width, int height, int line)
    {
        if (values.Length != 4)
            throw new LabelFormatException($"Dòng {line}: box cần 4 số, có {values.Length}.");

        var w = values[2] * width;
        var h = values[3] * height;
        var rect = new PixelRect(values[0] * width - w / 2, values[1] * height - h / 2, w, h);
        return new ParsedLabel(classKey, ShapeKind.Box, rect.Corners());
    }

    private static ParsedLabel Obb(double[] values, int classKey, int width, int height, int line)
    {
        if (values.Length != 8)
            throw new LabelFormatException($"Dòng {line}: oriented box cần 8 số, có {values.Length}.");
        return new ParsedLabel(classKey, ShapeKind.RotatedBox, Points(values, width, height));
    }

    private static ParsedLabel Segment(double[] values, int classKey, int width, int height, int line)
    {
        if (values.Length < 6 || values.Length % 2 != 0)
            throw new LabelFormatException($"Dòng {line}: polygon cần ít nhất 3 đỉnh (số chẵn các số), có {values.Length} số.");
        return new ParsedLabel(classKey, ShapeKind.Polygon, Points(values, width, height));
    }

    private static List<PixelPoint> Points(double[] values, int width, int height)
    {
        var points = new List<PixelPoint>(values.Length / 2);
        for (var index = 0; index < values.Length; index += 2)
            points.Add(new PixelPoint(values[index] * width, values[index + 1] * height));
        return points;
    }

    /// <summary>
    /// Class names by index from a data.yaml: the <c>names:</c> block as a map (<c>0: a</c>), a list (<c>- a</c>)
    /// or an inline list (<c>[a, b]</c>). Anything else in the file is ignored.
    /// </summary>
    public static IReadOnlyDictionary<int, string> ParseNames(string yaml)
    {
        var names = new SortedDictionary<int, string>();
        var lines = yaml.Replace("\r", string.Empty).Split('\n');
        for (var start = 0; start < lines.Length; start++)
        {
            var header = lines[start].TrimEnd();
            if (!header.StartsWith("names:", StringComparison.Ordinal)) continue;

            var inline = header["names:".Length..].Trim();
            if (inline.StartsWith('['))
            {
                var items = inline.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index < items.Length; index++)
                    names[index] = Unquote(items[index].Trim());
                return names;
            }

            var next = 0;
            for (var index = start + 1; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
                if (!char.IsWhiteSpace(line[0]) && !line.StartsWith('-')) break;

                var body = line.Trim();
                if (body.StartsWith('-'))
                {
                    names[next++] = Unquote(body[1..].Trim());
                    continue;
                }

                var colon = body.IndexOf(':');
                if (colon > 0 && int.TryParse(body[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var key))
                    names[key] = Unquote(body[(colon + 1)..].Trim());
            }

            return names;
        }

        return names;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            try { return JsonSerializer.Deserialize<string>(text) ?? string.Empty; }
            catch (JsonException) { return text[1..^1]; }
        }

        return text.Length >= 2 && text[0] == '\'' && text[^1] == '\'' ? text[1..^1].Replace("''", "'") : text;
    }
}

public sealed record CocoCategory(int Id, string Name);

public sealed record CocoImage(string FileName, int Width, int Height, IReadOnlyList<ParsedLabel> Labels);

public sealed record CocoDataset(IReadOnlyList<CocoCategory> Categories, IReadOnlyList<CocoImage> Images);

/// <summary>Reads a COCO annotation file. A label with a segmentation becomes a polygon, otherwise a box.</summary>
public static class CocoLabelParser
{
    /// <exception cref="LabelFormatException">The json is not a COCO file.</exception>
    public static CocoDataset Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var categories = root.GetProperty("categories").EnumerateArray()
                .Select(item => new CocoCategory(item.GetProperty("id").GetInt32(), item.GetProperty("name").GetString() ?? string.Empty))
                .ToList();

            var byImage = new Dictionary<int, List<ParsedLabel>>();
            foreach (var annotation in root.GetProperty("annotations").EnumerateArray())
            {
                var imageId = annotation.GetProperty("image_id").GetInt32();
                if (!byImage.TryGetValue(imageId, out var list))
                    byImage[imageId] = list = new List<ParsedLabel>();
                list.Add(ParseAnnotation(annotation));
            }

            var images = root.GetProperty("images").EnumerateArray()
                .Select(item =>
                {
                    var id = item.GetProperty("id").GetInt32();
                    return new CocoImage(
                        item.GetProperty("file_name").GetString() ?? string.Empty,
                        item.GetProperty("width").GetInt32(),
                        item.GetProperty("height").GetInt32(),
                        byImage.TryGetValue(id, out var labels) ? labels : new List<ParsedLabel>());
                })
                .ToList();

            return new CocoDataset(categories, images);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new LabelFormatException($"Không đọc được file COCO: {exception.Message}");
        }
    }

    private static ParsedLabel ParseAnnotation(JsonElement annotation)
    {
        var category = annotation.GetProperty("category_id").GetInt32();

        if (annotation.TryGetProperty("segmentation", out var segmentation)
            && segmentation.ValueKind == JsonValueKind.Array
            && segmentation.GetArrayLength() > 0
            && segmentation[0].ValueKind == JsonValueKind.Array)
        {
            var values = segmentation[0].EnumerateArray().Select(item => item.GetDouble()).ToList();
            if (values.Count >= 6 && values.Count % 2 == 0)
            {
                var points = new List<PixelPoint>();
                for (var index = 0; index < values.Count; index += 2)
                    points.Add(new PixelPoint(values[index], values[index + 1]));
                return new ParsedLabel(category, ShapeKind.Polygon, points);
            }
        }

        var box = annotation.GetProperty("bbox").EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (box.Length != 4)
            throw new LabelFormatException("bbox của COCO cần 4 số.");
        return new ParsedLabel(category, ShapeKind.Box, new PixelRect(box[0], box[1], box[2], box[3]).Corners());
    }
}
