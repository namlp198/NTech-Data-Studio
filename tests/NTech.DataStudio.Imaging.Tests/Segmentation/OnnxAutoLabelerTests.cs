using NTech.DataStudio.Imaging.Segmentation;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

public class OnnxAutoLabelerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ntech-autolabel-" + Guid.NewGuid().ToString("N"));
    private readonly OnnxAutoLabeler _labeler = new();

    public OnnxAutoLabelerTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _labeler.Dispose();
        Directory.Delete(_folder, true);
    }

    [Fact]
    public void WithoutAModel_SaysSo()
    {
        Assert.Contains("Chưa chọn model", _labeler.Unavailable);
        Assert.Empty(_labeler.ClassNames);
    }

    [Fact]
    public void AMissingFile_IsReportedWithItsPath()
    {
        _labeler.SetModel(Path.Combine(_folder, "nope.onnx"));

        Assert.Contains("nope.onnx", _labeler.Unavailable);
    }

    [Fact]
    public void AModelWithoutMeta_IsRefused()
    {
        var onnx = Path.Combine(_folder, "model.onnx");
        File.WriteAllBytes(onnx, new byte[] { 1 });
        _labeler.SetModel(onnx);

        Assert.Contains(OnnxAutoLabeler.MetaFile, _labeler.Unavailable);
    }

    [Fact]
    public void ADetectModel_IsRefusedBecauseItHasNoMasks()
    {
        var onnx = Path.Combine(_folder, "model.onnx");
        File.WriteAllBytes(onnx, new byte[] { 1 });
        File.WriteAllText(Path.Combine(_folder, OnnxAutoLabeler.MetaFile), "{\"task\":\"detect\",\"classes\":[\"a\"]}");
        _labeler.SetModel(onnx);

        Assert.Contains("không phải model segment", _labeler.Unavailable);
    }

    [Fact]
    public async Task DetectAsync_WhenUnavailable_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _labeler.DetectAsync("x.jpg", 0.4));
    }
}
