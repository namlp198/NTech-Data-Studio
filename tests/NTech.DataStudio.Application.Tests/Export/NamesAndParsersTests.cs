using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Application.Import;
using NTech.DataStudio.Domain.Annotations;

namespace NTech.DataStudio.Application.Tests.Export;

public class NamesAndParsersTests
{
    [Theory]
    [InlineData("Đầu nối xanh", "Dau_noi_xanh")]
    [InlineData("000012_ảnh gốc (1)", "000012_anh_goc_1")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("---", "---")]
    [InlineData("   ", "x")]
    [InlineData("", "x")]
    public void Slug_KeepsOnlyAsciiLettersDigitsDashAndUnderscore(string text, string expected)
    {
        Assert.Equal(expected, ExportNames.Slug(text));
    }

    [Fact]
    public void Unique_AddsASuffixUntilTheNameIsFree()
    {
        var taken = new HashSet<string>();

        Assert.Equal("a", ExportNames.Unique("a", taken));
        Assert.Equal("a_2", ExportNames.Unique("a", taken));
        Assert.Equal("a_3", ExportNames.Unique("a", taken));
    }

    [Fact]
    public void Yolo_ADetectLineBecomesABoxInPixels()
    {
        var labels = YoloLabelParser.Parse("2 0.5 0.5 0.25 0.5\n\n", YoloLabelKind.Detect, 200, 100);

        var label = Assert.Single(labels);
        Assert.Equal(2, label.ClassKey);
        Assert.Equal(ShapeKind.Box, label.Kind);
        Assert.Equal(75, label.Points[0].X);
        Assert.Equal(25, label.Points[0].Y);
        Assert.Equal(125, label.Points[2].X);
        Assert.Equal(75, label.Points[2].Y);
    }

    [Theory]
    [InlineData("x 0.5 0.5 0.1 0.1", YoloLabelKind.Detect, "Dòng 1")]
    [InlineData("0 0.5 0.5 0.1", YoloLabelKind.Detect, "4 số")]
    [InlineData("0 0.1 0.1 0.2 0.2 0.3", YoloLabelKind.Segment, "polygon")]
    [InlineData("0 0.1 0.1 0.2 0.2 0.3 0.3", YoloLabelKind.Obb, "8 số")]
    [InlineData("0 0.1 abc 0.1 0.1", YoloLabelKind.Detect, "abc")]
    public void Yolo_ABadLineIsReportedWithItsNumber(string text, YoloLabelKind kind, string expected)
    {
        var error = Assert.Throws<LabelFormatException>(() => YoloLabelParser.Parse("0 0.5 0.5 0.1 0.1\n" + text, kind == YoloLabelKind.Detect ? YoloLabelKind.Detect : kind, 100, 100));

        Assert.Contains(expected.StartsWith("Dòng") ? "Dòng 2" : expected, error.Message);
    }

    [Theory]
    [InlineData("names:\n  0: a\n  1: b\n", "a,b")]
    [InlineData("names:\n  - a\n  - b\n", "a,b")]
    [InlineData("names: [a, b]\n", "a,b")]
    [InlineData("train: x\nnames:\n  0: \"q: r\"\n  1: 'it''s'\nnc: 2\n", "q: r,it's")]
    [InlineData("train: x\n", "")]
    public void YoloNames_AreReadFromMapListAndInlineForms(string yaml, string expected)
    {
        var names = YoloLabelParser.ParseNames(yaml);

        Assert.Equal(expected, string.Join(",", names.Values));
    }

    [Fact]
    public void Coco_ANonCocoFileIsReported()
    {
        Assert.Throws<LabelFormatException>(() => CocoLabelParser.Parse("{\"hello\": 1}"));
        Assert.Throws<LabelFormatException>(() => CocoLabelParser.Parse("not json"));
    }
}
