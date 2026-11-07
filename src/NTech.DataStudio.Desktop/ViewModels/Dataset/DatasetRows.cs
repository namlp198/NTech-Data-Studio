using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Application.Datasets;
using NTech.DataStudio.Desktop.Controls;
using NTech.DataStudio.Desktop.ViewModels.Prepare;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Classes;
using NTech.DataStudio.Domain.Datasets;

namespace NTech.DataStudio.Desktop.ViewModels.Dataset;

/// <summary>A number typed by the user: "1,5" and "1.5" both mean one and a half.</summary>
internal static class NumberText
{
    public static bool TryParse(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    public static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (!TryParse(text, out var number) || number != Math.Floor(number) || Math.Abs(number) > int.MaxValue)
            return false;
        value = (int)number;
        return true;
    }

    public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public sealed record MetricRow(string Label, string Value, string Caption);

public sealed record ClassCountRow(string Name, string Train, string Val, string Test, string Total, bool IsLow);

public sealed record VersionRow(long Id, string Name, string Summary);

/// <summary>One line of the augmentation form: a switch, a range, or both.</summary>
public sealed partial class AugmentOptionRow : ObservableObject
{
    private readonly Func<AugmentConfig, double, AugmentConfig> _apply;
    private readonly Action _changed;

    [ObservableProperty]
    private bool _isOn;

    [ObservableProperty]
    private string _valueText;

    public AugmentOptionRow(
        string key,
        string label,
        string? unit,
        bool hasToggle,
        bool hasValue,
        double defaultValue,
        double offValue,
        Func<AugmentConfig, double, AugmentConfig> apply,
        Action changed)
    {
        Key = key;
        Label = label;
        Unit = unit;
        HasToggle = hasToggle;
        HasValue = hasValue;
        DefaultValue = defaultValue;
        OffValue = offValue;
        _apply = apply;
        _changed = changed;
        _valueText = NumberText.Format(defaultValue);
        _isOn = !hasToggle;
    }

    public string Key { get; }
    public string Label { get; }
    public string? Unit { get; }
    public bool HasToggle { get; }
    public bool HasValue { get; }
    public double DefaultValue { get; }
    public double OffValue { get; }

    partial void OnIsOnChanged(bool value) => _changed();

    partial void OnValueTextChanged(string value) => _changed();

    public void Set(bool on, double? value)
    {
        IsOn = on;
        ValueText = NumberText.Format(value ?? DefaultValue);
    }

    /// <summary>Folds this line into the config, or says what is wrong with the typed value.</summary>
    public bool TryApply(AugmentConfig config, out AugmentConfig result, out string? error)
    {
        result = config;
        error = null;

        double value;
        if (HasToggle && !IsOn)
        {
            value = OffValue;
        }
        else if (HasValue)
        {
            if (!NumberText.TryParse(ValueText, out value))
            {
                error = $"\"{Label}\": \"{ValueText}\" không phải số.";
                return false;
            }
        }
        else
        {
            value = 1;
        }

        result = _apply(config, value);
        return true;
    }
}

/// <summary>One class in the remap table: keep it, drop it, or merge it into another.</summary>
public sealed partial class ClassMapRow : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private Option<long?>? _selectedAction;

    public ClassMapRow(LabelClass labelClass, IReadOnlyList<LabelClass> all, Action changed)
    {
        ClassId = labelClass.Id;
        Name = labelClass.Name;
        _changed = changed;

        var options = new List<Option<long?>>
        {
            new("Giữ nguyên", KeepSentinel),
            new("Bỏ class này", DropSentinel)
        };
        options.AddRange(all.Where(other => other.Id != labelClass.Id)
            .Select(other => new Option<long?>($"Gộp vào {other.Name}", other.Id)));
        Actions = options;
        _selectedAction = options[0];
    }

    // Real class ids are positive, so these cannot clash with one.
    private const long KeepSentinel = -1;
    private const long DropSentinel = -2;

    public long ClassId { get; }
    public string Name { get; }
    public IReadOnlyList<Option<long?>> Actions { get; }

    /// <summary>Null when the class is kept.</summary>
    public ClassMapEntry? ToEntry() => SelectedAction?.Value switch
    {
        null or KeepSentinel => null,
        DropSentinel => new ClassMapEntry(ClassId, null),
        var target => new ClassMapEntry(ClassId, target)
    };

    partial void OnSelectedActionChanged(Option<long?>? value) => _changed();

    public void Select(ClassMapEntry? entry)
    {
        long? wanted = entry is null ? KeepSentinel : entry.TargetClassId ?? DropSentinel;
        SelectedAction = Actions.FirstOrDefault(option => option.Value == wanted) ?? Actions[0];
    }
}

/// <summary>One preview cell: a real output picture with its labels.</summary>
public sealed class PreviewTileViewModel
{
    private PreviewTileViewModel(string caption, BitmapSource? picture, IReadOnlyList<PreviewShape> shapes, string? error)
    {
        Caption = caption;
        Picture = picture;
        Shapes = shapes;
        Error = error;
    }

    public string Caption { get; }
    public BitmapSource? Picture { get; }
    public IReadOnlyList<PreviewShape> Shapes { get; }
    public string? Error { get; }

    /// <summary>Safe to call off the UI thread: the bitmap is frozen before it is handed over.</summary>
    public static PreviewTileViewModel From(PreviewTile tile, IReadOnlyDictionary<long, Color> colors)
    {
        var copy = tile.CopyIndex == 0 ? "gốc" : $"bản {tile.CopyIndex}";
        var caption = $"{Path.GetFileName(tile.Image.MasterPath)} · {copy}";
        if (tile.Picture is null)
            return new PreviewTileViewModel(caption, null, Array.Empty<PreviewShape>(), tile.Error ?? "Không dựng được ảnh.");

        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(tile.Picture.Data))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }

        bitmap.Freeze();
        var shapes = tile.Labels
            .Select(label => new PreviewShape(
                label.Kind,
                label.Points.Select(point => new Point(point.X, point.Y)).ToList(),
                colors.TryGetValue(label.ClassId, out var color) ? color : Colors.Gray))
            .ToList();
        return new PreviewTileViewModel(caption, bitmap, shapes, null);
    }
}

