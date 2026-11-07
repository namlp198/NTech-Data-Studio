using System.Globalization;
using System.Text;

namespace NTech.DataStudio.Application.Export;

/// <summary>File and folder names for an export: ASCII only, so no trainer or tool trips on a Vietnamese path.</summary>
public static class ExportNames
{
    /// <summary>Letters, digits, '-' and '_' only; accents are stripped. Never empty.</summary>
    public static string Slug(string text)
    {
        var decomposed = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;

            var folded = character switch
            {
                'đ' => 'd',
                'Đ' => 'D',
                _ => character
            };
            var plain = folded < 128 && (char.IsLetterOrDigit(folded) || folded is '-' or '_');
            var next = plain ? folded : '_';
            if (next == '_' && builder.Length > 0 && builder[^1] == '_') continue;
            builder.Append(next);
        }

        var result = builder.ToString().Trim('_');
        return result.Length == 0 ? "x" : result;
    }

    /// <summary>A name not in <paramref name="taken"/> yet; adds the name to it.</summary>
    public static string Unique(string name, ISet<string> taken)
    {
        var candidate = name;
        for (var suffix = 2; !taken.Add(candidate); suffix++)
            candidate = $"{name}_{suffix}";
        return candidate;
    }
}
