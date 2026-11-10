using NTech.DataStudio.Imaging.Segmentation;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

/// <summary>
/// Needs a real segment model, so it returns early without one (like SamSegmenterTests): set NTECH_AUTOLABEL_MODEL to a
/// segment .onnx and NTECH_AUTOLABEL_IMAGE to a picture; NTECH_AUTOLABEL_CLASSES is a comma list (default dau_cos,bang_dinh,day_thit)
/// for a model that has no model.meta.json beside it. NTECH_AUTOLABEL_OUT, if set, receives a picture with the outlines drawn.
/// </summary>
public class OnnxAutoLabelerModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ntech-autolabel-real-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public async Task RealModel_FindsPolygonsInsideTheImage()
    {
        var model = Environment.GetEnvironmentVariable("NTECH_AUTOLABEL_MODEL");
        var image = Environment.GetEnvironmentVariable("NTECH_AUTOLABEL_IMAGE");
        if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(image) || !File.Exists(model) || !File.Exists(image)) return;

        Directory.CreateDirectory(_folder);
        var onnx = Path.Combine(_folder, "model.onnx");
        File.Copy(model, onnx);
        if (!File.Exists(Path.Combine(Path.GetDirectoryName(model)!, OnnxAutoLabeler.MetaFile)))
        {
            var classes = (Environment.GetEnvironmentVariable("NTECH_AUTOLABEL_CLASSES") ?? "dau_cos,bang_dinh,day_thit").Split(',');
            File.WriteAllText(Path.Combine(_folder, OnnxAutoLabeler.MetaFile),
                "{\"task\":\"segment\",\"classes\":[" + string.Join(",", classes.Select(name => "\"" + name + "\"")) + "],"
                + "\"input\":{\"name\":\"images\",\"shape\":[1,3,640,640]},"
                + "\"output\":{\"kind\":\"detections_xyxy_conf_cls_mask_coefficients\",\"nms_embedded\":true,"
                + "\"detections_tensor\":\"output0\",\"prototypes_tensor\":\"output1\"}}");
        }
        else
        {
            File.Copy(Path.Combine(Path.GetDirectoryName(model)!, OnnxAutoLabeler.MetaFile), Path.Combine(_folder, OnnxAutoLabeler.MetaFile));
        }

        using var labeler = new OnnxAutoLabeler();
        labeler.SetModel(onnx);
        Assert.Null(labeler.Unavailable);

        var found = await labeler.DetectAsync(image, 0.4);

        using var picture = Cv2.ImDecode(File.ReadAllBytes(image), ImreadModes.Color);
        Assert.All(found, label =>
        {
            Assert.InRange(label.Score, 0.4, 1.0001);
            Assert.True(label.Outline.Count >= 3);
            Assert.All(label.Outline, point =>
            {
                Assert.InRange(point.X, 0, picture.Width);
                Assert.InRange(point.Y, 0, picture.Height);
            });
        });

        if (Environment.GetEnvironmentVariable("NTECH_AUTOLABEL_OUT") is { Length: > 0 } output)
        {
            foreach (var label in found)
            {
                var points = label.Outline.Select(point => new Point((int)point.X, (int)point.Y)).ToArray();
                Cv2.Polylines(picture, new[] { points }, true, new Scalar(0, 255, 0), 2);
                Cv2.PutText(picture, $"{label.ClassName} {label.Score:0.00}", points[0], HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);
            }

            Cv2.ImEncode(".png", picture, out var encoded);
            File.WriteAllBytes(output, encoded);
            File.WriteAllText(output + ".txt", string.Join("\n", found.Select(label => $"{label.ClassName} {label.Score:0.000} {label.Outline.Count} pts")));
        }
    }
}
