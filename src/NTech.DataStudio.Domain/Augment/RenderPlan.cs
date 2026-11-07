using NTech.DataStudio.Domain.Datasets;
using NTech.DataStudio.Domain.Geometry;

namespace NTech.DataStudio.Domain.Augment;

/// <summary>
/// One output picture, fully decided: its size, the single matrix from master
/// pixels to output pixels, and the photometric changes. The pixel engine and
/// the label transformer both read this, so image and labels cannot disagree.
/// </summary>
public sealed record RenderPlan(
    int OutputWidth,
    int OutputHeight,
    Affine2D Transform,
    bool Grayscale,
    AugmentParams Augment)
{
    /// <summary>Grey used for letterbox bars and for areas a rotation uncovers.</summary>
    public const byte FillValue = 114;

    /// <summary>Grey of a cutout box.</summary>
    public const byte CutoutValue = 128;

    public bool IsIdentity => Transform == Affine2D.Identity && !Grayscale && !Augment.ChangesGeometry;

    /// <summary>
    /// The same picture rendered no larger than <paramref name="maxEdge"/>, for previews.
    /// Labels transformed with the result line up with the smaller picture.
    /// </summary>
    public RenderPlan ScaledTo(int maxEdge)
    {
        var longEdge = Math.Max(OutputWidth, OutputHeight);
        if (longEdge <= maxEdge) return this;

        var factor = (double)maxEdge / longEdge;
        var width = Math.Max(1, (int)Math.Round(OutputWidth * factor));
        var height = Math.Max(1, (int)Math.Round(OutputHeight * factor));
        return this with
        {
            OutputWidth = width,
            OutputHeight = height,
            Transform = Transform.Then(Affine2D.Scale((double)width / OutputWidth, (double)height / OutputHeight))
        };
    }
}

public static class RenderPlanner
{
    /// <summary>
    /// Preprocessing first, then augmentation on the preprocessed frame, folded
    /// into one matrix so the master is resampled once.
    /// </summary>
    public static RenderPlan Plan(int masterWidth, int masterHeight, PreprocessConfig preprocess, AugmentParams? augment)
    {
        var (width, height, transform) = Preprocess(masterWidth, masterHeight, preprocess);
        var parameters = augment ?? AugmentParams.None;
        var geometry = parameters.BuildGeometry(width, height, out var outputWidth, out var outputHeight);
        return new RenderPlan(
            outputWidth,
            outputHeight,
            transform.Then(geometry),
            preprocess.Grayscale || parameters.Grayscale,
            parameters);
    }

    private static (int Width, int Height, Affine2D Transform) Preprocess(int width, int height, PreprocessConfig config)
    {
        if (config.Resize == ResizeMode.None)
            return (width, height, Affine2D.Identity);

        var targetWidth = (double)config.Width;
        var targetHeight = (double)config.Height;
        var scaleX = targetWidth / width;
        var scaleY = targetHeight / height;

        if (config.Resize == ResizeMode.Stretch)
            return (config.Width, config.Height, Affine2D.Scale(scaleX, scaleY));

        var scale = config.Resize == ResizeMode.Fit ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY);
        var offsetX = (targetWidth - width * scale) / 2;
        var offsetY = (targetHeight - height * scale) / 2;
        return (config.Width, config.Height, Affine2D.Scale(scale, scale).Then(Affine2D.Translation(offsetX, offsetY)));
    }
}
