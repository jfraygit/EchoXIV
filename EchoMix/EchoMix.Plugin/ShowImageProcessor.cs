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
    public static byte[] ProcessToJpeg(string sourcePath, int targetWidth, int targetHeight, float panX, float panY, float zoom) =>
        ProcessToJpeg(sourcePath, targetWidth, targetHeight, panX, panY, zoom, 0f);

    /// A hair more zoom than the geometry strictly asks for.
    internal const float CoverMargin = 1.003f;

    /// Extra cover at a right angle, scaled by how far the picture is actually turned.
    internal const float RotatedPanRoom = 0.22f;

    /// What the cover is multiplied by.
    internal static float CoverFactor(float degrees) =>
        CoverMargin + (RotatedPanRoom * MathF.Abs(MathF.Sin(degrees * MathF.PI / 180f)));

    /// Crops, rotates and encodes in one resampling pass.
    public static byte[] ProcessToJpeg(
        string sourcePath, int targetWidth, int targetHeight, float panX, float panY, float zoom, float degrees)
    {
        using var original = new Bitmap(sourcePath);

        var radians = degrees * MathF.PI / 180f;
        var cos = MathF.Abs(MathF.Cos(radians));
        var sin = MathF.Abs(MathF.Sin(radians));

        var footprintWidth = (targetWidth * cos) + (targetHeight * sin);
        var footprintHeight = (targetWidth * sin) + (targetHeight * cos);

        var cover = MathF.Max(footprintWidth / original.Width, footprintHeight / original.Height)
                    * CoverFactor(degrees);

        var effective = cover * MathF.Max(1f, zoom);

        var scaledWidth = original.Width * effective;
        var scaledHeight = original.Height * effective;

        var offsetX = (Math.Clamp(panX, 0f, 1f) - 0.5f) * MathF.Max(0f, scaledWidth - footprintWidth);
        var offsetY = (Math.Clamp(panY, 0f, 1f) - 0.5f) * MathF.Max(0f, scaledHeight - footprintHeight);

        using var resized = new Bitmap(targetWidth, targetHeight);

        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.Clear(Color.Black);

            g.TranslateTransform(targetWidth / 2f, targetHeight / 2f);
            g.RotateTransform(degrees);
            g.TranslateTransform(-offsetX, -offsetY);

            g.DrawImage(original, new RectangleF(-scaledWidth / 2f, -scaledHeight / 2f, scaledWidth, scaledHeight));
        }

        using var stream = new MemoryStream();
        var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        resized.Save(stream, jpegEncoder, encoderParams);
        return stream.ToArray();
    }

    /// The source's pixel dimensions, or null when it is not an image this can open.
    public static (int Width, int Height)? Measure(string path)
    {
        try
        {
            using var image = Image.FromFile(path);
            return (image.Width, image.Height);
        }
        catch
        {
            return null;
        }
    }
}
