namespace NTech.DataStudio.Desktop.Shell;

/// <summary>A view that holds resources, such as a database connection, only while it is on screen.</summary>
public interface IActivatable
{
    Task ActivateAsync();

    void Deactivate();
}
