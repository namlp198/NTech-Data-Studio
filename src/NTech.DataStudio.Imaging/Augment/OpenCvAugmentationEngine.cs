using NTech.DataStudio.Application.Abstractions;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Domain.Augment;
using NTech.DataStudio.Domain.Geometry;
using NTech.DataStudio.Imaging.Transforms;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Augment;

/// <summary>
/// Renders a <see cref="RenderPlan"/> with OpenCV: one warpAffine for all geometry, then the
/// photometric steps, then cutouts and optional JPEG recompression. Files go through byte
/// arrays because OpenCV cannot open paths with Vietnamese characters.
/// </summary>
public sealed class OpenCvAugmentationEngine : IAugmentationEngine
{
    public RenderedImage Render(string masterPath, RenderPlan plan, RenderFormat format, int jpegQuality = 95)
    {
        using var master = Decode(ReadAllBytes(masterPath));
        using var warped = Warp(master, plan);
        ApplyPhotometric(warped, plan);
        ApplyCutouts(warped, plan.Augment);

        using var finished = plan.Augment.JpegQuality is { } artifact ? Recompress(warped, artifact) : null;
        var final = finished ?? warped;

        var parameters = format == RenderFormat.Jpeg
            ? new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, Math.Clamp(jpegQuality, 1, 100)) }
            : Array.Empty<ImageEncodingParam>();
        Cv2.ImEncode(format == RenderFormat.Jpeg ? ".jpg" : ".png", final, out var buffer, parameters);
        return new RenderedImage(buffer, final.Width, final.Height);
    }

    private static Mat Warp(Mat master, RenderPlan plan)
    {
        var transform = plan.Transform;
        var sameSize = plan.OutputWidth == master.Width && plan.OutputHeight == master.Height;
        if (sameSize && transform == Affine2D.Identity)
            return master.Clone();

        // warpAffine samples without filtering, so a strong shrink would alias thin details.
        // Shrink by area first and let the warp do only the remaining rotation and shift.
        Mat? shrunk = null;
        var source = master;
        var scale = transform.MeanScale;
        if (scale < 0.5)
        {
            var width = Math.Max(1, (int)Math.Round(master.Width * scale));
            var height = Math.Max(1, (int)Math.Round(master.Height * scale));
            shrunk = new Mat();
            Cv2.Resize(master, shrunk, new Size(width, height), 0, 0, InterpolationFlags.Area);
            transform = Affine2D.Scale((double)master.Width / width, (double)master.Height / height).Then(transform);
            source = shrunk;
        }

        try
        {
            var centers = transform.ToPixelCenters();
            using var matrix = Mat.FromArray(new[,]
            {
                { centers.M11, centers.M12, centers.Dx },
                { centers.M21, centers.M22, centers.Dy }
            });
            var output = new Mat();
            Cv2.WarpAffine(
                source,
                output,
                matrix,
                new Size(plan.OutputWidth, plan.OutputHeight),
                InterpolationFlags.Linear,
                BorderTypes.Constant,
                Scalar.All(RenderPlan.FillValue));
            return output;
        }
        finally
        {
            shrunk?.Dispose();
        }
    }

    private static void ApplyPhotometric(Mat image, RenderPlan plan)
    {
        var augment = plan.Augment;
        var color = image.Channels() >= 3;

        if (plan.Grayscale && color)
        {
            using var gray = new Mat();
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(gray, image, ColorConversionCodes.GRAY2BGR);
        }

        // out = ((in * exposure) - 128) * contrast + 128 + brightness, as one multiply and one add.
        var gain = augment.Exposure * augment.Contrast;
        var offset = augment.Brightness + 128 * (1 - augment.Contrast);
        if (Math.Abs(gain - 1) > 1e-9 || Math.Abs(offset) > 1e-9)
            image.ConvertTo(image, -1, gain, offset);

        if (Math.Abs(augment.Gamma - 1) > 1e-9)
        {
            var table = new byte[256];
            for (var value = 0; value < 256; value++)
                table[value] = (byte)Math.Clamp(Math.Round(255 * Math.Pow(value / 255.0, augment.Gamma)), 0, 255);
            using var lut = Mat.FromArray(table);
            Cv2.LUT(image, lut, image);
        }

        if (color && !plan.Grayscale
            && (Math.Abs(augment.HueShiftDegrees) > 1e-9 || Math.Abs(augment.SaturationScale - 1) > 1e-9))
            ShiftHueAndSaturation(image, augment);

        if (augment.BlurSigma >= 0.3)
            Cv2.GaussianBlur(image, image, new Size(0, 0), augment.BlurSigma);

        var motionLength = (int)Math.Round(augment.MotionBlurLength);
        if (motionLength >= 3)
            MotionBlur(image, motionLength, augment.MotionBlurAngleDegrees);

        if (augment.NoiseSigma >= 0.5)
            AddNoise(image, augment);
    }

    private static void ShiftHueAndSaturation(Mat image, AugmentParams augment)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        var channels = Cv2.Split(hsv);
        try
        {
            // 8-bit hue runs 0..179, so a shift of d degrees is d / 2 steps, wrapped around.
            var shift = (int)Math.Round(augment.HueShiftDegrees / 2);
            var table = new byte[256];
            for (var value = 0; value < 256; value++)
                table[value] = (byte)(value < 180 ? (((value + shift) % 180) + 180) % 180 : value);
            using var lut = Mat.FromArray(table);
            Cv2.LUT(channels[0], lut, channels[0]);
            channels[1].ConvertTo(channels[1], -1, augment.SaturationScale, 0);
            Cv2.Merge(channels, hsv);
            Cv2.CvtColor(hsv, image, ColorConversionCodes.HSV2BGR);
        }
        finally
        {
            foreach (var channel in channels)
                channel.Dispose();
        }
    }

    private static void MotionBlur(Mat image, int length, double angleDegrees)
    {
        using var kernel = new Mat(length, length, MatType.CV_32FC1, Scalar.All(0));
        var center = (length - 1) / 2.0;
        var radians = angleDegrees * Math.PI / 180;
        var dx = Math.Cos(radians) * center;
        var dy = Math.Sin(radians) * center;
        Cv2.Line(
            kernel,
            new Point((int)Math.Round(center - dx), (int)Math.Round(center - dy)),
            new Point((int)Math.Round(center + dx), (int)Math.Round(center + dy)),
            Scalar.All(1));
        var sum = Cv2.Sum(kernel).Val0;
        if (sum <= 0) return;

        kernel.ConvertTo(kernel, -1, 1 / sum, 0);
        Cv2.Filter2D(image, image, -1, kernel, borderType: BorderTypes.Replicate);
    }

    private static void AddNoise(Mat image, AugmentParams augment)
    {
        // OpenCV's generator is global state; ours is seeded per picture, so the noise is repeatable.
        // Four uniform 16-bit draws summed are close enough to a Gaussian for image noise: variance 1/3.
        var random = new DeterministicRandom(unchecked((ulong)augment.NoiseSeed));
        var scale = augment.NoiseSigma * Math.Sqrt(3);
        var samples = new short[image.Width * image.Height * image.Channels()];
        for (var index = 0; index < samples.Length; index++)
        {
            var bits = random.NextUInt64();
            var sum = ((bits & 0xFFFF) + ((bits >> 16) & 0xFFFF) + ((bits >> 32) & 0xFFFF) + (bits >> 48)) / 65535.0 - 2.0;
            samples[index] = (short)Math.Round(sum * scale);
        }

        var wideType = MatType.MakeType(MatType.CV_16S, image.Channels());
        // SetArray wants a single-channel Mat; reshaping afterwards gives one sample per channel value.
        using var flat = new Mat(image.Height, image.Width * image.Channels(), MatType.CV_16SC1);
        flat.SetArray(samples);
        using var noise = flat.Reshape(image.Channels());
        using var wide = new Mat();
        image.ConvertTo(wide, wideType);
        Cv2.Add(wide, noise, wide);
        wide.ConvertTo(image, MatType.MakeType(MatType.CV_8U, image.Channels()));
    }

    private static void ApplyCutouts(Mat image, AugmentParams augment)
    {
        foreach (var cutout in augment.Cutouts)
        {
            var left = Math.Clamp((int)Math.Round(cutout.X * image.Width), 0, image.Width);
            var top = Math.Clamp((int)Math.Round(cutout.Y * image.Height), 0, image.Height);
            var right = Math.Clamp((int)Math.Round((cutout.X + cutout.Width) * image.Width), 0, image.Width);
            var bottom = Math.Clamp((int)Math.Round((cutout.Y + cutout.Height) * image.Height), 0, image.Height);
            if (right <= left || bottom <= top) continue;

            using var region = new Mat(image, new Rect(left, top, right - left, bottom - top));
            region.SetTo(Scalar.All(RenderPlan.CutoutValue));
        }
    }

    private static Mat Recompress(Mat image, int quality)
    {
        Cv2.ImEncode(".jpg", image, out var buffer, new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, quality) });
        return Cv2.ImDecode(buffer, ImreadModes.Unchanged);
    }

    private static Mat Decode(byte[] bytes)
    {
        Mat decoded;
        try
        {
            decoded = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
        }
        catch (OpenCVException exception)
        {
            throw new ImageReadException($"Không giải mã được ảnh: {exception.Message}", exception);
        }

        if (decoded is null || decoded.Empty())
        {
            decoded?.Dispose();
            throw new ImageReadException("Không giải mã được ảnh: file hỏng hoặc sai định dạng.");
        }

        using (decoded)
        {
            using var eightBit = BitDepth.ToEightBit(decoded);
            if (eightBit.Channels() != 4)
                return eightBit.Clone();

            var flat = new Mat();
            Cv2.CvtColor(eightBit, flat, ColorConversionCodes.BGRA2BGR);
            return flat;
        }
    }

    private static byte[] ReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImageReadException($"Không đọc được file: {exception.Message}", exception);
        }
    }
}

