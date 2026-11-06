using NTech.DataStudio.Application.Prepare;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>All pixel work. Every method is safe to call from several threads.</summary>
public interface IImageCodec
{
    /// <summary>Reads only as much of the file as the header needs, when the format allows it.</summary>
    /// <exception cref="ImageReadException">Not a readable image, or visibly truncated.</exception>
    ImageHeader ReadHeader(string path);

    /// <summary>Decodes once to compute hashes and quality, and writes the thumbnail.</summary>
    /// <exception cref="ImageReadException">The pixels cannot be decoded.</exception>
    ImageAnalysis Analyze(string path, string thumbnailPath);

    /// <summary>Writes the master file(s) for one source. A crash never leaves a half-written master.</summary>
    /// <exception cref="ImageReadException">The pixels cannot be decoded.</exception>
    IReadOnlyList<MasterFile> Normalize(
        string sourcePath,
        NormalizeOptions options,
        string masterFolder,
        string thumbsFolder,
        string fileStem);

    /// <exception cref="ImageReadException">The pixels cannot be decoded.</exception>
    NormalizePreview RenderPreview(string sourcePath, NormalizeOptions options, int maxEdge);
}
