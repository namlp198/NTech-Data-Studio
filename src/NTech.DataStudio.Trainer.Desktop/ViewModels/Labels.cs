using System.Globalization;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

namespace NTech.DataStudio.Trainer.Desktop.ViewModels;

/// <summary>Vietnamese wording of grades and states, in one place so every screen says the same.</summary>
public static class Labels
{
    public static string Meaning(Grade grade) => grade switch
    {
        Grade.Excellent => "Rất phù hợp",
        Grade.Good => "Phù hợp",
        Grade.Moderate => "Cần chỉnh cấu hình",
        Grade.Weak => "Chạy được nhưng chậm hoặc rủi ro",
        Grade.Inadequate => "Không đáp ứng",
        _ => "Thiếu dữ liệu"
    };

    public static string Confidence(Confidence value) => value switch
    {
        Hardware.Confidence.Measured => "đo thật",
        Hardware.Confidence.Reported => "hệ thống báo",
        _ => "giả định"
    };

    public static string State(string state) => state switch
    {
        RunStates.Created => "Đã tạo",
        RunStates.Preparing => "Đang chuẩn bị",
        RunStates.Preflight => "Đang chạy thử",
        RunStates.Running => "Đang train",
        RunStates.Evaluating => "Đang đánh giá",
        RunStates.Completed => "Hoàn tất",
        RunStates.Failed => "Lỗi",
        RunStates.Cancelled => "Đã dừng",
        RunStates.Interrupted => "Bị gián đoạn",
        _ => state
    };

    public static string ExportStatus(string? status) => status switch
    {
        "passed" => "Đạt",
        "failed" => "Không đạt",
        "inconclusive" => "Chưa kết luận",
        null or "" => "Chưa export",
        _ => status
    };

    public static string Metric(double? value) => value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "-";

    public static string Number(double? value, string format) => value?.ToString(format, CultureInfo.InvariantCulture) ?? "-";

    public static string Gb(double? value) => value is { } number ? number.ToString("0.#", CultureInfo.InvariantCulture) + " GB" : "-";

    public static string Duration(TimeSpan? value) =>
        value is { } span ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}" : "-";
}
