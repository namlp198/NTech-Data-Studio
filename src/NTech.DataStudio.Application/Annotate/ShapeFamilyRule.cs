using NTech.DataStudio.Domain.Annotations;

namespace NTech.DataStudio.Application.Annotate;

/// <summary>An edit would put a box and a polygon on the same image (decision 0022). The message says what to do instead.</summary>
public sealed class ShapeMixException : InvalidOperationException
{
    public ShapeMixException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// One image carries boxes or polygons, never both: a box and a polygon on one object export as two labels, and a Detect
/// dataset can be had from polygons anyway (Export turns each into its box). The rule only stops an edit from
/// <em>introducing</em> the mix, so an image that already has both (labelled before the rule) can still be tidied.
/// </summary>
public static class ShapeFamilyRule
{
    /// <returns>Null when the edit is fine; otherwise why it is refused, in words for the user.</returns>
    public static string? Check(IReadOnlyCollection<Annotation> existing, EditRequest request)
    {
        var removed = request.Before.Select(annotation => annotation.Id).ToHashSet();
        var known = existing.Select(annotation => annotation.Id).ToHashSet();
        var introduced = request.After.Where(annotation => !known.Contains(annotation.Id)).ToList();
        if (introduced.Count == 0) return null;

        var after = existing.Where(annotation => !removed.Contains(annotation.Id)).Concat(request.After).ToList();

        if (introduced.Any(annotation => annotation.Kind == ShapeKind.Box) && after.Any(annotation => annotation.Kind == ShapeKind.Polygon))
        {
            return "Ảnh này đã có polygon nên không thêm box được: mỗi ảnh chỉ có một loại hình, box hoặc polygon. "
                   + "Xóa các polygon trước. (Dataset Detect vẫn xuất được từ polygon: mỗi polygon thành box bao quanh.)";
        }

        if (introduced.Any(annotation => annotation.Kind == ShapeKind.Polygon) && after.Any(annotation => annotation.Kind == ShapeKind.Box))
        {
            return "Ảnh này đã có box nên không thêm polygon được: mỗi ảnh chỉ có một loại hình, box hoặc polygon. "
                   + "Dùng nút \"Box thành polygon (SAM)\" để thay các box bằng polygon, hoặc xóa các box trước.";
        }

        return null;
    }
}
