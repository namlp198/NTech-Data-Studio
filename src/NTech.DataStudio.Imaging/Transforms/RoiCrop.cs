using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Geometry;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Transforms;

public static class RoiCrop
{
    /// <summary>The same ROI applies to the whole batch, so a smaller image gets the part that overlaps it.</summary>
    /// <exception cref="ImageReadException">No ROI was set, or it misses this image entirely.</exception>
    public static Rect ClampToImage(PixelRect? roi, int width, int height)
    {
        if (roi is not { } area)
            throw new ImageReadException("Chưa chọn vùng ROI.");

        var left = (int)Math.Clamp(Math.Floor(area.X), 0, width);
        var top = (int)Math.Clamp(Math.Floor(area.Y), 0, height);
        var right = (int)Math.Clamp(Math.Ceiling(area.Right), 0, width);
        var bottom = (int)Math.Clamp(Math.Ceiling(area.Bottom), 0, height);
        if (right <= left || bottom <= top)
            throw new ImageReadException($"Vùng ROI nằm ngoài ảnh {width} × {height}.");

        return new Rect(left, top, right - left, bottom - top);
    }
}
