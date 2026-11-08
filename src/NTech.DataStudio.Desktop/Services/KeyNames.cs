using System.Windows.Input;

namespace NTech.DataStudio.Desktop.Services;

/// <summary>The name a WPF key is stored under in a shortcut. Folds the aliases WPF gives one key (Return and Enter).</summary>
public static class KeyNames
{
    public static string Of(Key key) => key switch
    {
        Key.Return => "Enter",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Capital => "CapsLock",
        _ => key.ToString()
    };
}
