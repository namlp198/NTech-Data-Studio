using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Tests.Prepare;

public class SmallPartsTests
{
    [Theory]
    [InlineData(120_000_000, 16_000_000_000, 8, 8)]
    [InlineData(120_000_000, 480_000_000, 8, 2)]
    [InlineData(120_000_000, 100_000_000, 8, 1)]
    [InlineData(0, 100, 4, 4)]
    public void Threads_AreLimitedByMemoryAndCores(long perImage, long available, int cores, int expected)
    {
        Assert.Equal(expected, ParallelismPolicy.ThreadsFor(perImage, available, cores));
    }

    [Fact]
    public void Audit_SummarizesTheBatchAndSeparatesCorruptFiles()
    {
        SourceImage Image(long id, int width, int height, string format, int depth, int channels, long size, RejectReason? reject = null) =>
            new(id, $"{id}", $"{id}", false, size, 0, format, width, height, depth, channels, null, Reject: reject);

        var report = AuditReportBuilder.Build(new[]
        {
            Image(1, 5472, 3648, "BMP", 8, 3, 60_000_000),
            Image(2, 5472, 3648, "BMP", 8, 3, 60_000_000),
            Image(3, 1920, 1080, "TIFF", 16, 1, 4_000_000),
            Image(4, 0, 0, "?", 0, 0, 100, RejectReason.Corrupt)
        });

        Assert.Equal(4, report.TotalFiles);
        Assert.Equal(1, report.CorruptFiles);
        Assert.Equal(124_000_100, report.TotalBytes);
        Assert.Equal(60_000_000, report.LargestFileBytes);
        Assert.Equal(5472, report.MedianLongEdge);
        Assert.Equal(new AuditBucket("5472 × 3648", 2), report.Resolutions[0]);
        Assert.Equal(new AuditBucket("BMP", 2), report.Formats[0]);
        Assert.Contains(new AuditBucket("16-bit", 1), report.BitDepths);
        Assert.Contains(new AuditBucket("1 kênh (mono)", 1), report.ChannelCounts);
        Assert.InRange(report.EstimatedMasterBytes, 1, report.TotalBytes);
    }

    [Fact]
    public void Audit_OfNothingIsEmptyNotAnError()
    {
        var report = AuditReportBuilder.Build(Array.Empty<SourceImage>());

        Assert.Equal((0, 0L, 0), (report.TotalFiles, report.TotalBytes, report.MedianLongEdge));
    }
}
