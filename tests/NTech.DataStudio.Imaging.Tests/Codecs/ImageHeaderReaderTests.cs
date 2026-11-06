using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Imaging.Codecs;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Codecs;

public class ImageHeaderReaderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("a.png", "PNG")]
    [InlineData("a.jpg", "JPEG")]
    [InlineData("a.bmp", "BMP")]
    [InlineData("a.tif", "TIFF")]
    [InlineData("a.webp", "WEBP")]
    public void Read_ColorImage(string name, string format)
    {
        using var image = TestImages.Scene(1, 320, 200);
        var header = ImageHeaderReader.Read(TestImages.Write(_temp.Path, name, image));

        Assert.NotNull(header);
        Assert.Equal(format, header.Format);
        Assert.Equal(320, header.Width);
        Assert.Equal(200, header.Height);
        Assert.Equal(8, header.BitDepth);
        Assert.Equal(3, header.Channels);
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.tif")]
    public void Read_SixteenBitMono(string name)
    {
        using var image = new Mat(50, 70, MatType.CV_16UC1, new Scalar(1000));
        var header = ImageHeaderReader.Read(TestImages.Write(_temp.Path, name, image));

        Assert.NotNull(header);
        Assert.Equal((70, 50, 16, 1), (header.Width, header.Height, header.BitDepth, header.Channels));
    }

    [Fact]
    public void Read_PngWithAlpha()
    {
        using var image = new Mat(10, 10, MatType.CV_8UC4, new Scalar(1, 2, 3, 128));
        var header = ImageHeaderReader.Read(TestImages.Write(_temp.Path, "a.png", image));

        Assert.Equal(4, header!.Channels);
    }

    [Fact]
    public void Read_JpegWithExifBlockBeforeTheFrame()
    {
        using var image = TestImages.Scene(1, 320, 200);
        var path = Path.Combine(_temp.Path, "a.jpg");
        File.WriteAllBytes(path, TestImages.WithExifOrientation(TestImages.Encode(image, ".jpg"), 6));

        var header = ImageHeaderReader.Read(path);

        Assert.Equal((320, 200), (header!.Width, header.Height));
    }

    [Theory]
    [InlineData("a.jpg")]
    [InlineData("a.png")]
    [InlineData("a.bmp")]
    public void Read_ThrowsOnTruncatedFile(string name)
    {
        using var image = TestImages.Scene(1);
        var path = TestImages.Write(_temp.Path, name, image);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes.Take(bytes.Length / 2).ToArray());

        Assert.Throws<ImageReadException>(() => ImageHeaderReader.Read(path));
    }

    [Fact]
    public void Read_ReturnsNullForUnknownContent()
    {
        var path = Path.Combine(_temp.Path, "a.jpg");
        File.WriteAllText(path, new string('x', 200));

        Assert.Null(ImageHeaderReader.Read(path));
    }

    [Fact]
    public void Read_ThrowsOnTinyFile()
    {
        var path = Path.Combine(_temp.Path, "a.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        Assert.Throws<ImageReadException>(() => ImageHeaderReader.Read(path));
    }
}
