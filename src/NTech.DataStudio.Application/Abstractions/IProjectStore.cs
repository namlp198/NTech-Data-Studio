using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Abstractions;

public interface IProjectStore
{
    /// <exception cref="ProjectException">The folder is not empty or cannot be written.</exception>
    Task<ProjectSummary> CreateAsync(string folder, string name, TaskKind? targetTask = null, CancellationToken cancellationToken = default);

    /// <summary>Sets or clears the project's target task. Labels are not touched.</summary>
    /// <exception cref="ProjectException">The project cannot be written.</exception>
    Task<ProjectSummary> SetTargetTaskAsync(string folder, TaskKind? targetTask, CancellationToken cancellationToken = default);

    /// <exception cref="ProjectException">The folder holds no readable project of a supported version.</exception>
    Task<ProjectSummary> OpenAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Opens an already created project for Prepare work. Dispose when the operation ends.</summary>
    IPrepareStore OpenPrepareStore(string folder);

    /// <summary>Opens an already created project for annotation. Dispose when the screen is left.</summary>
    IAnnotationStore OpenAnnotationStore(string folder);

    /// <summary>Opens an already created project for building versions. Dispose when the operation ends.</summary>
    IDatasetStore OpenDatasetStore(string folder);
}
