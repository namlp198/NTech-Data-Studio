using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>The open project as Prepare sees it. Methods may be called from several threads.</summary>
public interface IPrepareStore : IDisposable
{
    string ProjectRoot { get; }
    string SourceFolder { get; }
    string MasterFolder { get; }
    string ThumbsFolder { get; }
    string RejectedFolder { get; }

    IReadOnlyList<SourceImage> LoadSources();

    /// <returns>The new id.</returns>
    long AddSource(SourceImage source);

    void SaveAnalysis(long sourceId, ImageAnalysis analysis);

    void MarkCorrupt(long sourceId, string detail);

    /// <summary>
    /// Replaces the current cleaning result. Corrupt files and files the user
    /// restored are left as they are.
    /// </summary>
    void ReplaceRejections(IReadOnlyList<CleanDecision> decisions);

    void Restore(long sourceId);

    IReadOnlySet<long> SourcesWithMasters();

    /// <summary>Records every master of one source atomically.</summary>
    void AddMasters(long sourceId, IReadOnlyList<MasterFile> masters);

    int CountMasters();

    long SumMasterBytes();

    int CountAnnotations();

    string? ReadNormalizeConfig();

    void WriteNormalizeConfig(string config);

    /// <summary>Forgets all masters and returns their project-relative file paths, including thumbnails.</summary>
    IReadOnlyList<string> ClearMasters();
}
