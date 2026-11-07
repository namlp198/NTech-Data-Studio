using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NTech.DataStudio.Trainer.Catalog;

public enum ModelStatus
{
    Supported,
    Experimental,
    Hidden
}

public sealed record VramNeed(double Min, double Recommended);

public sealed record ModelFamily(
    string Id,
    string Engine,
    string Task,
    IReadOnlyList<string> Sizes,
    string WeightsPattern,
    ModelStatus Status,
    string License,
    bool OnnxNms,
    IReadOnlyDictionary<string, VramNeed> VramAt640);

/// <summary>One choosable model: a family and a size, such as yolo11 + s.</summary>
public sealed record ModelVariant(ModelFamily Family, string Size)
{
    public string Name => Family.Id + Size;

    public string Weights => Family.WeightsPattern.Replace("{size}", Size, StringComparison.Ordinal);

    /// <summary>Null when this version has not been measured: the assessment must then say Unknown, not guess.</summary>
    public VramNeed? VramAt640 => Family.VramAt640.GetValueOrDefault(Size);
}

/// <summary>
/// The Trainer's model list. Versions of one engine differ only by data here, so adding one needs
/// no code. Kept apart from <c>profiles/</c>, which drives the Export screen of Data Studio.
/// </summary>
public sealed class ModelCatalog
{
    private readonly List<ModelFamily> _families;

    private ModelCatalog(List<ModelFamily> families) => _families = families;

    public IReadOnlyList<ModelFamily> Visible => _families.Where(family => family.Status != ModelStatus.Hidden).ToList();

    public static ModelCatalog LoadFolder(string folder)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var families = new List<ModelFamily>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.yaml").Order(StringComparer.Ordinal))
        {
            var document = deserializer.Deserialize<CatalogFile>(File.ReadAllText(file));
            families.AddRange(document.Models.Select(Convert));
        }

        return new ModelCatalog(families);
    }

    /// <summary>Accepts <c>yolo11s</c>. Returns null for an unknown family or a size that family lacks.</summary>
    public ModelVariant? Find(string name)
    {
        foreach (var family in _families.OrderByDescending(item => item.Id.Length))
        {
            if (!name.StartsWith(family.Id, StringComparison.OrdinalIgnoreCase)) continue;
            var size = name[family.Id.Length..].ToLowerInvariant();
            if (family.Sizes.Contains(size)) return new ModelVariant(family, size);
        }

        return null;
    }

    private static ModelFamily Convert(CatalogEntry entry) => new(
        entry.Id,
        entry.Engine,
        entry.Task,
        entry.Sizes,
        entry.Weights,
        Enum.Parse<ModelStatus>(entry.Status, ignoreCase: true),
        entry.License,
        entry.OnnxNms,
        entry.VramGbAt640.ToDictionary(pair => pair.Key, pair => new VramNeed(pair.Value.Min, pair.Value.Recommended)));

    private sealed class CatalogFile
    {
        public List<CatalogEntry> Models { get; set; } = [];
    }

    private sealed class CatalogEntry
    {
        public string Id { get; set; } = "";
        public string Engine { get; set; } = "";
        public string Task { get; set; } = "";
        public List<string> Sizes { get; set; } = [];
        public string Weights { get; set; } = "";
        public string Status { get; set; } = "hidden";
        public string License { get; set; } = "";
        public bool OnnxNms { get; set; }

        [YamlMember(Alias = "vram_gb_at_640")]
        public Dictionary<string, VramEntry> VramGbAt640 { get; set; } = [];
    }

    private sealed class VramEntry
    {
        public double Min { get; set; }
        public double Recommended { get; set; }
    }
}
