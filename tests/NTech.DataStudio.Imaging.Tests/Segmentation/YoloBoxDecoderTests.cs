using NTech.DataStudio.Imaging.Segmentation;

namespace NTech.DataStudio.Imaging.Tests.Segmentation;

/// <summary>Decision 0018: the output of a Trainer detect ONNX is read as model.meta.json describes it. No model file needed.</summary>
public class YoloBoxDecoderTests
{
    private const int Size = 160;

    private static float[] Rows(params float[][] rows) => rows.SelectMany(row => row).ToArray();

    private static float[] Row(float x1, float y1, float x2, float y2, float score, int classId) => new[] { x1, y1, x2, y2, score, classId };

    [Fact]
    public void Decode_UndoesTheLetterboxToMasterPixels()
    {
        // 400 x 200 master in a 160 input: scale 0.4, top padding 40. Input box x 40..80, y 60..100 is master x 100..200, y 50..150.
        var frame = LetterboxFrame.For(400, 200, Size);

        var found = YoloBoxDecoder.Decode(Rows(Row(40, 60, 80, 100, 0.9f, 2)), 1, 6, frame, 0.4);

        var box = Assert.Single(found);
        Assert.Equal(2, box.ClassId);
        Assert.Equal(0.9, box.Score, 3);
        Assert.Equal(100, box.Box.X, 3);
        Assert.Equal(50, box.Box.Y, 3);
        Assert.Equal(100, box.Box.Width, 3);
        Assert.Equal(100, box.Box.Height, 3);
    }

    [Fact]
    public void Decode_SkipsPaddingRowsAndLowScores()
    {
        var frame = LetterboxFrame.For(160, 160, Size);
        var rows = Rows(Row(10, 10, 50, 50, 0.9f, 0), Row(10, 10, 50, 50, 0.2f, 0), Row(0, 0, 0, 0, 0, 0));

        var found = YoloBoxDecoder.Decode(rows, 3, 6, frame, 0.4);

        Assert.Equal(0.9, Assert.Single(found).Score, 3);
    }

    [Fact]
    public void Decode_ClipsABoxThatReachesOutsideTheInputAndThePicture()
    {
        // Embedded NMS returns boxes beyond the input (decision 0012); the padding above and below the picture is not picture.
        var frame = LetterboxFrame.For(400, 200, Size);

        var found = YoloBoxDecoder.Decode(Rows(Row(-20, 10, 200, 170, 0.8f, 0)), 1, 6, frame, 0.4);

        var box = Assert.Single(found);
        Assert.Equal(0, box.Box.X, 3);
        Assert.Equal(0, box.Box.Y, 3);
        Assert.Equal(400, box.Box.Right, 3);
        Assert.Equal(200, box.Box.Bottom, 3);
    }

    [Fact]
    public void Decode_DropsABoxThatLiesWhollyInThePadding()
    {
        var frame = LetterboxFrame.For(400, 200, Size);

        // Rows 0..40 of the input are padding for a 400 x 200 picture.
        var found = YoloBoxDecoder.Decode(Rows(Row(20, 5, 60, 30, 0.9f, 0)), 1, 6, frame, 0.4);

        Assert.Empty(found);
    }

    [Fact]
    public void Decode_RefusesRowsThatAreNotSixValues()
    {
        var frame = LetterboxFrame.For(160, 160, Size);

        Assert.Throws<ArgumentException>(() => YoloBoxDecoder.Decode(new float[16], 1, 16, frame, 0.4));
    }
}
