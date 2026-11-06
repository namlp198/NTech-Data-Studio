using System.Text.RegularExpressions;
using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Domain.Classes;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>The first ten class colors; they mirror Color.Class.1..10 in Themes/Tokens/Annotation.xaml. Class 11 onward cycles.</summary>
public static class ClassPalette
{
    public static readonly IReadOnlyList<string> Colors = new[]
    {
        "#4A90E2", "#E25D5D", "#34C3A0", "#C77DFF", "#FF9F43",
        "#2EC4E6", "#F05FB8", "#9BC53D", "#FFD166", "#7E8CE0"
    };

    public static string ForIndex(int index) => Colors[index % Colors.Count];
}

public sealed partial class ClassService
{
    public const int MaxNameLength = 64;
    public const int MaxHotkey = 9;

    private readonly IAnnotationStore _store;

    public ClassService(IAnnotationStore store)
    {
        _store = store;
    }

    public IReadOnlyList<LabelClass> Load() => _store.LoadClasses().OrderBy(item => item.Order).ThenBy(item => item.Id).ToList();

    public IReadOnlyDictionary<long, int> LoadCounts() => _store.LoadClassCounts();

    /// <summary>The new class takes the next palette color and the lowest free hotkey from 1 to 9.</summary>
    public LabelClass Add(string name)
    {
        var classes = Load();
        var clean = ValidateName(name, classes, exceptId: null);
        var used = classes.Where(item => item.Hotkey is not null).Select(item => item.Hotkey!.Value).ToHashSet();
        int? hotkey = Enumerable.Range(1, MaxHotkey).Cast<int?>().FirstOrDefault(key => !used.Contains(key!.Value));
        return _store.AddClass(clean, ClassPalette.ForIndex(classes.Count), hotkey);
    }

    public void Rename(long id, string name)
    {
        var classes = Load();
        var current = Find(classes, id);
        _store.UpdateClass(current with { Name = ValidateName(name, classes, id) });
    }

    public void Recolor(long id, string? color)
    {
        var clean = (color ?? string.Empty).Trim();
        if (!HexColor().IsMatch(clean))
            throw new ProjectException($"Màu \"{clean}\" không hợp lệ. Dùng dạng #RRGGBB.");

        _store.UpdateClass(Find(Load(), id) with { Color = clean.ToUpperInvariant() });
    }

    public ClassUsage Usage(long id) => _store.GetClassUsage(id);

    /// <summary>Everything labeled as <paramref name="fromId"/> becomes <paramref name="intoId"/>.</summary>
    public void Merge(long fromId, long intoId)
    {
        if (fromId == intoId)
            throw new ProjectException("Không thể gộp một class vào chính nó.");

        var classes = Load();
        Find(classes, fromId);
        Find(classes, intoId);
        _store.MergeClasses(fromId, intoId);
    }

    /// <summary>Also deletes the labels of this class. Ask the user first, with <see cref="Usage"/>.</summary>
    public void Delete(long id)
    {
        Find(Load(), id);
        _store.DeleteClass(id);
    }

    private static LabelClass Find(IReadOnlyList<LabelClass> classes, long id) =>
        classes.FirstOrDefault(item => item.Id == id)
            ?? throw new ProjectException("Class này không còn tồn tại.");

    private static string ValidateName(string? name, IReadOnlyList<LabelClass> classes, long? exceptId)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0)
            throw new ProjectException("Tên class không được để trống.");
        if (clean.Length > MaxNameLength)
            throw new ProjectException($"Tên class dài quá {MaxNameLength} ký tự.");
        if (classes.Any(item => item.Id != exceptId && string.Equals(item.Name, clean, StringComparison.OrdinalIgnoreCase)))
            throw new ProjectException($"Đã có class tên \"{clean}\".");
        return clean;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
