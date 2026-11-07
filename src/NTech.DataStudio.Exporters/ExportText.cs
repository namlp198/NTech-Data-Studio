using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NTech.DataStudio.Application.Export;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Exporters;

internal static class ExportText
{
    private static readonly JsonSerializerOptions Quote = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Up to six decimals, no trailing zeros, '.' always. Enough for a pixel on an image 100 000 px wide.</summary>
    public static string Number(double value) =>
        (Math.Abs(value) < 5e-7 ? 0 : value).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>A YAML double-quoted string; JSON escaping is a subset of it, so any class name is safe.</summary>
    public static string YamlString(string text) => JsonSerializer.Serialize(text, Quote);

    public static string SplitFolder(SplitKind split, bool cocoNames) => split switch
    {
        SplitKind.Train => "train",
        SplitKind.Val => cocoNames ? "valid" : "val",
        _ => "test"
    };

    /// <summary>Writes a text file as UTF-8 without a byte-order mark, creating its folder.</summary>
    public static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    public static Dictionary<long, ExportClass> ByClassId(IReadOnlyList<ExportClass> classes) =>
        classes.ToDictionary(item => item.ClassId);
}
