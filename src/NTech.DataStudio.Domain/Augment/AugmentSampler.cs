using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Domain.Augment;

/// <summary>Rolls the dice for one copy of one image, from the version seed alone.</summary>
public static class AugmentSampler
{
    /// <summary>
    /// Copy 0 is the original and is never touched. Copies 1 and up draw every
    /// value in a fixed order whether its option is on or not, so switching one
    /// option never changes what the others rolled.
    /// </summary>
    public static AugmentParams Sample(AugmentConfig config, long seed, long imageId, int copyIndex)
    {
        if (copyIndex <= 0 || !config.IsEnabled)
            return AugmentParams.None;

        var random = DeterministicRandom.From(seed, imageId, copyIndex);

        var flipHorizontal = random.NextBool() && config.FlipHorizontal;
        var flipVertical = random.NextBool() && config.FlipVertical;

        var turnPick = random.NextInt(4);
        var quarterTurns = 0;
        var turnOptions = new List<int>();
        if (config.Rotate90Clockwise) turnOptions.Add(1);
        if (config.Rotate180) turnOptions.Add(2);
        if (config.Rotate90CounterClockwise) turnOptions.Add(3);
        // None is one of the choices, so a copy is not forced to turn.
        if (turnOptions.Count > 0)
        {
            var slot = turnPick % (turnOptions.Count + 1);
            quarterTurns = slot == 0 ? 0 : turnOptions[slot - 1];
        }

        var rotate = random.NextSigned(config.RotateDegrees);
        var scale = 1 + random.NextSigned(config.ScalePercent / 100);
        var shearX = random.NextSigned(config.ShearDegrees);
        var shearY = random.NextSigned(config.ShearDegrees);
        var translateX = random.NextSigned(config.TranslatePercent / 100);
        var translateY = random.NextSigned(config.TranslatePercent / 100);

        var brightness = random.NextSigned(config.BrightnessPercent / 100 * 255);
        var exposure = 1 + random.NextSigned(config.ExposurePercent / 100);
        var contrast = 1 + random.NextSigned(config.ContrastPercent / 100);
        var gamma = 1 + random.NextSigned(config.GammaPercent / 100);
        var hue = random.NextSigned(config.HueDegrees);
        var saturation = 1 + random.NextSigned(config.SaturationPercent / 100);
        var grayscale = random.NextDouble() * 100 < config.GrayscalePercent;

        var blur = random.NextDouble() * config.BlurPx;
        var motionLength = random.NextDouble() * config.MotionBlurPx;
        var motionAngle = random.NextDouble() * 180;
        var noise = random.NextDouble() * config.NoisePercent / 100 * 255;
        var noiseSeed = (long)(random.NextUInt64() >> 1);
        var jpegDraw = random.NextDouble();

        var cutouts = new List<NormalizedRect>();
        var cutoutSize = config.CutoutSizePercent / 100.0;
        for (var index = 0; index < config.CutoutCount; index++)
        {
            var centerX = random.NextDouble();
            var centerY = random.NextDouble();
            cutouts.Add(new NormalizedRect(
                centerX - cutoutSize / 2,
                centerY - cutoutSize / 2,
                cutoutSize,
                cutoutSize));
        }

        return new AugmentParams
        {
            FlipHorizontal = flipHorizontal,
            FlipVertical = flipVertical,
            QuarterTurns = quarterTurns,
            RotateDegrees = rotate,
            Scale = scale,
            ShearXDegrees = shearX,
            ShearYDegrees = shearY,
            TranslateX = translateX,
            TranslateY = translateY,
            Brightness = brightness,
            Exposure = exposure,
            Contrast = contrast,
            Gamma = gamma,
            HueShiftDegrees = hue,
            SaturationScale = saturation,
            Grayscale = grayscale,
            BlurSigma = blur,
            MotionBlurLength = motionLength,
            MotionBlurAngleDegrees = motionAngle,
            NoiseSigma = noise,
            NoiseSeed = noiseSeed,
            JpegQuality = config.JpegQualityMin < 100
                ? (int)Math.Round(config.JpegQualityMin + jpegDraw * (100 - config.JpegQualityMin))
                : null,
            Cutouts = cutouts
        };
    }
}
