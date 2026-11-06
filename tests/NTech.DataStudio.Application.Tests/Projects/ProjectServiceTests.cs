using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Storage.Sqlite;

namespace NTech.DataStudio.Application.Tests.Projects;

public class ProjectServiceTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectService _service = new(new SqliteProjectStore());

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Create_PutsTheProjectInASubfolderNamedAfterIt()
    {
        var summary = await _service.CreateAsync(_temp.Path, "  harness  ");

        Assert.Equal("harness", summary.Name);
        Assert.Equal(Path.Combine(_temp.Path, "harness"), summary.RootPath);
        Assert.Same(summary, _service.Current);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("name.")]
    public async Task Create_RejectsNamesUnusableAsAFolder(string name)
    {
        await Assert.ThrowsAsync<ProjectException>(() => _service.CreateAsync(_temp.Path, name));
        Assert.Null(_service.Current);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path));
    }

    [Fact]
    public async Task Create_RejectsMissingParentFolder()
    {
        await Assert.ThrowsAsync<ProjectException>(() => _service.CreateAsync(" ", "harness"));
    }

    [Fact]
    public async Task CurrentChanged_FiresOnOpenAndClose()
    {
        var created = await _service.CreateAsync(_temp.Path, "harness");
        var changes = 0;
        _service.CurrentChanged += (_, _) => changes++;

        await _service.OpenAsync(created.RootPath);
        _service.Close();

        Assert.Equal(2, changes);
        Assert.Null(_service.Current);
    }

    [Fact]
    public async Task FailedOpen_KeepsTheCurrentProject()
    {
        var created = await _service.CreateAsync(_temp.Path, "harness");

        await Assert.ThrowsAsync<ProjectException>(() => _service.OpenAsync(Path.Combine(_temp.Path, "missing")));

        Assert.Equal(created.RootPath, _service.Current?.RootPath);
    }
}
