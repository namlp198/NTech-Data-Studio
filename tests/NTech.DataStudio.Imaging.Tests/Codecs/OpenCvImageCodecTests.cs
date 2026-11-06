using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Codecs;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Codecs;

public class OpenCvImageCodecTests : IDisposable
{
    private static readonly NormalizeOptions KeepSize = new(
        new GeometryPlan(GeometryMethod.Downscale, MaxLongEdge: 4096), MasterFormat.Jpeg95, MonoMode.KeepOneChannel);

    private readonly TempFolder _temp = new();
    private readonly OpenCvImageCodec _codec = new();

    public void Dispose() => _temp.Dispose();

    private string MasterFolder => Directory.CreateDirectory(Path.Combine(_temp.Path, "master")).FullName;
    private string ThumbFolder => Directory.CreateDirectory(Path.Combine(_temp.Path, "thumbs")).FullName;

    private IReadOnlyList<MasterFile> Normalize(string path, NormalizeOptions options) =>
        _codec.Normalize(path, options, MasterFolder, ThumbFolder, "bộ ảnh");

    private Mat ReadMaster(MasterFile master) =>
        Cv2.ImDecode(File.ReadAllBytes(Path.Combine(MasterFolder, master.FileName)), ImreadModes.Unchanged);

    [Fact]
    public void ReadHeader_FallsBackToDecodingAndReportsCorruptFiles()
    {
        var path = Path.Combine(_temp.Path, "a.jpg");
        File.WriteAllText(path, new string('x', 200));

        Assert.Throws<ImageReadException>(() => _codec.ReadHeader(path));
    }

    [Fact]
    public void Analyze_WorksOnANonAsciiPathAndWritesAThumbnail()
    {
        using var image = TestImages.Scene(1, 800, 400);
        var path = TestImages.Write(_temp.Path, "ảnh số 1.png", image);
        var thumbnail = Path.Combine(ThumbFolder, "t.jpg");

        var analysis = _codec.Analyze(path, thumbnail);

        Assert.Equal(64, analysis.Sha256.Length);
        using var thumb = Cv2.ImDecode(File.ReadAllBytes(thumbnail), ImreadModes.Unchanged);
        Assert.Equal(256, Math.Max(thumb.Width, thumb.Height));
    }

    [Fact]
    public void Analyze_ThrowsOnUndecodableBytes()
    {
        var path = Path.Combine(_temp.Path, "a.png");
        File.WriteAllBytes(path, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3, 4, 5, 6, 7, 8 });

        Assert.Throws<ImageReadException>(() => _codec.Analyze(path, Path.Combine(ThumbFolder, "t.jpg")));
    }

    [Fact]
    public void Analyze_MissingFileIsAReadError()
    {
        Assert.Throws<ImageReadException>(() =>
            _codec.Analyze(Path.Combine(_temp.Path, "none.png"), Path.Combine(ThumbFolder, "t.jpg")));
    }

    [Fact]
    public void Normalize_DownscalesTheLongEdgeAndRecordsTheScale()
    {
        using var image = TestImages.Scene(1, 3000, 1500);
        var path = TestImages.Write(_temp.Path, "a.bmp", image);
        var options = KeepSize with { Geometry = new GeometryPlan(GeometryMethod.Downscale, MaxLongEdge: 2048) };

        var master = Assert.Single(Normalize(path, options));

        Assert.Equal((2048, 1024), (master.Width, master.Height));
        Assert.Equal(2048 / 3000.0, master.Scale, 6);
        Assert.Equal(new FileInfo(Path.Combine(MasterFolder, master.FileName)).Length, master.FileSize);
    }

    [Fact]
    public void Normalize_NeverUpscales()
    {
        using var image = TestImages.Scene(1, 300, 200);
        var master = Assert.Single(Normalize(TestImages.Write(_temp.Path, "a.png", image), KeepSize));

        Assert.Equal((300, 200, 1.0), (master.Width, master.Height, master.Scale));
    }

    [Fact]
    public void Normalize_AppliesExifRotation()
    {
        using var image = TestImages.Scene(1, 320, 200);
        var path = Path.Combine(_temp.Path, "a.jpg");
        File.WriteAllBytes(path, TestImages.WithExifOrientation(TestImages.Encode(image, ".jpg"), 6));

        var master = Assert.Single(Normalize(path, KeepSize));

        Assert.Equal((200, 320), (master.Width, master.Height));
    }

    [Fact]
    public void Normalize_DropsMetadata()
    {
        using var image = TestImages.Scene(1, 320, 200);
        var path = Path.Combine(_temp.Path, "a.jpg");
        File.WriteAllBytes(path, TestImages.WithExifOrientation(TestImages.Encode(image, ".jpg"), 6));

        var master = Assert.Single(Normalize(path, KeepSize));

        var bytes = File.ReadAllBytes(Path.Combine(MasterFolder, master.FileName));
        Assert.Equal(-1, bytes.AsSpan().IndexOf("Exif"u8));
    }

    [Fact]
    public void Normalize_StretchesALowRangeSixteenBitImageToTheFullEightBitRange()
    {
        // 12-bit camera data: dividing by 256 would give values of 0..15.
        using var image = new Mat(100, 100, MatType.CV_16UC1);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
                image.Set(y, x, (ushort)(x * 40));
        }

        var master = Assert.Single(Normalize(TestImages.Write(_temp.Path, "a.tif", image), KeepSize with { Format = MasterFormat.Png }));

        using var output = ReadMaster(master);
        Assert.Equal(MatType.CV_8UC1, output.Type());
        Cv2.MinMaxLoc(output, out double low, out double high);
        Assert.True(low <= 5, $"low {low}");
        Assert.True(high >= 250, $"high {high}");
    }

    [Fact]
    public void Normalize_DoesNotDivideByZeroOnAFlatSixteenBitImage()
    {
        using var image = new Mat(20, 20, MatType.CV_16UC1, new Scalar(500));

        var master = Assert.Single(Normalize(TestImages.Write(_temp.Path, "a.png", image), KeepSize));

        Assert.Equal(20, master.Width);
    }

    [Fact]
    public void Normalize_DropsAlpha()
    {
        using var image = new Mat(20, 20, MatType.CV_8UC4, new Scalar(10, 20, 30, 128));

        var master = Assert.Single(Normalize(TestImages.Write(_temp.Path, "a.png", image), KeepSize with { Format = MasterFormat.Png }));

        using var output = ReadMaster(master);
        Assert.Equal(3, output.Channels());
    }

    [Theory]
    [InlineData(MonoMode.KeepOneChannel, 1)]
    [InlineData(MonoMode.TripleChannel, 3)]
    public void Normalize_HonorsTheMonoMode(MonoMode mono, int channels)
    {
        using var image = new Mat(20, 20, MatType.CV_8UC1, new Scalar(100));

        var master = Assert.Single(Normalize(
            TestImages.Write(_temp.Path, "a.png", image),
            KeepSize with { Format = MasterFormat.Png, Mono = mono }));

        using var output = ReadMaster(master);
        Assert.Equal(channels, output.Channels());
    }

    [Fact]
    public void Normalize_PngMasterIsLossless()
    {
        using var image = TestImages.Scene(3, 200, 100);

        var master = Assert.Single(Normalize(TestImages.Write(_temp.Path, "a.bmp", image), KeepSize with { Format = MasterFormat.Png }));

        using var output = ReadMaster(master);
        using var difference = new Mat();
        Cv2.Absdiff(image, output, difference);
        Assert.Equal(new Scalar(0, 0, 0, 0), Cv2.Sum(difference));
    }

    [Fact]
    public void Normalize_TilesKeepOriginAndPixels()
    {
        using var image = TestImages.Scene(4, 1000, 600);
        var plan = new GeometryPlan(GeometryMethod.Tile, TileSize: 640, TileOverlap: 64);

        var masters = Normalize(TestImages.Write(_temp.Path, "a.png", image), KeepSize with { Geometry = plan, Format = MasterFormat.Png });

        Assert.Equal(TileLayout.Count(1000, 600, 640, 64), masters.Count);
        Assert.Equal(masters.Count, masters.Select(master => master.FileName).Distinct().Count());
        foreach (var master in masters)
        {
            using var output = ReadMaster(master);
            using var expected = new Mat(image, new Rect(master.OriginX, master.OriginY, master.Width, master.Height));
            using var difference = new Mat();
            Cv2.Absdiff(expected, output, difference);
            Assert.Equal(new Scalar(0, 0, 0, 0), Cv2.Sum(difference));
        }
    }

    [Fact]
    public void Normalize_RoiIsClampedToTheImage()
    {
        using var image = TestImages.Scene(5, 400, 300);
        var plan = new GeometryPlan(GeometryMethod.RoiCrop, Roi: new PixelRect(300, 200, 500, 500));

        var master = Assert.Single(Normalize(TestImages.Write(_temp.Path, "a.png", image), KeepSize with { Geometry = plan }));

        Assert.Equal((100, 100, 300, 200), (master.Width, master.Height, master.OriginX, master.OriginY));
    }

    [Fact]
    public void Normalize_RoiOutsideTheImageIsAReadError()
    {
        using var image = TestImages.Scene(5, 400, 300);
        var plan = new GeometryPlan(GeometryMethod.RoiCrop, Roi: new PixelRect(900, 900, 50, 50));

        Assert.Throws<ImageReadException>(() =>
            Normalize(TestImages.Write(_temp.Path, "a.png", image), KeepSize with { Geometry = plan }));
    }

    [Fact]
    public void Normalize_LeavesNoPartialFilesAndNeverTouchesTheSource()
    {
        using var image = TestImages.Scene(6);
        var path = TestImages.Write(_temp.Path, "a.bmp", image);
        var before = File.ReadAllBytes(path);

        Normalize(path, KeepSize);

        Assert.Empty(Directory.GetFiles(_temp.Path, "*.partial", SearchOption.AllDirectories));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void RenderPreview_ReportsTheFullOutputAndKeepsImagesSmall()
    {
        using var image = TestImages.Scene(7, 2000, 1000);
        var plan = new GeometryPlan(GeometryMethod.Tile, TileSize: 640, TileOverlap: 64);

        var preview = _codec.RenderPreview(TestImages.Write(_temp.Path, "a.bmp", image), KeepSize with { Geometry = plan }, 512);

        Assert.Equal(TileLayout.Count(2000, 1000, 640, 64), preview.MasterCount);
        Assert.Equal((2000, 1000, 640, 640), (preview.SourceWidth, preview.SourceHeight, preview.MasterWidth, preview.MasterHeight));
        Assert.True(preview.MasterBytes > 0);
        using var before = Cv2.ImDecode(preview.BeforeJpeg, ImreadModes.Unchanged);
        Assert.Equal(512, Math.Max(before.Width, before.Height));
    }
}
