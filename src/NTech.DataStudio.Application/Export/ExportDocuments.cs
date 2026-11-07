using System.Globalization;
using System.Text;
using System.Text.Json;
using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Export;

/// <summary>The two files every export carries: <c>version.json</c> to rebuild it and <c>README.dataset.md</c> to read it.</summary>
internal static class ExportDocuments
{
    private static readonly string[] SplitNames = { "train", "val", "test" };

    public static void Write(
        string folder,
        DatasetVersion version,
        DatasetConfig config,
        ExportRequest request,
        ExportKind kind,
        IReadOnlyList<ExportClass> classes,
        IReadOnlyList<ExportEntry> entries,
        ValidationReport report,
        int skippedLabels,
        int[] labelsPerSplit)
    {
        var perClass = CountPerClass(kind, classes, entries);
        File.WriteAllText(Path.Combine(folder, "version.json"), VersionJson(version, request, kind, classes, entries, perClass, labelsPerSplit, skippedLabels), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "README.dataset.md"), Readme(version, config, request, kind, classes, entries, perClass, report, skippedLabels), new UTF8Encoding(false));
    }

    /// <summary>Labels per class and split (images, for classification).</summary>
    private static Dictionary<long, int[]> CountPerClass(ExportKind kind, IReadOnlyList<ExportClass> classes, IReadOnlyList<ExportEntry> entries)
    {
        var counts = classes.ToDictionary(item => item.ClassId, _ => new int[3]);
        foreach (var entry in entries)
        {
            if (kind.IsClassify())
            {
                if (entry.ClassifyClass is { } tag) counts[tag.ClassId][(int)entry.Split]++;
                continue;
            }

            foreach (var label in entry.Labels)
            {
                if (counts.TryGetValue(label.ClassId, out var byClass))
                    byClass[(int)entry.Split]++;
            }
        }

        return counts;
    }

    private static string VersionJson(
        DatasetVersion version,
        ExportRequest request,
        ExportKind kind,
        IReadOnlyList<ExportClass> classes,
        IReadOnlyList<ExportEntry> entries,
        Dictionary<long, int[]> perClass,
        int[] labelsPerSplit,
        int skippedLabels)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", kind.Id());
            writer.WriteString("profile", request.Profile.Id);
            writer.WriteNumber("imgsz", request.Imgsz);
            writer.WriteString("imageFormat", request.ImageFormat.ToString().ToLowerInvariant());
            if (request.ImageFormat == ExportImageFormat.Jpeg)
                writer.WriteNumber("jpegQuality", request.JpegQuality);

            writer.WriteStartObject("version");
            writer.WriteNumber("id", version.Id);
            writer.WriteString("name", version.Name);
            writer.WriteNumber("seed", version.Seed);
            writer.WriteString("createdAt", version.CreatedAt);
            writer.WriteEndObject();

            // The three configs exactly as saved, so the export can be rebuilt from this file.
            writer.WritePropertyName("prep");
            JsonDocument.Parse(version.PrepJson).RootElement.WriteTo(writer);
            writer.WritePropertyName("split");
            JsonDocument.Parse(version.SplitJson).RootElement.WriteTo(writer);
            writer.WritePropertyName("augment");
            JsonDocument.Parse(version.AugmentJson).RootElement.WriteTo(writer);

            writer.WriteStartArray("classes");
            foreach (var item in classes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", item.Index);
                writer.WriteNumber("id", item.ClassId);
                writer.WriteString("name", item.Name);
                var counts = perClass[item.ClassId];
                for (var split = 0; split < 3; split++)
                    writer.WriteNumber(SplitNames[split], counts[split]);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartObject("counts");
            for (var split = 0; split < 3; split++)
            {
                writer.WriteStartObject(SplitNames[split]);
                writer.WriteNumber("images", entries.Count(entry => (int)entry.Split == split));
                writer.WriteNumber("labels", labelsPerSplit[split]);
                writer.WriteEndObject();
            }

            writer.WriteNumber("skippedLabels", skippedLabels);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Readme(
        DatasetVersion version,
        DatasetConfig config,
        ExportRequest request,
        ExportKind kind,
        IReadOnlyList<ExportClass> classes,
        IReadOnlyList<ExportEntry> entries,
        Dictionary<long, int[]> perClass,
        ValidationReport report,
        int skippedLabels)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Dataset {version.Name}");
        text.AppendLine();
        text.AppendLine($"Xuất từ NTech Data Studio, profile `{request.Profile.Id}`, định dạng `{kind.Id()}`.");
        text.AppendLine($"Version `{version.Name}`, seed {version.Seed}. Cấu hình đầy đủ nằm trong `version.json`.");
        text.AppendLine();

        text.AppendLine("## Số lượng");
        text.AppendLine();
        var unit = kind.IsClassify() ? "ảnh" : "nhãn";
        text.AppendLine($"| Tập | Ảnh | {Capitalize(unit)} |");
        text.AppendLine("| --- | ---: | ---: |");
        for (var split = 0; split < 3; split++)
        {
            var images = entries.Count(entry => (int)entry.Split == split);
            if (images == 0 && split == (int)SplitKind.Test) continue;
            var labels = perClass.Values.Sum(counts => counts[split]);
            text.AppendLine($"| {SplitNames[split]} | {images} | {labels} |");
        }

        text.AppendLine();
        text.AppendLine($"| Class | train | val | test |");
        text.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var item in classes)
        {
            var counts = perClass[item.ClassId];
            text.AppendLine($"| {item.Index}: {item.Name} | {counts[0]} | {counts[1]} | {counts[2]} |");
        }

        if (skippedLabels > 0)
        {
            text.AppendLine();
            text.AppendLine($"{skippedLabels} nhãn không ghi được vào định dạng này (point, hoặc hình bị cắt đến mức không còn diện tích) nên đã bị bỏ.");
        }

        text.AppendLine();
        text.AppendLine("## Preprocessing");
        text.AppendLine();
        foreach (var line in DescribePreprocess(config.Preprocess, classes))
            text.AppendLine($"- {line}");

        text.AppendLine();
        text.AppendLine("## Tăng cường (chỉ tập train)");
        text.AppendLine();
        foreach (var line in DescribeAugment(config.Augment))
            text.AppendLine($"- {line}");

        var warnings = report.Issues.Where(issue => issue.Severity == IssueSeverity.Warning).ToList();
        if (warnings.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Cảnh báo lúc xuất");
            text.AppendLine();
            foreach (var issue in warnings)
                text.AppendLine($"- {issue.Message}");
        }

        text.AppendLine();
        text.AppendLine("## Train thử");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine(TrainCommand(kind, request));
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine(kind is ExportKind.CocoDetect or ExportKind.CocoSegment
            ? "Lệnh trên chỉ là mẫu: đối chiếu tên lớp và tham số với phiên bản `rfdetr` bạn dùng."
            : "Chạy lệnh từ bên trong thư mục này. Đổi `yolo26s` thành cỡ model bạn muốn.");
        return text.ToString();
    }

    public static string TrainCommand(ExportKind kind, ExportRequest request) => kind switch
    {
        ExportKind.YoloDetect => $"yolo detect train model=yolo26s.pt data=data.yaml imgsz={request.Imgsz}",
        ExportKind.YoloSegment => $"yolo segment train model=yolo26s-seg.pt data=data.yaml imgsz={request.Imgsz}",
        ExportKind.YoloObb => $"yolo obb train model=yolo26s-obb.pt data=data.yaml imgsz={request.Imgsz}",
        ExportKind.YoloClassify => $"yolo classify train model=yolo26s-cls.pt data=. imgsz={request.Imgsz}",
        ExportKind.CocoSegment => "model = RFDETRSegPreview(); model.train(dataset_dir=\".\", epochs=50, batch_size=4)",
        _ => "model = RFDETRBase(); model.train(dataset_dir=\".\", epochs=50, batch_size=4)"
    };

    private static IEnumerable<string> DescribePreprocess(PreprocessConfig config, IReadOnlyList<ExportClass> classes)
    {
        yield return config.Resize switch
        {
            ResizeMode.None => "Giữ nguyên kích thước master.",
            ResizeMode.Stretch => $"Stretch về {config.Width}×{config.Height}.",
            ResizeMode.Fit => $"Fit (letterbox, viền xám) về {config.Width}×{config.Height}.",
            _ => $"Fill (phủ kín, cắt phần thừa) về {config.Width}×{config.Height}."
        };
        if (config.Grayscale) yield return "Chuyển ảnh xám.";
        if (config.ClassMap.Count > 0) yield return $"{config.ClassMap.Count} class bị gộp hoặc bỏ; xem `version.json`.";
        if (config.DropNullPercent > 0) yield return $"Bỏ {config.DropNullPercent}% ảnh không có nhãn.";
        yield return $"{classes.Count} class trong dataset.";
    }

    private static IEnumerable<string> DescribeAugment(AugmentConfig config)
    {
        if (!config.IsEnabled)
        {
            yield return "Không tăng cường: mỗi ảnh train xuất một lần.";
            yield break;
        }

        yield return $"Nhân ×{config.Multiplier} (gồm ảnh gốc).";
        var geometry = new List<string>();
        if (config.FlipHorizontal) geometry.Add("lật ngang");
        if (config.FlipVertical) geometry.Add("lật dọc");
        if (config.Rotate90Clockwise || config.Rotate90CounterClockwise || config.Rotate180) geometry.Add("xoay 90°");
        if (config.RotateDegrees > 0) geometry.Add($"xoay ±{Num(config.RotateDegrees)}°");
        if (config.ScalePercent > 0) geometry.Add($"scale ±{Num(config.ScalePercent)}%");
        if (config.TranslatePercent > 0) geometry.Add($"dịch ±{Num(config.TranslatePercent)}%");
        if (config.ShearDegrees > 0) geometry.Add($"shear ±{Num(config.ShearDegrees)}°");
        if (geometry.Count > 0) yield return "Hình học: " + string.Join(", ", geometry) + ".";

        var color = new List<string>();
        if (config.BrightnessPercent > 0) color.Add($"độ sáng ±{Num(config.BrightnessPercent)}%");
        if (config.ExposurePercent > 0) color.Add($"phơi sáng ±{Num(config.ExposurePercent)}%");
        if (config.ContrastPercent > 0) color.Add($"tương phản ±{Num(config.ContrastPercent)}%");
        if (config.GammaPercent > 0) color.Add($"gamma ±{Num(config.GammaPercent)}%");
        if (config.HueDegrees > 0) color.Add($"hue ±{Num(config.HueDegrees)}°");
        if (config.SaturationPercent > 0) color.Add($"bão hòa ±{Num(config.SaturationPercent)}%");
        if (config.GrayscalePercent > 0) color.Add($"ảnh xám {config.GrayscalePercent}%");
        if (color.Count > 0) yield return "Ánh sáng, màu: " + string.Join(", ", color) + ".";

        var noise = new List<string>();
        if (config.BlurPx > 0) noise.Add($"blur đến {Num(config.BlurPx)} px");
        if (config.MotionBlurPx > 0) noise.Add($"motion blur đến {Num(config.MotionBlurPx)} px");
        if (config.NoisePercent > 0) noise.Add($"nhiễu đến {Num(config.NoisePercent)}%");
        if (config.JpegQualityMin < 100) noise.Add($"nén JPEG xuống chất lượng {config.JpegQualityMin}");
        if (config.CutoutCount > 0) noise.Add($"{config.CutoutCount} ô cutout {config.CutoutSizePercent}%");
        if (noise.Count > 0) yield return "Nhiễu, che khuất: " + string.Join(", ", noise) + ".";
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
