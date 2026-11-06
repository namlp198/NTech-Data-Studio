using NTech.DataStudio.Domain.Profiles;
using NTech.DataStudio.Storage.Profiles;

namespace NTech.DataStudio.Application.Tests.Storage;

public class YamlModelProfileCatalogTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string ShippedProfilesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NTech.DataStudio.slnx")))
            directory = directory.Parent;

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repo root not found."), "profiles");
    }

    [Fact]
    public void ShippedProfiles_AllLoad()
    {
        var profiles = new YamlModelProfileCatalog(ShippedProfilesFolder()).LoadAll();

        Assert.Equal(
            new[]
            {
                "anomaly-patchcore", "rf-detr-detect", "rf-detr-segment",
                "yolo26-classify", "yolo26-detect", "yolo26-obb", "yolo26-segment"
            },
            profiles.Select(profile => profile.Id));
    }

    [Fact]
    public void ShippedProfiles_DefaultSizeIsOneOfTheOptionsAndAMultipleOfTheStride()
    {
        foreach (var profile in new YamlModelProfileCatalog(ShippedProfilesFolder()).LoadAll())
        {
            Assert.Contains(profile.ImgszDefault, profile.ImgszOptions);
            Assert.Equal(0, profile.ImgszDefault % profile.Stride);
        }
    }

    [Fact]
    public void Load_ReadsTheDocumentedFields()
    {
        var profile = new YamlModelProfileCatalog(ShippedProfilesFolder()).LoadAll()
            .Single(item => item.Id == "yolo26-detect");

        Assert.Equal("ultralytics-yolo", profile.Family);
        Assert.Equal(TaskKind.Detect, profile.Task);
        Assert.Equal(640, profile.ImgszDefault);
        Assert.Equal(new[] { 640, 960, 1280 }, profile.ImgszOptions);
        Assert.Equal(32, profile.Stride);
        Assert.Equal(12, profile.MinObjectPx);
        Assert.Equal(new[] { "yolo" }, profile.ExportFormats);
    }

    [Theory]
    [InlineData("id: [unclosed")]
    [InlineData("family: x\ntask: detect\nimgsz_default: 640\nstride: 32")]
    [InlineData("id: x\nfamily: x\ntask: dance\nimgsz_default: 640\nstride: 32")]
    [InlineData("id: x\nfamily: x\ntask: detect\nimgsz_default: 0\nstride: 32")]
    public void Load_NamesTheBadFile(string yaml)
    {
        File.WriteAllText(Path.Combine(_temp.Path, "broken.yaml"), yaml);

        var exception = Assert.Throws<InvalidDataException>(() => new YamlModelProfileCatalog(_temp.Path).LoadAll());

        Assert.Contains("broken.yaml", exception.Message);
    }

    [Fact]
    public void Load_MissingFolderIsReported()
    {
        Assert.Throws<InvalidDataException>(() =>
            new YamlModelProfileCatalog(Path.Combine(_temp.Path, "none")).LoadAll());
    }
}
