using System.ComponentModel;
using NTech.DataStudio.Application.Settings;

namespace NTech.DataStudio.Desktop.ViewModels.Annotate;

/// <summary>
/// Tooltip text that names the keys an action has now. The rail binds to <c>Hints[BoxTool]</c>; a rebind
/// raises <c>Item[]</c>, so every tooltip follows without the view knowing which keys are set.
/// </summary>
public sealed class ShortcutHints : INotifyPropertyChanged
{
    private readonly SettingsService _settings;

    public ShortcutHints(SettingsService settings)
    {
        _settings = settings;
        _settings.Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string action] =>
        Enum.TryParse<ShortcutAction>(action, out var parsed) ? Text(parsed) : action;

    /// <summary>The keys of an action as shown to the user: <c>V hoặc S</c>.</summary>
    public string Keys(ShortcutAction action) => string.Join(" hoặc ", _settings.Shortcuts.For(action).Select(key => key.Display));

    public string Text(ShortcutAction action) => action switch
    {
        ShortcutAction.SelectTool =>
            $"Chọn và sửa ({Keys(action)}). {Keys(ShortcutAction.NextTool)} và {Keys(ShortcutAction.PreviousTool)}: công cụ kế và trước. "
            + $"Kéo vùng trống: pan ảnh. {Keys(ShortcutAction.Escape)}: về công cụ này",
        ShortcutAction.BoxTool => With("Bounding box", action),
        ShortcutAction.PolygonTool => With("Polygon", action),
        ShortcutAction.MagicPolygonTool =>
            $"Magic polygon ({Keys(action)}): rê chuột để xem vật thể tự tách, bấm để lấy, hoặc kéo khung quanh vật thể (hợp với vật nhỏ, ít tương phản). Phóng to thì app phân tích riêng phần đang xem nên chính xác hơn. Shift+bấm thêm điểm trong vật thể, bấm phải thêm điểm ngoài vật thể, {Keys(ShortcutAction.UndoPoint)} bỏ điểm vừa thêm",
        ShortcutAction.RotatedBoxTool => With("Rotated box", action),
        ShortcutAction.TagTool => With("Tag cả ảnh", action),
        ShortcutAction.PanTool =>
            $"Pan: kéo ảnh bằng chuột trái ({Keys(action)}). Cũng giữ Space rồi kéo, hoặc kéo chuột giữa",
        ShortcutAction.Undo => With("Hoàn tác", action),
        ShortcutAction.Redo => With("Làm lại", action),
        ShortcutAction.Delete => With("Xóa nhãn đang chọn", action),
        ShortcutAction.ToggleShapes => With("Ẩn/hiện mọi nhãn", action),
        ShortcutAction.FitView => With("Vừa khung", action),
        ShortcutAction.ZoomIn => With("Phóng to", action),
        ShortcutAction.ZoomOut => With("Thu nhỏ", action),
        ShortcutAction.ToggleNull => With("Ảnh không có vật thể", action),
        _ => With(ShortcutCatalog.Of(action).Label, action)
    };

    private string With(string text, ShortcutAction action) => $"{text} ({Keys(action)})";
}
