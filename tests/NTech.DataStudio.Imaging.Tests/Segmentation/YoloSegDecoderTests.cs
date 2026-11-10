using NTech.DataStudio.Imaging.Segmentation;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

/// <summary>Decision 0017: the output of a Trainer segment ONNX is read as model.meta.json describes it. No model file needed.</summary>
public class YoloSegDecoderTests
{
    private const int Size = 160;
    private const int ProtoSide = Size / 4;
    private const int K = 4;

    /// <summary>One prototype channel that is positive inside a box (in input pixels) and negative elsewhere.</summary>
    private static float[] Prototypes(int channel, int left, int top, int right, int bottom)
    {
        var protos = new float[K * ProtoSide * ProtoSide];
        for (var y = 0; y < ProtoSide; y++)
        for (var x = 0; x < ProtoSide; x++)
        {
            var inside = x * 4 >= left && x * 4 < right && y * 4 >= top && y * 4 < bottom;
            protos[channel * ProtoSide * ProtoSide + y * ProtoSide + x] = inside ? 5f : -5f;
        }

        return protos;
    }

    private static float[] Row(float x1, float y1, float x2, float y2, float score, int classId, float coefficient0)
    {
        var row = new float[6 + K];
        row[0] = x1; row[1] = y1; row[2] = x2; row[3] = y2; row[4] = score; row[5] = classId; row[6] = coefficient0;
        return row;
    }

    [Fact]
    public void Frame_CentersAndScalesLikeTheTrainer()
    {
        var frame = LetterboxFrame.For(400, 200, Size);

        Assert.Equal(0.4, frame.Scale, 6);
        Assert.Equal(160, frame.NewWidth);
        Assert.Equal(80, frame.NewHeight);
        Assert.Equal(0, frame.Left);
        Assert.Equal(40, frame.Top);
    }

    [Fact]
    public void Decode_GivesTheMaskOutlineInMasterPixelsAfterUndoingTheLetterbox()
    {
        // 400 x 200 master in a 160 input: scale 0.4, top padding 40. Object at input x 40..80, y 60..100
        // is master x 100..200, y 50..150.
        var frame = LetterboxFrame.For(400, 200, Size);
        var rows = Row(40, 60, 80, 100, 0.9f, 1, 1f);
        var protos = Prototypes(0, 40, 60, 80, 100);

        var found = YoloSegDecoder.Decode(rows, 1, 6 + K, protos, ProtoSide, ProtoSide, frame, 0.4);

        var detection = Assert.Single(found);
        Assert.Equal(1, detection.ClassId);
        Assert.Equal(0.9, detection.Score, 3);
        Assert.InRange(detection.Outline.Min(point => point.X), 95, 105);
        Assert.InRange(detection.Outline.Max(point => point.X), 195, 205);
        Assert.InRange(detection.Outline.Min(point => point.Y), 45, 55);
        Assert.InRange(detection.Outline.Max(point => point.Y), 145, 155);
    }

    [Fact]
    public void Decode_SkipsLowScoresAndPaddingRows()
    {
        var frame = LetterboxFrame.For(160, 160, Size);
        var rows = Row(40, 60, 80, 100, 0.3f, 0, 1f).Concat(new float[6 + K]).ToArray();
        var protos = Prototypes(0, 40, 60, 80, 100);

        Assert.Empty(YoloSegDecoder.Decode(rows, 2, 6 + K, protos, ProtoSide, ProtoSide, frame, 0.4));
        Assert.Single(YoloSegDecoder.Decode(rows, 2, 6 + K, protos, ProtoSide, ProtoSide, frame, 0.25));
    }

    [Fact]
    public void Decode_CutsTheMaskToItsOwnBox()
    {
        // The prototype lights x 20..120 but the box only covers x 40..80: the outline must stop at the box.
        var frame = LetterboxFrame.For(160, 160, Size);
        var rows = Row(40, 60, 80, 100, 0.9f, 0, 1f);
        var protos = Prototypes(0, 20, 20, 120, 120);

        var detection = Assert.Single(YoloSegDecoder.Decode(rows, 1, 6 + K, protos, ProtoSide, ProtoSide, frame, 0.4));

        Assert.InRange(detection.Outline.Min(point => point.X), 39, 42);
        Assert.InRange(detection.Outline.Max(point => point.X), 78, 81);
    }

    [Fact]
    public void Decode_ClipsBoxesThatReachOutsideTheInput()
    {
        var frame = LetterboxFrame.For(160, 160, Size);
        var rows = Row(-30, -30, 60, 60, 0.9f, 0, 1f);
        var protos = Prototypes(0, 0, 0, 60, 60);

        var detection = Assert.Single(YoloSegDecoder.Decode(rows, 1, 6 + K, protos, ProtoSide, ProtoSide, frame, 0.4));

        Assert.All(detection.Outline, point => Assert.InRange(point.X, 0, 160));
        Assert.All(detection.Outline, point => Assert.InRange(point.Y, 0, 160));
    }

    [Fact]
    public void Decode_RejectsAPrototypeTensorOfTheWrongSize()
    {
        var frame = LetterboxFrame.For(160, 160, Size);

        Assert.Throws<ArgumentException>(() =>
            YoloSegDecoder.Decode(new float[6 + K], 1, 6 + K, new float[10], ProtoSide, ProtoSide, frame, 0.4));
    }
}
