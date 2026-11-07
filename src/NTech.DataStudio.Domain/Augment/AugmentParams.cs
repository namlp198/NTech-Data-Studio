using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Augment;

/// <summary>A rectangle as fractions of the output image: 0..1 across and down.</summary>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height);

/// <summary>
/// The dice rolled for one augmented copy. Ranges live in <c>AugmentConfig</c>;
/// this is a single outcome, so the same value always reproduces the same picture.
/// </summary>
public sealed record AugmentParams
{
    public static AugmentParams None { get; } = new();

    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    /// <summary>Clockwise quarter turns, 0 to 3.</summary>
    public int QuarterTurns { get; init; }

    public double RotateDegrees { get; init; }

    /// <summary>1 means unchanged; below 1 zooms out and leaves a border, above 1 zooms in and crops.</summary>
    public double Scale { get; init; } = 1;

    public double ShearXDegrees { get; init; }
    public double ShearYDegrees { get; init; }

    /// <summary>Shift as a fraction of the output size.</summary>
    public double TranslateX { get; init; }

    public double TranslateY { get; init; }

    /// <summary>Added to every channel, in 0..255 units.</summary>
    public double Brightness { get; init; }

    /// <summary>Multiplier on every channel; 1 means unchanged.</summary>
    public double Exposure { get; init; } = 1;

    public double Contrast { get; init; } = 1;
    public double Gamma { get; init; } = 1;
    public double HueShiftDegrees { get; init; }
    public double SaturationScale { get; init; } = 1;
    public bool Grayscale { get; init; }
    public double BlurSigma { get; init; }
    public double MotionBlurLength { get; init; }
    public double MotionBlurAngleDegrees { get; init; }

    /// <summary>Standard deviation of Gaussian noise in 0..255 units.</summary>
    public double NoiseSigma { get; init; }

    /// <summary>Seed for the noise, so noise is as reproducible as everything else.</summary>
    public long NoiseSeed { get; init; }

    /// <summary>JPEG quality to recompress at, or null for none.</summary>
    public int? JpegQuality { get; init; }

    public IReadOnlyList<NormalizedRect> Cutouts { get; init; } = Array.Empty<NormalizedRect>();

    public bool ChangesGeometry =>
        FlipHorizontal || FlipVertical || QuarterTurns != 0
        || RotateDegrees != 0 || Scale != 1 || ShearXDegrees != 0 || ShearYDegrees != 0
        || TranslateX != 0 || TranslateY != 0;

    /// <summary>
    /// The geometric part as one matrix, for a frame of the given size. Flips and
    /// quarter turns come first and can swap width and height; the small rotation,
    /// scale, shear and shift then act around the centre of what is left.
    /// </summary>
    public Affine2D BuildGeometry(int frameWidth, int frameHeight, out int outputWidth, out int outputHeight)
    {
        double width = frameWidth;
        double height = frameHeight;
        var matrix = Affine2D.Identity;

        if (FlipHorizontal) matrix = matrix.Then(Affine2D.FlipHorizontal(width));
        if (FlipVertical) matrix = matrix.Then(Affine2D.FlipVertical(height));
        for (var turn = 0; turn < ((QuarterTurns % 4) + 4) % 4; turn++)
        {
            matrix = matrix.Then(Affine2D.QuarterTurnClockwise(height));
            (width, height) = (height, width);
        }

        var small = Affine2D.Scale(Scale, Scale)
            .Then(Affine2D.Shear(ShearXDegrees * Math.PI / 180, ShearYDegrees * Math.PI / 180))
            .Then(Affine2D.Rotation(RotateDegrees * Math.PI / 180));
        matrix = matrix
            .Then(Affine2D.About(small, width / 2, height / 2))
            .Then(Affine2D.Translation(TranslateX * width, TranslateY * height));

        outputWidth = (int)width;
        outputHeight = (int)height;
        return matrix;
    }
}
