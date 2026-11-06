namespace NTech.DataStudio.Storage.Files;

/// <summary>The self-contained project folder: copy it and it opens on another machine.</summary>
public sealed class ProjectFolderLayout
{
    public const string DatabaseFileName = "project.db";

    public ProjectFolderLayout(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }
    public string DatabasePath => Path.Combine(RootPath, DatabaseFileName);
    public string SourcePath => Path.Combine(RootPath, "source");
    public string MasterPath => Path.Combine(RootPath, "master");
    public string ThumbsPath => Path.Combine(RootPath, "thumbs");
    public string RejectedPath => Path.Combine(RootPath, "rejected");
    public string VersionsPath => Path.Combine(RootPath, "versions");
    public string ExportsPath => Path.Combine(RootPath, "exports");

    /// <summary>Also repairs a project whose empty folders were lost, for example by version control.</summary>
    public void EnsureDirectories()
    {
        foreach (var path in new[] { RootPath, SourcePath, MasterPath, ThumbsPath, RejectedPath, VersionsPath, ExportsPath })
            Directory.CreateDirectory(path);
    }
}
