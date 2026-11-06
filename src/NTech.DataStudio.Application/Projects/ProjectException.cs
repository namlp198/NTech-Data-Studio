namespace NTech.DataStudio.Application.Projects;

/// <summary>A project operation failed; <see cref="Exception.Message"/> is shown to the user.</summary>
public sealed class ProjectException : Exception
{
    public ProjectException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
