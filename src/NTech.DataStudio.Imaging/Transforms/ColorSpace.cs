using NTech.DataStudio.Application.Prepare;
using OpenCvSharp;

namespace NTech.DataStudio.Imaging.Transforms;

public static class ColorSpace
{
    public static Mat ToGray(Mat image)
    {
        switch (image.Channels())
        {
            case 1:
                return image.Clone();
            case 4:
            {
                var gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGRA2GRAY);
                return gray;
            }
            default:
            {
                var gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                return gray;
            }
        }
    }

    /// <summary>Output has no alpha: 3 channels, or 1 for a mono image unless tripled.</summary>
    public static Mat ForMaster(Mat image, MonoMode mono)
    {
        var output = new Mat();
        switch (image.Channels())
        {
            case 1 when mono == MonoMode.KeepOneChannel:
                image.CopyTo(output);
                break;
            case 1:
                Cv2.CvtColor(image, output, ColorConversionCodes.GRAY2BGR);
                break;
            case 4:
                Cv2.CvtColor(image, output, ColorConversionCodes.BGRA2BGR);
                break;
            default:
                image.CopyTo(output);
                break;
        }

        return output;
    }
}
