using Microsoft.Win32;

namespace NTech.DataStudio.Desktop.Services;

public interface IFolderPicker
{
    /// <summary>The chosen folder, or null when the user cancels.</summary>
    string? Pick(string title, string? initialFolder = null);
}

public sealed class WpfFolderPicker : IFolderPicker
{
    public string? Pick(string title, string? initialFolder = null)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = initialFolder ?? string.Empty };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
