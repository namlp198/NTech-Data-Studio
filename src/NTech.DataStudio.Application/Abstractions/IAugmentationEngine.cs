using NTech.DataStudio.Domain.Augment;

namespace NTech.DataStudio.Application.Abstractions;

public enum RenderFormat
{
    Png,
    Jpeg
}

/// <summary>An encoded picture and its size.</summary>
public sealed record RenderedImage(byte[] Data, int Width, int Height);

/// <summary>Pixel work for a <see cref="RenderPlan"/>. Safe to call from several threads, and the same plan always gives the same pixels.</summary>
public interface IAugmentationEngine
{
    /// <exception cref="Prepare.ImageReadException">The master cannot be read.</exception>
    RenderedImage Render(string masterPath, RenderPlan plan, RenderFormat format, int jpegQuality = 95);
}
