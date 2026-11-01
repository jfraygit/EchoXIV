using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace EchoGlam.Game;

/// Crops a picked image to a fixed size and encodes it, before anything is uploaded.
public static class ImageProcessor
{
    /// A glamour screenshot.
    public const int ShotWidth = 1200;
    public const int ShotHeight = 1600;

    /// The shot's aspect, as width over height.
    public const float ShotAspect = ShotWidth / (float)ShotHeight;

    /// How tall a glamour image is when drawn at this width.
    public static float HeightFor(float width) => width / ShotAspect;

    /// A profile avatar.
    public const int AvatarSize = 320;

    /// A profile banner.
    public const int BannerWidth = 1280;
    public const int BannerHeight = 280;

    /// What an upload may weigh once encoded.
    private const int MaximumUploadBytes = 500 * 1024;

    /// Tried in order until the result fits.
    private static readonly long[] QualityLadder = [84L, 76L, 68L, 60L];

    /// Crops and encodes, at a chosen position and zoom within the cover-scaled source.
    internal const float CoverMargin = 1.003f;

    /// Extra cover, as a fraction, at a right angle - scaled down by how far the picture is actually turned.
    internal const float RotatedPanRoom = 0.22f;

    /// What the cover is multiplied by.
    internal static float CoverFactor(float degrees) =>
        CoverMargin + (RotatedPanRoom * MathF.Abs(MathF.Sin(degrees * MathF.PI / 180f)));

    /// degrees turns the picture clockwise, any angle.
    public static byte[] ToJpeg(
        string sourcePath, int targetWidth, int targetHeight, float panX = 0.5f, float panY = 0.5f,
        float zoom = 1f, float degrees = 0f)
    {
        using var original = new Bitmap(sourcePath);
        return Encode(original, targetWidth, targetHeight, panX, panY, zoom, degrees);
    }

    public static byte[] ToJpeg(
        byte[] source, int targetWidth, int targetHeight, float panX = 0.5f, float panY = 0.5f,
        float zoom = 1f, float degrees = 0f)
    {
        using var stream = new MemoryStream(source);
        using var original = new Bitmap(stream);
        return Encode(original, targetWidth, targetHeight, panX, panY, zoom, degrees);
    }

    /// Crops, rotates and encodes, in one resampling pass.
    private static byte[] Encode(
        Bitmap original, int targetWidth, int targetHeight, float panX, float panY, float zoom, float degrees)
    {
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

        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            graphics.Clear(Color.Black);

            graphics.TranslateTransform(targetWidth / 2f, targetHeight / 2f);
            graphics.RotateTransform(degrees);
            graphics.TranslateTransform(-offsetX, -offsetY);

            graphics.DrawImage(
                original,
                new RectangleF(-scaledWidth / 2f, -scaledHeight / 2f, scaledWidth, scaledHeight));
        }

        var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        byte[]? last = null;

        foreach (var quality in QualityLadder)
        {
            using var buffer = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

            resized.Save(buffer, encoder, parameters);
            last = buffer.ToArray();

            if (last.Length <= MaximumUploadBytes)
                return last;
        }

        return last!;
    }

    /// The pixel size of an image on disk, without decoding all of it.
    public static (int Width, int Height)? Measure(string path)
    {
        try
        {
            using var bitmap = new Bitmap(path);
            return (bitmap.Width, bitmap.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
