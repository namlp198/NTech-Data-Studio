using NTech.DataStudio.Domain.Geometry;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>One object read from the model's output: class index, score and outline in master pixels.</summary>
internal sealed record SegDetection(int ClassId, double Score, IReadOnlyList<PixelPoint> Outline);

/// <summary>How a master picture sits inside the model's square input: scaled to fit, centered, padded (letterbox).</summary>
internal readonly record struct LetterboxFrame(int ImageWidth, int ImageHeight, int Size, double Scale, int NewWidth, int NewHeight, int Left, int Top)
{
    public static LetterboxFrame For(int width, int height, int size)
    {
        var scale = Math.Min((double)size / height, (double)size / width);
        var newWidth = (int)Math.Round(width * scale);
        var newHeight = (int)Math.Round(height * scale);
        return new LetterboxFrame(width, height, size, scale, newWidth, newHeight, (size - newWidth) / 2, (size - newHeight) / 2);
    }
}

/// <summary>
/// Reads the output of a Trainer segment ONNX as <c>model.meta.json</c> describes it (decisions 0012 and 0014 of the
/// Trainer): <c>(1, N, 6 + K)</c> rows of x1 y1 x2 y2 score class and K mask coefficients, with prototypes
/// <c>(1, K, h, w)</c> a quarter of the input. A mask is the coefficients times the prototypes, scaled up to the input,
/// cut to its own box, and kept where the logit is above 0.
/// </summary>
internal static class YoloSegDecoder
{
    private const int BoxColumns = 6;
    private const double MinContourArea = 4;

    /// <param name="rows">Row-major N x <paramref name="columns"/> detections; rows below the threshold or padded with zeros are skipped.</param>
    /// <param name="prototypes">Row-major K x <paramref name="protoHeight"/> x <paramref name="protoWidth"/>.</param>
    public static List<SegDetection> Decode(
        float[] rows, int rowCount, int columns, float[] prototypes, int protoHeight, int protoWidth, LetterboxFrame frame, double minScore)
    {
        var kept = columns - BoxColumns;
        if (kept <= 0) throw new ArgumentException("Output has no mask coefficients.", nameof(columns));
        var area = protoHeight * protoWidth;
        if (prototypes.Length != kept * area)
            throw new ArgumentException("Prototype tensor does not match the coefficient count.", nameof(prototypes));

        var detections = new List<SegDetection>();
        for (var row = 0; row < rowCount; row++)
        {
            var offset = row * columns;
            var score = rows[offset + 4];
            if (!(score >= minScore) || score <= 0) continue;

            var x1 = Math.Clamp(rows[offset], 0, frame.Size);
            var y1 = Math.Clamp(rows[offset + 1], 0, frame.Size);
            var x2 = Math.Clamp(rows[offset + 2], 0, frame.Size);
            var y2 = Math.Clamp(rows[offset + 3], 0, frame.Size);
            if (x2 - x1 < 1 || y2 - y1 < 1) continue;

            var outline = OutlineOf(rows.AsSpan(offset + BoxColumns, kept), prototypes, protoHeight, protoWidth, x1, y1, x2, y2, frame);
            if (outline is not null)
                detections.Add(new SegDetection((int)rows[offset + 5], score, outline));
        }

        return detections;
    }

    private static IReadOnlyList<PixelPoint>? OutlineOf(
        ReadOnlySpan<float> coefficients, float[] prototypes, int protoHeight, int protoWidth,
        float x1, float y1, float x2, float y2, LetterboxFrame frame)
    {
        var area = protoHeight * protoWidth;
        var logits = new float[area];
        for (var channel = 0; channel < coefficients.Length; channel++)
        {
            var weight = coefficients[channel];
            if (weight == 0) continue;
            var start = channel * area;
            for (var pixel = 0; pixel < area; pixel++)
                logits[pixel] += weight * prototypes[start + pixel];
        }

        using var small = new Mat(protoHeight, protoWidth, MatType.CV_32FC1);
        small.SetArray(logits);
        using var full = new Mat();
        Cv2.Resize(small, full, new Size(frame.Size, frame.Size), 0, 0, InterpolationFlags.Linear);

        // x1 <= x < x2 and y1 <= y < y2.
        var left = Math.Min(frame.Size - 1, (int)Math.Ceiling(x1));
        var top = Math.Min(frame.Size - 1, (int)Math.Ceiling(y1));
        var right = Math.Min(frame.Size, (int)Math.Ceiling(x2));
        var bottom = Math.Min(frame.Size, (int)Math.Ceiling(y2));
        if (right <= left || bottom <= top) return null;

        using var region = new Mat(full, new OpenCvSharp.Rect(left, top, right - left, bottom - top));
        using var positive = new Mat();
        Cv2.Threshold(region, positive, 0, 255, ThresholdTypes.Binary);
        using var binary = new Mat();
        positive.ConvertTo(binary, MatType.CV_8UC1);

        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        if (contours.Length == 0) return null;

        var chosen = contours.OrderByDescending(contour => Cv2.ContourArea(contour)).First();
        if (Cv2.ContourArea(chosen) < MinContourArea) return null;

        var epsilon = Math.Clamp(Cv2.ArcLength(chosen, true) * 0.0025, 0.75, 4.0);
        var simplified = Cv2.ApproxPolyDP(chosen, epsilon, true);
        if (simplified.Length < 3) return null;

        var outline = simplified.Select(point => ToMaster(point, left, top, frame)).ToList();
        return PolygonMath.HasSelfIntersection(outline, true)
            ? Cv2.ConvexHull(chosen).Select(point => ToMaster(point, left, top, frame)).ToList()
            : outline;
    }

    /// <summary>Contour points are pixel indices in the cut-out; +0.5 moves them to pixel centres, then the letterbox is undone.</summary>
    private static PixelPoint ToMaster(Point point, int left, int top, LetterboxFrame frame)
    {
        var x = (point.X + left + 0.5 - frame.Left) / frame.Scale;
        var y = (point.Y + top + 0.5 - frame.Top) / frame.Scale;
        return new PixelPoint(Math.Clamp(x, 0, frame.ImageWidth), Math.Clamp(y, 0, frame.ImageHeight));
    }
}
