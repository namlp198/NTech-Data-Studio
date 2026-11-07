using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Application.Abstractions;

/// <summary>
/// The open project as the dataset screen sees it. Opened for one operation and
/// disposed after it; methods may be called from several threads.
/// </summary>
public interface IDatasetStore : IDisposable
{
    string ProjectRoot { get; }

    /// <summary>Active masters in id order, with the labels they carry.</summary>
    IReadOnlyList<DatasetImage> LoadImages();

    IReadOnlyList<LabelClass> LoadClasses();

    IReadOnlyList<Annotation> LoadAnnotations(long imageId);

    IReadOnlyList<DatasetVersion> LoadVersions();

    IReadOnlyList<SplitAssignment> LoadAssignments(long versionId);

    /// <summary>Saves the version and its split in one transaction.</summary>
    /// <exception cref="InvalidOperationException">A version with this name exists, or an image is gone.</exception>
    DatasetVersion CreateVersion(string name, DatasetConfig config, IReadOnlyList<SplitAssignment> assignments);

    void DeleteVersion(long versionId);
}
