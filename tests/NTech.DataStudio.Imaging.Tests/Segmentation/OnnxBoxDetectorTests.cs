using NTech.DataStudio.Imaging.Segmentation;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

public class OnnxBoxDetectorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ntech-boxdetect-" + Guid.NewGuid().ToString("N"));
    private readonly OnnxBoxDetector _detector = new();

    public OnnxBoxDetectorTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _detector.Dispose();
        Directory.Delete(_folder, true);
    }

    private string WriteModel(string meta)
    {
        var onnx = Path.Combine(_folder, "model.onnx");
        File.WriteAllBytes(onnx, new byte[] { 1 });
        File.WriteAllText(Path.Combine(_folder, OnnxAutoLabeler.MetaFile), meta);
        return onnx;
    }

    [Fact]
    public void WithoutAModel_SaysSo()
    {
        Assert.Contains("Chưa chọn model detect", _detector.Unavailable);
        Assert.Empty(_detector.ClassNames);
    }

    [Fact]
    public void AMissingFile_IsReportedWithItsPath()
    {
        _detector.SetModel(Path.Combine(_folder, "nope.onnx"));

        Assert.Contains("nope.onnx", _detector.Unavailable);
    }

    [Fact]
    public void AModelWithoutMeta_IsRefused()
    {
        var onnx = Path.Combine(_folder, "model.onnx");
        File.WriteAllBytes(onnx, new byte[] { 1 });
        _detector.SetModel(onnx);

        Assert.Contains(OnnxAutoLabeler.MetaFile, _detector.Unavailable);
    }

    [Fact]
    public void ASegmentModel_IsRefusedHere()
    {
        _detector.SetModel(WriteModel("{\"task\":\"segment\",\"classes\":[\"a\"]}"));

        Assert.Contains("không phải model detect", _detector.Unavailable);
    }

    [Fact]
    public void AModelWithoutEmbeddedNms_IsRefused()
    {
        _detector.SetModel(WriteModel(
            "{\"task\":\"detect\",\"classes\":[\"a\"],\"input\":{\"name\":\"images\",\"shape\":[1,3,640,640]},"
            + "\"output\":{\"kind\":\"detections_xyxy_conf_cls\",\"nms_embedded\":false}}"));

        Assert.Contains("NMS", _detector.Unavailable);
    }

    [Fact]
    public void AnOutputOfAnotherKind_IsRefused()
    {
        _detector.SetModel(WriteModel(
            "{\"task\":\"detect\",\"classes\":[\"a\"],\"input\":{\"name\":\"images\",\"shape\":[1,3,640,640]},"
            + "\"output\":{\"kind\":\"raw_xywh_scores_needs_nms\",\"nms_embedded\":false}}"));

        Assert.Contains("không phải dạng detect", _detector.Unavailable);
    }

    [Fact]
    public async Task DetectAsync_WhenUnavailable_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _detector.DetectAsync("x.jpg", 0.4));
    }
}
