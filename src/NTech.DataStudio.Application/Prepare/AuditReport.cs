using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Prepare;

public sealed record AuditBucket(string Label, int Count);

public sealed record AuditReport(
    int TotalFiles,
    int CorruptFiles,
    long TotalBytes,
    long LargestFileBytes,
    int MedianLongEdge,
    long EstimatedMasterBytes,
    IReadOnlyList<AuditBucket> Resolutions,
    IReadOnlyList<AuditBucket> Formats,
    IReadOnlyList<AuditBucket> BitDepths,
    IReadOnlyList<AuditBucket> ChannelCounts);

public static class AuditReportBuilder
{
    // Rough sizes of a typical photo: JPEG q95 per pixel of 3 channels, lossless PNG per pixel.
    private const double JpegBytesPerPixel = 0.6;
    private const double PngBytesPerPixel = 1.8;

    public static AuditReport Build(IReadOnlyCollection<SourceImage> sources, MasterFormat format = MasterFormat.Jpeg95)
    {
        var readable = sources.Where(source => source.Reject != RejectReason.Corrupt).ToList();
        var longEdges = readable.Select(source => source.LongEdge).Order().ToList();

        return new AuditReport(
            sources.Count,
            sources.Count - readable.Count,
            sources.Sum(source => source.FileSize),
            sources.Count == 0 ? 0 : sources.Max(source => source.FileSize),
            longEdges.Count == 0 ? 0 : longEdges[longEdges.Count / 2],
            EstimateMasterBytes(readable, format),
            Buckets(readable, source => $"{source.Width} × {source.Height}"),
            Buckets(readable, source => source.Format),
            Buckets(readable, source => $"{source.BitDepth}-bit"),
            Buckets(readable, source => source.Channels switch
            {
                1 => "1 kênh (mono)",
                3 => "3 kênh",
                4 => "4 kênh (có alpha)",
                var count => $"{count} kênh"
            }));
    }

    public static long EstimateMasterBytes(IEnumerable<SourceImage> sources, MasterFormat format)
    {
        var bytesPerPixel = format == MasterFormat.Png ? PngBytesPerPixel : JpegBytesPerPixel;
        return (long)sources.Sum(source => source.Width * (double)source.Height * bytesPerPixel);
    }

    private static List<AuditBucket> Buckets(IEnumerable<SourceImage> sources, Func<SourceImage, string> label) =>
        sources.GroupBy(label)
            .Select(group => new AuditBucket(group.Key, group.Count()))
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Label, StringComparer.Ordinal)
            .ToList();
}
