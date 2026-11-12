using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Imaging.Segmentation;

/// <summary>One box read from a detect model's output: class index, score and the box in master pixels.</summary>
internal sealed record DecodedBox(int ClassId, double Score, PixelRect Box);

/// <summary>
/// Reads the output of a Trainer detect ONNX as <c>model.meta.json</c> describes it (decision 0012 of the Trainer):
/// <c>(1, N, 6)</c> rows of x1 y1 x2 y2 score class in pixels of the letterboxed input, padded with zero rows. Boxes may
/// reach outside the input, so they are clipped to it first; then the letterbox is undone and the box is clipped to the picture.
/// </summary>
internal static class YoloBoxDecoder
{
    public const int Columns = 6;

    private const double MinSide = 1;

    public static List<DecodedBox> Decode(float[] rows, int rowCount, int columns, LetterboxFrame frame, double minScore)
    {
        if (columns != Columns) throw new ArgumentException("A detect row has six values.", nameof(columns));

        var detections = new List<DecodedBox>();
        for (var row = 0; row < rowCount; row++)
        {
            var offset = row * columns;
            var score = rows[offset + 4];
            if (!(score >= minScore) || score <= 0) continue;

            var left = Math.Clamp(rows[offset], 0, frame.Size);
            var top = Math.Clamp(rows[offset + 1], 0, frame.Size);
            var right = Math.Clamp(rows[offset + 2], 0, frame.Size);
            var bottom = Math.Clamp(rows[offset + 3], 0, frame.Size);

            var x1 = Math.Clamp((left - frame.Left) / frame.Scale, 0, frame.ImageWidth);
            var y1 = Math.Clamp((top - frame.Top) / frame.Scale, 0, frame.ImageHeight);
            var x2 = Math.Clamp((right - frame.Left) / frame.Scale, 0, frame.ImageWidth);
            var y2 = Math.Clamp((bottom - frame.Top) / frame.Scale, 0, frame.ImageHeight);
            if (x2 - x1 < MinSide || y2 - y1 < MinSide) continue;

            detections.Add(new DecodedBox((int)rows[offset + 5], score, new PixelRect(x1, y1, x2 - x1, y2 - y1)));
        }

        return detections;
    }
}
