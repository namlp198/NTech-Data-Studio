using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Segmentation;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

/// <summary>
/// Needs the real SAM files (in the usual folder) and a hand-labeled picture, so it returns early without them.
/// Set NTECH_SAM_HANDLABELS to a JSON file {"image": path, "width": w, "height": h, "annotations": [{"class": name, "points": [[x, y], ...]}]}:
/// every hand polygon's bounding box plays the part of a perfect detector's box, SAM cuts it out (with the planned
/// regions, and with the whole picture only, for comparison), and the cut-out is scored by its overlap with the hand polygon.
/// NTECH_SAM_OUT, if set, receives the numbers as text and NTECH_SAM_PICTURE a picture with the outlines drawn.
/// </summary>
public class DetectThenSegmentRealSamTests
{
    private sealed class BoxesFromHandLabels : IBoxDetector
    {
        private readonly BoxDetection _detection;

        public BoxesFromHandLabels(BoxDetection detection) => _detection = detection;

        public string? Unavailable => null;
        public IReadOnlyList<string> ClassNames => _detection.Boxes.Select(box => box.ClassName).Distinct().ToList();
        public void SetModel(string? onnxPath) { }
        public Task<BoxDetection> DetectAsync(string imagePath, double minScore, CancellationToken cancellationToken = default) => Task.FromResult(_detection);
    }

    /// <summary>The same SAM, but every box is given the whole picture: what a plain "box prompt on the whole picture" would do.</summary>
    private sealed class WholePictureOnly : ISmartSegmenter
    {
        private readonly ISmartSegmenter _inner;

        public WholePictureOnly(ISmartSegmenter inner) => _inner = inner;

        public string? Unavailable => _inner.Unavailable;
        public Task PrepareAsync(string imagePath, PixelRect? region, CancellationToken cancellationToken = default) => _inner.PrepareAsync(imagePath, null, cancellationToken);

        public Task<IReadOnlyList<PixelPoint>?> SegmentAsync(string imagePath, PixelRect? region, SegmentRequest request, CancellationToken cancellationToken = default) =>
            _inner.SegmentAsync(imagePath, null, request, cancellationToken);
    }

    private static double Overlap(IReadOnlyList<PixelPoint> first, IReadOnlyList<PixelPoint> second, int width, int height)
    {
        using var a = Fill(first, width, height);
        using var b = Fill(second, width, height);
        using var both = new Mat();
        using var either = new Mat();
        Cv2.BitwiseAnd(a, b, both);
        Cv2.BitwiseOr(a, b, either);
        var union = Cv2.CountNonZero(either);
        return union == 0 ? 0 : (double)Cv2.CountNonZero(both) / union;
    }

    private static double LongSideInSamPixels(IReadOnlyList<PixelPoint> points, int width, int height) =>
        Math.Max(points.Max(point => point.X) - points.Min(point => point.X), points.Max(point => point.Y) - points.Min(point => point.Y))
        * SamRegionPlanner.SamInputSide / Math.Max(width, height);

    private static Mat Fill(IReadOnlyList<PixelPoint> points, int width, int height)
    {
        var mask = new Mat(height, width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { points.Select(point => new Point((int)Math.Round(point.X), (int)Math.Round(point.Y))).ToArray() }, Scalar.All(255));
        return mask;
    }

    [Fact]
    public async Task RealSam_CutsHandLabelBoxesOutCloseToTheHandPolygons()
    {
        var labels = Environment.GetEnvironmentVariable("NTECH_SAM_HANDLABELS");
        if (string.IsNullOrEmpty(labels) || !File.Exists(labels)) return;
        using var sam = new SamSegmenter(SamSegmenter.DefaultFolder());
        if (sam.Unavailable is not null) return;

        using var document = JsonDocument.Parse(File.ReadAllText(labels));
        var root = document.RootElement;
        var image = root.GetProperty("image").GetString()!;
        var width = root.GetProperty("width").GetInt32();
        var height = root.GetProperty("height").GetInt32();
        var hand = root.GetProperty("annotations").EnumerateArray()
            .Select(item => (
                Class: item.GetProperty("class").GetString()!,
                Points: (IReadOnlyList<PixelPoint>)item.GetProperty("points").EnumerateArray().Select(point => new PixelPoint(point[0].GetDouble(), point[1].GetDouble())).ToList()))
            .ToList();

        var boxes = hand.Select(item =>
        {
            var left = item.Points.Min(point => point.X);
            var top = item.Points.Min(point => point.Y);
            return new DetectedBox(item.Class, 0.9, new PixelRect(left, top, item.Points.Max(point => point.X) - left, item.Points.Max(point => point.Y) - top));
        }).ToList();
        var detector = new BoxesFromHandLabels(new BoxDetection(width, height, boxes));

        var report = new StringBuilder();
        report.AppendLine($"{hand.Count} hand polygons on {Path.GetFileName(image)} ({width}x{height}); regions planned: {SamRegionPlanner.Plan(width, height, boxes).Count}");

        IReadOnlyList<AutoLabel>? planned = null;
        IReadOnlyList<AutoLabel>? whole = null;
        foreach (var (name, segmenter) in new (string, ISmartSegmenter)[] { ("planned regions", sam), ("whole picture only", new WholePictureOnly(sam)) })
        {
            var clock = Stopwatch.StartNew();
            var found = await new DetectThenSegmentLabeler(detector, segmenter).DetectAsync(image, 0.4);
            clock.Stop();
            if (name == "planned regions") planned = found;
            else whole = found;

            var scores = new List<(string Class, double Iou, bool Box, bool Tiny)>();
            foreach (var label in found)
            {
                // Match the cut-out to the hand polygon of the same box: the one it overlaps most.
                var best = hand
                    .Where(item => item.Class == label.ClassName)
                    .Select(item => (Item: item, Iou: Overlap(label.Outline, item.Points, width, height)))
                    .OrderByDescending(pair => pair.Iou)
                    .FirstOrDefault();
                var tiny = best.Item.Points is not null && LongSideInSamPixels(best.Item.Points, width, height) < SamRegionPlanner.TinyBoxSamPixels;
                scores.Add((label.ClassName, best.Iou, label.IsBoxOutline, tiny));
            }

            report.AppendLine($"[{name}] {found.Count} labels in {clock.Elapsed.TotalSeconds:0.0} s; mean overlap with the hand polygon {scores.Average(item => item.Iou):0.00}; kept as box {scores.Count(item => item.Box)}");
            foreach (var group in scores.GroupBy(item => item.Class))
                report.AppendLine($"    {group.Key}: {group.Count()} labels, mean overlap {group.Average(item => item.Iou):0.00}, below 0.5: {group.Count(item => item.Iou < 0.5)}");
            if (scores.Any(item => item.Tiny))
                report.AppendLine($"    tiny (under {SamRegionPlanner.TinyBoxSamPixels} px in SAM's view): {scores.Count(item => item.Tiny)} labels, mean overlap {scores.Where(item => item.Tiny).Average(item => item.Iou):0.00}");
            Assert.Equal(boxes.Count, found.Count);
            Assert.All(found, label => Assert.All(label.Outline, point =>
            {
                Assert.InRange(point.X, -0.001, width + 0.001);
                Assert.InRange(point.Y, -0.001, height + 0.001);
            }));
        }

        if (Environment.GetEnvironmentVariable("NTECH_SAM_OUT") is { Length: > 0 } outPath) File.WriteAllText(outPath, report.ToString());
        if (Environment.GetEnvironmentVariable("NTECH_SAM_PICTURE") is { Length: > 0 } picturePath && planned is not null)
        {
            // Green: hand polygon. Yellow: SAM with the whole picture only. Red: SAM with the planned regions (blue when kept as box).
            // NTECH_SAM_CROP "x,y,w,h" draws only that part, magnified 4 times, so the outlines can be compared by eye.
            using var picture = Cv2.ImDecode(File.ReadAllBytes(image), ImreadModes.Color);
            var crop = new OpenCvSharp.Rect(0, 0, picture.Width, picture.Height);
            var zoom = 1;
            if (Environment.GetEnvironmentVariable("NTECH_SAM_CROP") is { Length: > 0 } text)
            {
                var part = text.Split(',').Select(int.Parse).ToArray();
                crop = new OpenCvSharp.Rect(part[0], part[1], part[2], part[3]);
                zoom = 4;
            }

            using var cut = new Mat(picture, crop);
            using var view = new Mat();
            Cv2.Resize(cut, view, new Size(crop.Width * zoom, crop.Height * zoom), 0, 0, InterpolationFlags.Cubic);
            void Draw(IReadOnlyList<PixelPoint> outline, Scalar color) =>
                Cv2.Polylines(view, new[] { outline.Select(point => new Point((int)((point.X - crop.X) * zoom), (int)((point.Y - crop.Y) * zoom))).ToArray() }, true, color, 1);
            foreach (var item in hand) Draw(item.Points, new Scalar(0, 255, 0));
            foreach (var label in whole!) Draw(label.Outline, new Scalar(0, 255, 255));
            foreach (var label in planned) Draw(label.Outline, label.IsBoxOutline ? new Scalar(255, 0, 0) : new Scalar(0, 0, 255));
            Cv2.ImWrite(picturePath, view);
        }
    }
}
