using System.Diagnostics;

namespace NTech.DataStudio.Desktop.Services;

public interface IFolderOpener
{
    /// <summary>Shows the folder in the file explorer.</summary>
    void Open(string folder);
}

public sealed class ExplorerFolderOpener : IFolderOpener
{
    public void Open(string folder)
    {
        if (!Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = true });
    }
}
