using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Domain.Profiles;

namespace NTech.DataStudio.Application.Prepare;

public sealed record GeometryProposal(GeometryPlan Plan, int RecommendedImgsz, IReadOnlyList<string> Warnings);

/// <summary>Turns "how small is the object" into a geometry plan, so tiny objects do not vanish when training resizes the image.</summary>
public static class GeometryPlanner
{
    private static readonly int[] MasterLongEdges = { 2048, 4096 };

    private const double MinTileOverlapFraction = 0.1;
    private const double TileOverlapObjectFactor = 1.5;

    // Annotating needs more pixels than training does to place edges accurately.
    private const double AnnotationMarginFactor = 2;

    /// <param name="sourceLongEdge">Long edge of the batch, for example the audit's median.</param>
    /// <param name="minObjectPx">Smallest object in source pixels; null when unknown.</param>
    /// <param name="roi">A fixed crop the user drew, or null.</param>
    public static GeometryProposal Propose(int sourceLongEdge, int? minObjectPx, ModelProfile profile, PixelRect? roi = null)
    {
        var longEdge = roi is { } crop ? (int)Math.Ceiling(Math.Max(crop.Width, crop.Height)) : sourceLongEdge;
        var sizes = profile.ImgszOptions.Where(size => size > 0).Append(profile.ImgszDefault).Distinct().Order().ToList();
        var warnings = new List<string>();

        if (minObjectPx is not > 0 || profile.MinObjectPx <= 0 || longEdge <= 0)
            return Finish(roi, longEdge, profile.ImgszDefault, null, profile, warnings);

        var objectPx = minObjectPx.Value;
        if (objectPx < profile.MinObjectPx)
            warnings.Add($"Vật thể {objectPx} px đã nhỏ hơn mức tối thiểu {profile.MinObjectPx} px của {profile.Id} ngay trên ảnh gốc; model có thể bỏ sót dù giữ nguyên độ phân giải.");

        var atDefault = PixelsAt(objectPx, longEdge, profile.ImgszDefault);
        if (atDefault >= profile.MinObjectPx)
            return Finish(roi, longEdge, profile.ImgszDefault, objectPx, profile, warnings);

        var larger = sizes.Where(size => size > profile.ImgszDefault).ToList();
        var enough = larger.Cast<int?>().FirstOrDefault(size => PixelsAt(objectPx, longEdge, size!.Value) >= profile.MinObjectPx);
        if (enough is { } imgsz)
        {
            warnings.Add($"Ở imgsz {profile.ImgszDefault}, vật thể {objectPx} px còn {atDefault:0.#} px. Nên dùng imgsz {imgsz} hoặc tile.");
            return Finish(roi, longEdge, imgsz, objectPx, profile, warnings);
        }

        var hint = larger.Count > 0 ? $" imgsz {larger[^1]} vẫn chưa đủ, nên dùng tile." : " Nên dùng tile.";
        warnings.Add($"Ở imgsz {profile.ImgszDefault}, vật thể {objectPx} px còn {atDefault:0.#} px.{hint}");
        if (roi is not null)
            return Finish(roi, longEdge, sizes[^1], objectPx, profile, warnings);

        var tile = sizes.Where(size => size <= longEdge).DefaultIfEmpty(sizes[0]).Last();
        var overlap = (int)Math.Ceiling(Math.Max(tile * MinTileOverlapFraction, objectPx * TileOverlapObjectFactor));
        return new GeometryProposal(
            new GeometryPlan(GeometryMethod.Tile, TileSize: tile, TileOverlap: Math.Min(overlap, tile / 2)),
            tile,
            warnings);
    }

    private static GeometryProposal Finish(
        PixelRect? roi,
        int longEdge,
        int imgsz,
        int? objectPx,
        ModelProfile profile,
        IReadOnlyList<string> warnings)
    {
        var plan = roi is not null
            ? new GeometryPlan(GeometryMethod.RoiCrop, Roi: roi)
            : new GeometryPlan(GeometryMethod.Downscale, MaxLongEdge: ChooseMasterEdge(longEdge, imgsz, objectPx, profile));
        return new GeometryProposal(plan, imgsz, warnings);
    }

    private static int ChooseMasterEdge(int longEdge, int imgsz, int? objectPx, ModelProfile profile)
    {
        foreach (var edge in MasterLongEdges)
        {
            if (edge < imgsz) continue;
            if (objectPx is null || PixelsAt(objectPx.Value, longEdge, edge) >= profile.MinObjectPx * AnnotationMarginFactor)
                return edge;
        }

        return MasterLongEdges[^1];
    }

    /// <summary>An image is only ever scaled down, so a size above the long edge changes nothing.</summary>
    private static double PixelsAt(int objectPx, int longEdge, int size) =>
        objectPx * Math.Min(1.0, size / (double)longEdge);
}
