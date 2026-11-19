namespace NTech.DataStudio.Application.Prepare;

/// <summary>What choosing one option of the Normalize step does, with what it gains and costs. Shown under the option and as its tooltip.</summary>
public sealed record OptionHelp(string What, string Benefit, string Drawback)
{
    public string Text => $"Khi chọn: {What}\nLợi ích: {Benefit}\nBất lợi: {Drawback}";
}

/// <summary>
/// The words of the Normalize step. Only figures measured in this project are quoted (docs/00_CONTEXT.md, the 20 MP run);
/// anything else says "thường" or "có thể" instead of a number.
/// </summary>
public static class NormalizeHelp
{
    public const string Locked =
        "Master là ảnh dùng để gán nhãn và nhãn lưu theo pixel của nó. Khi project đã có nhãn, cấu hình này không đổi được nữa; hãy chốt trước khi gán.";

    public const string Profile =
        "Model profile chỉ dùng để đề xuất cách xử lý và imgsz (cỡ ảnh lúc train). Nó không khóa task của project và không đổi nhãn. "
        + "Chọn profile cần ảnh lớn nhất trong các model bạn định train.";

    public const string MinObject =
        "Cạnh ngắn nhất của vật thể nhỏ nhất, đo trên ảnh gốc. Khi train, ảnh bị thu về imgsz; vật còn nhỏ hơn mức tối thiểu của profile thì model khó học được. "
        + "Điền số này để app cảnh báo và đề xuất imgsz hay tile. Bỏ trống thì không có cảnh báo.";

    public static OptionHelp Method(GeometryMethod method) => method switch
    {
        GeometryMethod.Downscale => new OptionHelp(
            "mỗi ảnh gốc thành một master, thu nhỏ nếu cạnh dài vượt mức đã chọn (không bao giờ phóng to).",
            "master nhẹ, mở và gán nhãn nhanh; một ảnh gốc ứng với một master nên dễ theo dõi.",
            "vật nhỏ hoặc mảnh mất chi tiết khi thu nhỏ nhiều, đường biên kém chính xác."),
        GeometryMethod.Tile => new OptionHelp(
            "mỗi ảnh gốc được cắt thành nhiều ô (tile) chồng lấn nhau, mỗi ô là một master.",
            "giữ nguyên chi tiết của vật rất nhỏ trên ảnh rất lớn; ô có kích thước vừa với imgsz nên không bị thu nhỏ khi train.",
            "số ảnh cần gán nhãn tăng nhiều lần; vật nằm ở chỗ chồng lấn xuất hiện ở hai ô. Quyết định nặng nhất vì mọi nhãn gán trên các ô này."),
        GeometryMethod.RoiCrop => new OptionHelp(
            "chỉ giữ một vùng cố định bạn kéo trên ảnh Trước, phần ngoài vùng bị bỏ khỏi master.",
            "bỏ phần nền không liên quan nên master nhẹ và vật chiếm tỉ lệ lớn hơn; hợp với camera cố định.",
            "vật nằm ngoài vùng sẽ không còn trong dữ liệu; ảnh không giao với vùng bị báo lỗi."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
    };

    public static OptionHelp MaxEdge(int longEdge) => longEdge switch
    {
        <= 2048 => new OptionHelp(
            $"cạnh dài master tối đa {longEdge} px.",
            "nhẹ nhất, tải ảnh nhanh. Đo trên bộ ảnh 20 MP: master còn khoảng 1/46 so với ảnh gốc.",
            "vật mảnh hay nhỏ (dây, băng dính) mất chi tiết; không thể lấy lại sau khi đã gán nhãn."),
        _ => new OptionHelp(
            $"cạnh dài master tối đa {longEdge} px.",
            "giữ nhiều chi tiết hơn cho vật nhỏ và mảnh, đường biên gán nhãn chính xác hơn.",
            "master nặng hơn, mở và gán nhãn chậm hơn; ảnh gốc nhỏ hơn mức này thì không khác gì (không phóng to).")
    };

    public static OptionHelp Format(MasterFormat format) => format switch
    {
        MasterFormat.Jpeg95 => new OptionHelp(
            "master lưu JPEG chất lượng 95.",
            "nhẹ, mở nhanh; đủ cho phần lớn bài toán.",
            "nén có mất mát nhỏ; lỗi bề mặt rất mảnh hay có độ tương phản thấp có thể bị làm mờ."),
        MasterFormat.Png => new OptionHelp(
            "master lưu PNG, không mất mát.",
            "giữ nguyên điểm ảnh, hợp với lỗi bề mặt rất mảnh.",
            "thường nặng hơn nhiều so với JPEG, tốn dung lượng đĩa và mở chậm hơn."),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    public static OptionHelp Mono(MonoMode mode) => mode switch
    {
        MonoMode.KeepOneChannel => new OptionHelp(
            "ảnh đen trắng giữ một kênh xám.",
            "ít dữ liệu nhất, đúng với nội dung ảnh.",
            "có trainer hay model chỉ nhận ảnh 3 kênh; khi đó phải đổi ở bước train."),
        MonoMode.TripleChannel => new OptionHelp(
            "ảnh đen trắng được nhân thành 3 kênh giống nhau.",
            "tương thích với mọi trainer và model, không phải xử lý thêm.",
            "dữ liệu có thể lớn hơn mà không thêm thông tin. Không ảnh hưởng ảnh màu."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };
}
