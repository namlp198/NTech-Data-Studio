namespace NTech.DataStudio.Application.Settings;

/// <summary>Everything in Annotate a key can do (decision 0015). The names are stored in settings.json: never rename one.</summary>
public enum ShortcutAction
{
    SelectTool,
    BoxTool,
    PolygonTool,
    RotatedBoxTool,
    TagTool,
    PanTool,
    NextTool,
    PreviousTool,
    ZoomIn,
    ZoomOut,
    FitView,
    ToggleShapes,
    PreviousImage,
    NextImage,
    Finish,
    UndoPoint,
    Escape,
    ToggleNull,
    Delete,
    Undo,
    Redo,
    Copy,
    Paste
}

public sealed record ShortcutInfo(ShortcutAction Action, string Group, string Label, IReadOnlyList<Shortcut> Defaults);

/// <summary>The catalogue: what each action is called and which keys it has until the user changes them.</summary>
public static class ShortcutCatalog
{
    public const string Tools = "Công cụ";
    public const string View = "Xem và duyệt ảnh";
    public const string Edit = "Chỉnh sửa nhãn";

    public static IReadOnlyList<ShortcutInfo> All { get; } = new[]
    {
        Info(ShortcutAction.SelectTool, Tools, "Công cụ Chọn", "V", "S"),
        Info(ShortcutAction.BoxTool, Tools, "Công cụ Box", "B"),
        Info(ShortcutAction.PolygonTool, Tools, "Công cụ Polygon", "P"),
        Info(ShortcutAction.RotatedBoxTool, Tools, "Công cụ Rotated box", "R"),
        Info(ShortcutAction.TagTool, Tools, "Công cụ Tag", "T"),
        Info(ShortcutAction.PanTool, Tools, "Công cụ Pan", "G"),
        Info(ShortcutAction.NextTool, Tools, "Công cụ kế", "Q"),
        Info(ShortcutAction.PreviousTool, Tools, "Công cụ trước", "Shift+Q"),

        Info(ShortcutAction.ZoomIn, View, "Phóng to", "OemPlus", "Add"),
        Info(ShortcutAction.ZoomOut, View, "Thu nhỏ", "OemMinus", "Subtract"),
        Info(ShortcutAction.FitView, View, "Vừa khung", "F"),
        Info(ShortcutAction.ToggleShapes, View, "Ẩn/hiện mọi nhãn", "H"),
        Info(ShortcutAction.PreviousImage, View, "Ảnh trước", "A", "Left"),
        Info(ShortcutAction.NextImage, View, "Ảnh sau", "D", "Right"),

        Info(ShortcutAction.Finish, Edit, "Kết thúc hình đang vẽ", "Enter"),
        Info(ShortcutAction.UndoPoint, Edit, "Bỏ đỉnh vừa vẽ", "Back"),
        Info(ShortcutAction.Escape, Edit, "Thoát lệnh, về công cụ Chọn", "Escape"),
        Info(ShortcutAction.ToggleNull, Edit, "Đánh dấu ảnh Null", "N"),
        Info(ShortcutAction.Delete, Edit, "Xóa nhãn đang chọn", "Delete"),
        Info(ShortcutAction.Undo, Edit, "Hoàn tác", "Ctrl+Z"),
        Info(ShortcutAction.Redo, Edit, "Làm lại", "Ctrl+Y", "Ctrl+Shift+Z"),
        Info(ShortcutAction.Copy, Edit, "Sao chép nhãn", "Ctrl+C"),
        Info(ShortcutAction.Paste, Edit, "Dán nhãn", "Ctrl+V")
    };

    public static ShortcutInfo Of(ShortcutAction action) => All.First(info => info.Action == action);

    private static ShortcutInfo Info(ShortcutAction action, string group, string label, params string[] defaults) =>
        new(action, group, label, defaults.Select(text =>
        {
            Shortcut.TryParse(text, out var shortcut);
            return shortcut;
        }).ToList());
}

/// <summary>A key cannot be given to an action; <see cref="Exception.Message"/> says why, in words for the user.</summary>
public sealed class ShortcutException : ArgumentException
{
    public ShortcutException(string message)
        : base(message)
    {
    }
}
