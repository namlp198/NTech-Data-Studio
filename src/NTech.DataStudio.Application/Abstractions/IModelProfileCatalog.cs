using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Abstractions;

public interface IModelProfileCatalog
{
    /// <summary>Every profile, sorted by id.</summary>
    /// <exception cref="InvalidDataException">A profile file is malformed; the message names the file.</exception>
    IReadOnlyList<ModelProfile> LoadAll();
}
