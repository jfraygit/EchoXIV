using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace EchoMix.Plugin;

/// Center-crops a DJ-picked show/venue image to 16:9 and downsizes it to a small, fixed thumbnail entirely
/// client-side, before it's ever sent anywhere - keeps every listener's "View Live Shows" grid load fast
/// regardless of how big the DJ's own original file was, and means the relay only ever has to move/store a
/// few tens of KB per show, not an arbitrary photo.
public static class ShowImageProcessor
{
    public const int TargetWidth = 480;
    public const int TargetHeight = 270;

    public const int DjAvatarSize = 256;
    public const int DjBannerWidth = 1024;
    public const int DjBannerHeight = 199;

    private const long JpegQuality = 80L;

    /// Show/venue images: fixed 16:9 at TargetWidth/TargetHeight, centered.
    public static byte[] ProcessToJpeg(string sourcePath) => ProcessToJpeg(sourcePath, TargetWidth, TargetHeight);

    /// General form, centered - crops to whatever aspect ratio (targetWidth, targetHeight) implies (square
    /// for a DJ List avatar, 3:1 for its banner, 16:9 for a show image) and resizes to that exact pixel size,
    /// so every caller gets a uniformly-sized, predictable image regardless of what the DJ originally picked.
    public static byte[] ProcessToJpeg(string sourcePath, int targetWidth, int targetHeight) =>
        ProcessToJpeg(sourcePath, targetWidth, targetHeight, 0.5f, 0.5f);

    /// Same crop/resize/encode as the centered overload, but at a caller-chosen position within the
    /// "cover"-scaled source instead of always dead center - see ImageCropDialog, which is the only caller
    /// that ever passes anything other than (0.5, 0.5).
    public static byte[] ProcessToJpeg(string sourcePath, int targetWidth, int targetHeight, float panX, float panY) =>
        ProcessToJpeg(sourcePath, targetWidth, targetHeight, panX, panY, 1f);

    /// Same as the panX/panY overload, but at a caller-chosen zoom level too - see ImageCropDialog, whose own
    /// zoom is a magnification factor on top of "cover" (1 reproduces the cover-fit crop exactly, matching
    /// the dialog's own MinZoom).
    public static byte[] ProcessToJpeg(string sourcePath, int targetWidth, int targetHeight, float panX, float panY, float zoom)
    {
        using var original = new Bitmap(sourcePath);

        var targetAspect = targetWidth / (float)targetHeight;
        var sourceAspect = original.Width / (float)original.Height;

        int cropWidth, cropHeight;
        if (sourceAspect > targetAspect)
        {
            cropHeight = original.Height;
            cropWidth = (int)(cropHeight * targetAspect);
        }
        else
        {
            cropWidth = original.Width;
            cropHeight = (int)(cropWidth / targetAspect);
        }

        var effectiveZoom = MathF.Max(1f, zoom);
        cropWidth = Math.Max(1, (int)(cropWidth / effectiveZoom));
        cropHeight = Math.Max(1, (int)(cropHeight / effectiveZoom));

        var maxOffsetX = original.Width - cropWidth;
        var maxOffsetY = original.Height - cropHeight;
        var cropX = (int)(Math.Clamp(panX, 0f, 1f) * maxOffsetX);
        var cropY = (int)(Math.Clamp(panY, 0f, 1f) * maxOffsetY);
        var cropRect = new Rectangle(cropX, cropY, cropWidth, cropHeight);

        using var resized = new Bitmap(targetWidth, targetHeight);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImage(original, new Rectangle(0, 0, targetWidth, targetHeight), cropRect, GraphicsUnit.Pixel);
        }

        using var stream = new MemoryStream();
        var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        resized.Save(stream, jpegEncoder, encoderParams);
        return stream.ToArray();
    }
}
