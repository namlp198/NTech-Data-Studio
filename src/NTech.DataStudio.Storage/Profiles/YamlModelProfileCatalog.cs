using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Domain.Profiles;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NTech.DataStudio.Storage.Profiles;

/// <summary>Adding a model is adding a YAML file to the folder; no code changes.</summary>
public sealed class YamlModelProfileCatalog : IModelProfileCatalog
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly string _folder;

    public YamlModelProfileCatalog(string folder)
    {
        _folder = folder;
    }

    public IReadOnlyList<ModelProfile> LoadAll()
    {
        if (!Directory.Exists(_folder))
            throw new InvalidDataException($"Không tìm thấy thư mục profile \"{_folder}\".");

        return Directory.EnumerateFiles(_folder, "*.yaml")
            .Select(Load)
            .OrderBy(profile => profile.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static ModelProfile Load(string path)
    {
        ProfileFile? file;
        try
        {
            file = Deserializer.Deserialize<ProfileFile>(File.ReadAllText(path));
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"Profile \"{path}\" sai cú pháp: {exception.Message}", exception);
        }

        if (file is null
            || string.IsNullOrWhiteSpace(file.Id)
            || string.IsNullOrWhiteSpace(file.Family)
            || file.ImgszDefault <= 0
            || file.Stride <= 0
            || !Enum.TryParse<TaskKind>(file.Task, ignoreCase: true, out var task))
        {
            throw new InvalidDataException($"Profile \"{path}\" thiếu hoặc sai id, family, task, imgsz_default hay stride.");
        }

        return new ModelProfile(
            file.Id,
            file.Family,
            task,
            file.ImgszDefault,
            file.ImgszOptions,
            file.Stride,
            file.MinObjectPx,
            file.ExportFormats);
    }

    private sealed class ProfileFile
    {
        public string Id { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Task { get; set; } = string.Empty;
        public int ImgszDefault { get; set; }
        public List<int> ImgszOptions { get; set; } = new();
        public int Stride { get; set; }
        public int MinObjectPx { get; set; }
        public List<string> ExportFormats { get; set; } = new();
    }
}
