using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Desktop.Shell;

namespace NTech.DataStudio.Desktop.ViewModels.Settings;

/// <summary>One action of the Hot-Key list: its name, its keys now, and the two buttons.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _keysText = string.Empty;

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeButtonText))]
    private bool _isCapturing;

    public ShortcutRowViewModel(ShortcutInfo info, Action<ShortcutRowViewModel> change, Action<ShortcutRowViewModel> reset)
    {
        Info = info;
        ChangeCommand = new RelayCommand(() => change(this));
        ResetCommand = new RelayCommand(() => reset(this), () => IsCustom);
    }

    public ShortcutInfo Info { get; }
    public string Label => Info.Label;
    public IRelayCommand ChangeCommand { get; }
    public IRelayCommand ResetCommand { get; }

    public string ChangeButtonText => IsCapturing ? "Nhấn phím..." : "Đổi phím";
    public string ChangeAutomationName => $"Đổi phím {Label}";
    public string ResetAutomationName => $"Mặc định {Label}";

    partial void OnIsCustomChanged(bool value) => ResetCommand.NotifyCanExecuteChanged();
}

public sealed class ShortcutGroupViewModel
{
    public ShortcutGroupViewModel(string title, IReadOnlyList<ShortcutRowViewModel> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }
    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }
}

/// <summary>
/// The Settings screen. Its one section so far is Hot-Key: change the keys of the Annotate actions, saved at once
/// (decision 0015). A change takes effect without a restart because Annotate reads the same <see cref="SettingsService"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IActivatable
{
    private readonly SettingsService _settings;
    private readonly List<ShortcutRowViewModel> _rows = new();
    private ShortcutRowViewModel? _capturing;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;

        var groups = new List<ShortcutGroupViewModel>();
        foreach (var group in ShortcutCatalog.All.GroupBy(info => info.Group))
        {
            var rows = group.Select(info => new ShortcutRowViewModel(info, ToggleCapture, Reset)).ToList();
            _rows.AddRange(rows);
            groups.Add(new ShortcutGroupViewModel(group.Key, rows));
        }

        Groups = groups;
        _settings.Changed += (_, _) => Refresh();
        Refresh();
    }

    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; }

    public bool IsCapturing => _capturing is not null;

    /// <summary>The file could not be read or the last change could not be saved.</summary>
    public string? ProblemMessage => _settings.Problem;

    public bool HasCustom => _settings.Shortcuts.HasCustom;

    public Task ActivateAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    public void Deactivate() => StopCapture();

    private void Refresh()
    {
        foreach (var row in _rows)
        {
            row.KeysText = string.Join("   ", _settings.Shortcuts.For(row.Info.Action).Select(key => key.Display));
            row.IsCustom = _settings.Shortcuts.IsCustom(row.Info.Action);
        }

        OnPropertyChanged(nameof(ProblemMessage));
        OnPropertyChanged(nameof(HasCustom));
        ResetAllCommand.NotifyCanExecuteChanged();
    }

    private void ToggleCapture(ShortcutRowViewModel row)
    {
        var wasThis = ReferenceEquals(_capturing, row);
        StopCapture();
        if (wasThis) return;

        _capturing = row;
        row.IsCapturing = true;
        ErrorMessage = null;
        StatusMessage = $"Nhấn phím mới cho \"{row.Label}\". Esc để hủy.";
        OnPropertyChanged(nameof(IsCapturing));
    }

    private void StopCapture()
    {
        if (_capturing is { } row) row.IsCapturing = false;
        _capturing = null;
        OnPropertyChanged(nameof(IsCapturing));
    }

    private void Reset(ShortcutRowViewModel row)
    {
        StopCapture();
        ErrorMessage = null;
        _settings.Reset(row.Info.Action);
        StatusMessage = $"\"{row.Label}\" về phím mặc định: {row.KeysText}.";
    }

    private bool CanResetAll() => _settings.Shortcuts.HasCustom;

    [RelayCommand(CanExecute = nameof(CanResetAll))]
    private void ResetAll()
    {
        StopCapture();
        ErrorMessage = null;
        _settings.ResetAll();
        StatusMessage = "Mọi phím tắt đã về mặc định.";
    }

    /// <summary>
    /// The view sends every key here while a row is waiting for one. True when it was used up, so the view marks it handled:
    /// Esc cancels, a lone Ctrl or Shift is ignored, anything else becomes the row's key or says why it cannot.
    /// </summary>
    public bool HandleCapture(Key key, ModifierKeys modifiers)
    {
        if (_capturing is not { } row) return false;

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            StopCapture();
            StatusMessage = null;
            return true;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.System or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
            return true;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            ErrorMessage = "Không dùng được tổ hợp có Alt. Hãy thử Ctrl hoặc Shift.";
            return true;
        }

        var shortcut = new Shortcut(
            KeyNames.Of(key),
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift));
        if (_settings.Assign(row.Info.Action, shortcut) is { } problem)
        {
            ErrorMessage = problem;
            return true;
        }

        ErrorMessage = null;
        StopCapture();
        StatusMessage = $"\"{row.Label}\" dùng {shortcut.Display}.";
        return true;
    }
}
