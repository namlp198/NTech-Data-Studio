using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Segmentation;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

/// <summary>
/// Decision 0016. The model files are not in the repository, so the tests that need them return early on a machine that
/// does not have them (xunit 2 has no dynamic skip); the missing-model tests always run.
/// </summary>
public class SamSegmenterTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly SamSegmenter _segmenter = new(SamSegmenter.DefaultFolder());

    public void Dispose()
    {
        _segmenter.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task MissingModel_SaysWhichFilesAndWhereAndRefusesToRun()
    {
        using var empty = new SamSegmenter(Path.Combine(_temp.Path, "no-models"));

        Assert.Contains(SamSegmenter.EncoderFile, empty.Unavailable);
        Assert.Contains(SamSegmenter.DecoderFile, empty.Unavailable);
        Assert.Contains("no-models", empty.Unavailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.PrepareAsync("whatever.jpg"));
    }

    // Grey noise with a red block (200,150)-(400,350) and a blue block (500,300)-(700,500).
    private string MakePicture(string name)
    {
        using var image = new Mat(600, 800, MatType.CV_8UC3);
        Cv2.Randu(image, new Scalar(90, 90, 90), new Scalar(150, 150, 150));
        Cv2.Rectangle(image, new Rect(200, 150, 200, 200), new Scalar(40, 40, 220), -1);
        Cv2.Rectangle(image, new Rect(500, 300, 200, 200), new Scalar(220, 80, 40), -1);
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllBytes(path, image.ImEncode(".png"));
        return path;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<PixelPoint> outline) =>
        (outline.Min(point => point.X), outline.Min(point => point.Y), outline.Max(point => point.X), outline.Max(point => point.Y));

    [Fact]
    public async Task AClickInsideABlock_GivesItsOutlineInMasterPixels()
    {
        if (_segmenter.Unavailable is not null) return;

        var path = MakePicture("p.png");
        await _segmenter.PrepareAsync(path);

        var outline = await _segmenter.SegmentAsync(path, new[] { new SegmentPrompt(new PixelPoint(300, 250), true) });

        Assert.NotNull(outline);
        var (minX, minY, maxX, maxY) = Bounds(outline!);
        Assert.InRange(minX, 196, 204);
        Assert.InRange(minY, 146, 154);
        Assert.InRange(maxX, 396, 404);
        Assert.InRange(maxY, 346, 354);
        Assert.True(outline!.Count is >= 4 and < 60, $"{outline.Count} vertices");
        Assert.False(PolygonMath.HasSelfIntersection(outline, true));
    }

    [Fact]
    public async Task ASecondPicture_ReplacesTheFirstAndAnotherBlockIsFound()
    {
        if (_segmenter.Unavailable is not null) return;

        var first = MakePicture("a.png");
        var second = MakePicture("b.png");
        await _segmenter.PrepareAsync(first);
        await _segmenter.PrepareAsync(second);

        var outline = await _segmenter.SegmentAsync(second, new[] { new SegmentPrompt(new PixelPoint(600, 400), true) });

        Assert.NotNull(outline);
        var (minX, minY, maxX, maxY) = Bounds(outline!);
        Assert.InRange(minX, 496, 504);
        Assert.InRange(maxX, 696, 704);
        Assert.InRange(minY, 296, 304);
        Assert.InRange(maxY, 496, 504);
    }

    [Fact]
    public async Task APathWithVietnameseLetters_Opens()
    {
        if (_segmenter.Unavailable is not null) return;

        var folder = Path.Combine(_temp.Path, "ảnh thô");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ảnh 1.png");
        File.Copy(MakePicture("src.png"), path);

        var outline = await _segmenter.SegmentAsync(path, new[] { new SegmentPrompt(new PixelPoint(300, 250), true) });

        Assert.NotNull(outline);
    }

    [Fact]
    public async Task NoPoints_GiveNothing()
    {
        if (_segmenter.Unavailable is not null) return;

        Assert.Null(await _segmenter.SegmentAsync(MakePicture("n.png"), Array.Empty<SegmentPrompt>()));
    }
}
