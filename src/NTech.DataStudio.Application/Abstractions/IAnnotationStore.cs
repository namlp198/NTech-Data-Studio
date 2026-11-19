using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Domain.Classes;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>
/// The open project as the annotation screen sees it. Every write is saved
/// before the call returns, so a crash loses nothing that was shown as done.
/// Methods may be called from several threads.
/// </summary>
public interface IAnnotationStore : IDisposable
{
    string ProjectRoot { get; }

    /// <summary>Active masters in id order.</summary>
    IReadOnlyList<AnnotateImage> LoadImages();

    /// <summary>When on, only images with <see cref="AnnotateImage.InDataset"/> go into a dataset version; when off, every labeled image does.</summary>
    bool LoadSelectionOnly();

    void SetSelectionOnly(bool value);

    void SetInDataset(long imageId, bool value);

    /// <summary>Sets the flag of many images in one transaction (select all, clear, invert).</summary>
    void SetInDatasetMany(IReadOnlyCollection<long> imageIds, bool value);

    /// <summary>
    /// Takes images out of the project: marks them rejected (no file is touched, labels stay) and remembers their status. Decision 0005 and 0022.
    /// </summary>
    void DeleteImages(IReadOnlyCollection<long> imageIds);

    /// <summary>Ids of the images deleted from Annotate, which <see cref="RestoreImages"/> can bring back.</summary>
    IReadOnlyList<long> LoadDeletedIds();

    /// <summary>Puts deleted images back with the status they had.</summary>
    void RestoreImages(IReadOnlyCollection<long> imageIds);

    ImageContent LoadContent(long imageId);

    /// <exception cref="InvalidOperationException">The image no longer exists.</exception>
    void Apply(StoreChange change);

    IReadOnlyList<LabelClass> LoadClasses();

    LabelClass AddClass(string name, string color, int? hotkey);

    void UpdateClass(LabelClass labelClass);

    ClassUsage GetClassUsage(long classId);

    /// <summary>Annotations plus image tags per class.</summary>
    IReadOnlyDictionary<long, int> LoadClassCounts();

    /// <summary>Moves every label and tag of <paramref name="fromId"/> to <paramref name="intoId"/> and removes the first class.</summary>
    void MergeClasses(long fromId, long intoId);

    /// <summary>Removes the class with its labels and tags; images left without labels go back to New.</summary>
    void DeleteClass(long classId);
}
