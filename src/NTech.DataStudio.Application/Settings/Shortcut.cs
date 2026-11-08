namespace NTech.DataStudio.Application.Settings;

/// <summary>
/// One key with its modifiers, kept as plain text so the Application layer needs no WPF types.
/// <see cref="Key"/> is a WPF <c>Key</c> name, with the aliases folded: Enter, PageUp, PageDown.
/// Alt is not supported: a held Alt means a menu to Windows and the screens ignore it.
/// </summary>
public sealed record Shortcut(string Key, bool Ctrl = false, bool Shift = false)
{
    private static readonly HashSet<string> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "None", "Ctrl", "Control", "Shift", "Alt", "LeftCtrl", "RightCtrl", "LeftShift", "RightShift", "LeftAlt", "RightAlt", "System",
        "LWin", "RWin", "CapsLock", "Capital", "NumLock", "Scroll"
    };

    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OemPlus"] = "+", ["Add"] = "Num +", ["OemMinus"] = "-", ["Subtract"] = "Num -",
        ["Back"] = "Backspace", ["Escape"] = "Esc", ["Delete"] = "Delete",
        ["Left"] = "←", ["Right"] = "→", ["Up"] = "↑", ["Down"] = "↓",
        ["Oem3"] = "`", ["OemComma"] = ",", ["OemPeriod"] = ".", ["OemQuestion"] = "/",
        ["Oem1"] = ";", ["Oem5"] = "\\", ["Oem4"] = "[", ["Oem6"] = "]", ["Oem7"] = "'"
    };

    /// <summary>Text for the screen: <c>Ctrl+Shift+Z</c>.</summary>
    public string Display
    {
        get
        {
            var key = Friendly.TryGetValue(Key, out var friendly) ? friendly : Key;
            if (Key.Length == 2 && Key[0] == 'D' && char.IsDigit(Key[1])) key = Key[1].ToString();
            if (Key.StartsWith("NumPad", StringComparison.Ordinal)) key = "Num " + Key["NumPad".Length..];
            return (Ctrl ? "Ctrl+" : string.Empty) + (Shift ? "Shift+" : string.Empty) + key;
        }
    }

    /// <summary>The stored form, readable back with <see cref="TryParse"/>.</summary>
    public override string ToString() => (Ctrl ? "Ctrl+" : string.Empty) + (Shift ? "Shift+" : string.Empty) + Key;

    public static bool TryParse(string? text, out Shortcut shortcut)
    {
        shortcut = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var ctrl = false;
        var shift = false;
        var rest = text.Trim();
        while (true)
        {
            if (rest.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase)) { ctrl = true; rest = rest[5..]; }
            else if (rest.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase)) { shift = true; rest = rest[6..]; }
            else break;
        }

        if (rest.Length == 0 || ModifierKeys.Contains(rest) || rest.Contains('+') && rest != "+") return false;

        shortcut = new Shortcut(rest, ctrl, shift);
        return true;
    }

    /// <summary>
    /// Why this cannot be a shortcut, in words for the user; null when it can. Space is the pan modifier
    /// of the canvas and 1 to 9 pick classes, so both stay fixed.
    /// </summary>
    public string? Problem()
    {
        if (ModifierKeys.Contains(Key)) return "Cần một phím chính, không chỉ phím Ctrl, Shift hay Alt.";
        if (Key.Equals("Space", StringComparison.OrdinalIgnoreCase)) return "Phím Space dành cho việc kéo pan ảnh.";
        if (Key.Length == 2 && Key[0] == 'D' && char.IsDigit(Key[1]) || Key.StartsWith("NumPad", StringComparison.Ordinal))
            return "Phím số 1 đến 9 dành cho việc chọn class.";
        return null;
    }
}
