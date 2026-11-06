using System.Text;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

internal static class PrepareText
{
    private const int FailuresShown = 5;

    public static string Bytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    public static string Reason(RejectReason? reason) => reason switch
    {
        RejectReason.Corrupt => "Hỏng",
        RejectReason.ExactDuplicate => "Trùng hoàn toàn",
        RejectReason.NearDuplicate => "Gần trùng",
        RejectReason.Blurry => "Mờ",
        RejectReason.TooDark => "Quá tối",
        RejectReason.TooBright => "Cháy sáng",
        _ => string.Empty
    };

    /// <summary>The first few failures, because a long list would not fit on the screen.</summary>
    public static string Failures(IReadOnlyList<ImageFailure> failures)
    {
        var text = new StringBuilder($"{failures.Count} ảnh lỗi:");
        foreach (var failure in failures.Take(FailuresShown))
            text.Append('\n').Append(failure.FileName).Append(": ").Append(failure.Message);
        if (failures.Count > FailuresShown)
            text.Append($"\n... và {failures.Count - FailuresShown} ảnh khác.");
        return text.ToString();
    }
}
