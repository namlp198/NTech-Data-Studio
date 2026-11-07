using System.Text.Json;
using System.Text.Json.Serialization;

namespace NTech.DataStudio.Domain.Datasets;

/// <summary>How a version divides images. Percentages are whole numbers that add up to 100.</summary>
public sealed record SplitConfig
{
    public int TrainPercent { get; init; } = 70;
    public int ValPercent { get; init; } = 20;
    public int TestPercent { get; init; } = 10;

    /// <summary>Spread every class over train and val, as far as its image count allows.</summary>
    public bool Stratify { get; init; } = true;

    /// <summary>Keep images of one folder (session, product) in one split.</summary>
    public bool GroupByFolder { get; init; } = true;

    /// <summary>Images still in New: off by default, because an unlabeled image reads as "no object" to a trainer.</summary>
    public bool IncludeUnlabeled { get; init; }

    /// <summary>Null when valid, otherwise what is wrong, in words for the user.</summary>
    public string? Validate()
    {
        if (TrainPercent < 0 || ValPercent < 0 || TestPercent < 0)
            return "Tỉ lệ chia không được âm.";
        if (TrainPercent + ValPercent + TestPercent != 100)
            return $"Tỉ lệ train + val + test phải bằng 100 (hiện là {TrainPercent + ValPercent + TestPercent}).";
        if (TrainPercent == 0)
            return "Tập train không được để 0%.";
        return null;
    }
}

public enum ResizeMode
{
    /// <summary>Keep the master size and let the training framework resize.</summary>
    None,

    /// <summary>Squash to the target size, ignoring the aspect ratio.</summary>
    Stretch,

    /// <summary>Scale to fit inside the target and pad the rest (letterbox).</summary>
    Fit,

    /// <summary>Scale to cover the target and crop what overhangs.</summary>
    Fill
}

/// <summary>Sends a class to another one, or drops it (<see cref="TargetClassId"/> null).</summary>
public sealed record ClassMapEntry(long ClassId, long? TargetClassId);

/// <summary>What happens to every image of every split.</summary>
public sealed record PreprocessConfig
{
    public ResizeMode Resize { get; init; } = ResizeMode.None;
    public int Width { get; init; } = 640;
    public int Height { get; init; } = 640;
    public bool Grayscale { get; init; }

    /// <summary>Only classes that change; a class not listed is kept as it is.</summary>
    public IReadOnlyList<ClassMapEntry> ClassMap { get; init; } = Array.Empty<ClassMapEntry>();

    /// <summary>Share of images without any label (Null, or emptied by dropping classes) to leave out.</summary>
    public int DropNullPercent { get; init; }

    public string? Validate()
    {
        if (Resize != ResizeMode.None && (Width < 16 || Height < 16 || Width > 16384 || Height > 16384))
            return "Kích thước đích phải nằm trong 16 đến 16384 px.";
        if (DropNullPercent is < 0 or > 100)
            return "Tỉ lệ bỏ ảnh Null phải nằm trong 0 đến 100.";

        var mapped = ClassMap.Select(entry => entry.ClassId).ToHashSet();
        if (mapped.Count != ClassMap.Count)
            return "Một class xuất hiện hai lần trong bảng gộp class.";
        foreach (var entry in ClassMap)
        {
            if (entry.TargetClassId is { } target && (target == entry.ClassId || mapped.Contains(target)))
                return "Class đích của việc gộp không được chính nó bị gộp hay bỏ.";
        }

        return null;
    }
}

/// <summary>Random augmentation for the train split. Every range is symmetric: 5 means -5 to +5.</summary>
public sealed record AugmentConfig
{
    public const int MaxMultiplier = 10;

    /// <summary>Output images per train image, the original included. 1 means no augmentation.</summary>
    public int Multiplier { get; init; } = 1;

    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }
    public bool Rotate90Clockwise { get; init; }
    public bool Rotate90CounterClockwise { get; init; }
    public bool Rotate180 { get; init; }

    public double RotateDegrees { get; init; }
    public double ScalePercent { get; init; }
    public double TranslatePercent { get; init; }
    public double ShearDegrees { get; init; }

    public double BrightnessPercent { get; init; }
    public double ExposurePercent { get; init; }
    public double ContrastPercent { get; init; }
    public double GammaPercent { get; init; }

    public double HueDegrees { get; init; }
    public double SaturationPercent { get; init; }
    public int GrayscalePercent { get; init; }

    public double BlurPx { get; init; }
    public double MotionBlurPx { get; init; }
    public double NoisePercent { get; init; }

    /// <summary>Lowest JPEG quality to recompress at; 100 means no artifacts.</summary>
    public int JpegQualityMin { get; init; } = 100;

    public int CutoutCount { get; init; }
    public int CutoutSizePercent { get; init; } = 10;

    /// <summary>A label covered by cutouts at least this much is dropped; 0 keeps every label.</summary>
    public int CutoutDropLabelPercent { get; init; }

    /// <summary>A label with less than this share of its area left inside the image is dropped.</summary>
    public int MinVisiblePercent { get; init; } = 30;

    public bool IsEnabled => Multiplier > 1;

    public string? Validate()
    {
        if (Multiplier is < 1 or > MaxMultiplier)
            return $"Hệ số nhân phải nằm trong 1 đến {MaxMultiplier}.";
        if (RotateDegrees is < 0 or > 180 || ShearDegrees is < 0 or >= 60)
            return "Góc xoay phải trong 0 đến 180°, góc shear trong 0 đến 60°.";
        if (ScalePercent is < 0 or >= 90)
            return "Biên độ scale phải trong 0 đến 90%.";
        if (TranslatePercent is < 0 or > 50)
            return "Biên độ dịch chuyển phải trong 0 đến 50%.";
        if (BrightnessPercent is < 0 or > 100 || ExposurePercent is < 0 or >= 100
            || ContrastPercent is < 0 or >= 100 || GammaPercent is < 0 or >= 100)
            return "Biên độ sáng, phơi sáng, tương phản, gamma phải trong 0 đến 100% (phơi sáng, tương phản, gamma dưới 100%).";
        if (HueDegrees is < 0 or > 180 || SaturationPercent is < 0 or >= 100)
            return "Hue phải trong 0 đến 180°, bão hòa trong 0 đến 100%.";
        if (GrayscalePercent is < 0 or > 100)
            return "Tỉ lệ ảnh xám phải trong 0 đến 100%.";
        if (BlurPx < 0 || MotionBlurPx < 0 || NoisePercent is < 0 or > 50)
            return "Độ mờ không âm; nhiễu phải trong 0 đến 50%.";
        if (JpegQualityMin is < 10 or > 100)
            return "Chất lượng JPEG thấp nhất phải trong 10 đến 100.";
        if (CutoutCount is < 0 or > 20 || CutoutSizePercent is < 1 or > 60)
            return "Cutout: 0 đến 20 ô, kích thước 1 đến 60% cạnh ảnh.";
        if (CutoutDropLabelPercent is < 0 or > 100 || MinVisiblePercent is < 1 or > 100)
            return "Các ngưỡng bỏ nhãn phải trong 0 đến 100% (độ hiện tối thiểu từ 1).";
        return null;
    }
}

/// <summary>Everything that defines a version besides which images exist. Same config and seed always give the same split and the same pictures.</summary>
public sealed record DatasetConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public int Seed { get; init; } = 42;
    public SplitConfig Split { get; init; } = new();
    public PreprocessConfig Preprocess { get; init; } = new();
    public AugmentConfig Augment { get; init; } = new();

    public string? Validate() => Split.Validate() ?? Preprocess.Validate() ?? Augment.Validate();

    public static string ToJson(SplitConfig value) => JsonSerializer.Serialize(value, Json);
    public static string ToJson(PreprocessConfig value) => JsonSerializer.Serialize(value, Json);
    public static string ToJson(AugmentConfig value) => JsonSerializer.Serialize(value, Json);

    /// <exception cref="JsonException">The stored text is not a config of a version this app wrote.</exception>
    public static DatasetConfig FromJson(string preprocessJson, string splitJson, string augmentJson, int seed) => new()
    {
        Seed = seed,
        Split = JsonSerializer.Deserialize<SplitConfig>(splitJson, Json) ?? throw new JsonException("Config chia tập rỗng."),
        Preprocess = JsonSerializer.Deserialize<PreprocessConfig>(preprocessJson, Json) ?? throw new JsonException("Config preprocessing rỗng."),
        Augment = JsonSerializer.Deserialize<AugmentConfig>(augmentJson, Json) ?? throw new JsonException("Config augmentation rỗng.")
    };
}
