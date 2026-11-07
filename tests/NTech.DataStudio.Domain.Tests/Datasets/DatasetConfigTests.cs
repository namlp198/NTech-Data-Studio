using System.Text.Json;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Domain.Tests.Datasets;

public class DatasetConfigTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Assert.Null(new DatasetConfig().Validate());
    }

    [Theory]
    [InlineData(70, 20, 20)]
    [InlineData(60, 20, 10)]
    [InlineData(100, 10, -10)]
    [InlineData(0, 50, 50)]
    public void Split_RejectsRatiosThatAreNotAValidHundred(int train, int val, int test)
    {
        var split = new SplitConfig { TrainPercent = train, ValPercent = val, TestPercent = test };

        Assert.NotNull(split.Validate());
    }

    [Fact]
    public void Split_AllowsNoTestSet()
    {
        Assert.Null(new SplitConfig { TrainPercent = 80, ValPercent = 20, TestPercent = 0 }.Validate());
    }

    [Fact]
    public void Preprocess_RejectsAClassMappedOntoAClassThatIsAlsoMapped()
    {
        var config = new PreprocessConfig
        {
            ClassMap = new[] { new ClassMapEntry(1, 2), new ClassMapEntry(2, 3) }
        };

        Assert.NotNull(config.Validate());
    }

    [Fact]
    public void Preprocess_RejectsAClassMappedToItselfOrListedTwice()
    {
        Assert.NotNull(new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(1, 1) } }.Validate());
        Assert.NotNull(new PreprocessConfig { ClassMap = new[] { new ClassMapEntry(1, 2), new ClassMapEntry(1, null) } }.Validate());
    }

    [Fact]
    public void Preprocess_AcceptsMergingAndDropping()
    {
        var config = new PreprocessConfig
        {
            ClassMap = new[] { new ClassMapEntry(2, 1), new ClassMapEntry(3, null) }
        };

        Assert.Null(config.Validate());
    }

    [Fact]
    public void Preprocess_RejectsAnAbsurdTargetSize()
    {
        Assert.NotNull(new PreprocessConfig { Resize = ResizeMode.Fit, Width = 4, Height = 640 }.Validate());
        Assert.Null(new PreprocessConfig { Resize = ResizeMode.None, Width = 4, Height = 640 }.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Augment_RejectsAMultiplierOutsideOneToTen(int multiplier)
    {
        Assert.NotNull(new AugmentConfig { Multiplier = multiplier }.Validate());
    }

    [Fact]
    public void Augment_RejectsRangesThatWouldBreakAnImage()
    {
        Assert.NotNull(new AugmentConfig { Multiplier = 2, ScalePercent = 95 }.Validate());
        Assert.NotNull(new AugmentConfig { Multiplier = 2, ContrastPercent = 100 }.Validate());
        Assert.NotNull(new AugmentConfig { Multiplier = 2, ShearDegrees = 70 }.Validate());
        Assert.NotNull(new AugmentConfig { Multiplier = 2, JpegQualityMin = 5 }.Validate());
    }

    [Fact]
    public void Json_RoundTripsEveryPart()
    {
        var config = new DatasetConfig
        {
            Seed = 7,
            Split = new SplitConfig { TrainPercent = 80, ValPercent = 10, TestPercent = 10, GroupByFolder = false },
            Preprocess = new PreprocessConfig
            {
                Resize = ResizeMode.Fit,
                Width = 800,
                Height = 600,
                ClassMap = new[] { new ClassMapEntry(2, 1), new ClassMapEntry(3, null) }
            },
            Augment = new AugmentConfig { Multiplier = 3, FlipHorizontal = true, BlurPx = 1.5 }
        };

        var restored = DatasetConfig.FromJson(
            DatasetConfig.ToJson(config.Preprocess),
            DatasetConfig.ToJson(config.Split),
            DatasetConfig.ToJson(config.Augment),
            config.Seed);

        Assert.Equal(config.Seed, restored.Seed);
        Assert.Equal(config.Split, restored.Split);
        Assert.Equal(config.Augment, restored.Augment);
        Assert.Equal(ResizeMode.Fit, restored.Preprocess.Resize);
        Assert.Equal(config.Preprocess.ClassMap, restored.Preprocess.ClassMap);
    }

    [Fact]
    public void Json_WritesEnumsAsNamesSoAFileStaysReadable()
    {
        Assert.Contains("\"Fit\"", DatasetConfig.ToJson(new PreprocessConfig { Resize = ResizeMode.Fit }));
    }

    [Fact]
    public void Json_AnOlderVersionWithMissingFieldsGetsDefaults()
    {
        var restored = DatasetConfig.FromJson("{}", "{\"TrainPercent\":60,\"ValPercent\":30,\"TestPercent\":10}", "{}", 1);

        Assert.Equal(60, restored.Split.TrainPercent);
        Assert.True(restored.Split.Stratify);
        Assert.Equal(1, restored.Augment.Multiplier);
        Assert.Equal(ResizeMode.None, restored.Preprocess.Resize);
    }

    [Fact]
    public void Json_TextThatIsNotAConfigFailsLoudly()
    {
        Assert.Throws<JsonException>(() => DatasetConfig.FromJson("null", "{}", "{}", 1));
        Assert.Throws<JsonException>(() => DatasetConfig.FromJson("not json", "{}", "{}", 1));
    }
}
