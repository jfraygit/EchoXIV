using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace EchoRoleplay.Game;

/// Cropping and encoding a portrait, on this machine, before it goes anywhere.
public static class ImageProcessor
{
    /// What is uploaded, in pixels.
    public const int PortraitSize = 512;

    /// What an upload may weigh once encoded.
    private const int MaximumUploadBytes = 400 * 1024;

    /// Tried in order until the result fits.
    private static readonly long[] QualityLadder = [88L, 80L, 72L, 64L];

    /// Crops and encodes, at a chosen position and zoom within the cover-scaled source.
    public static byte[] ToJpeg(string sourcePath, int size, float panX = 0.5f, float panY = 0.5f, float zoom = 1f)
    {
        using var original = new Bitmap(sourcePath);

        var square = Math.Min(original.Width, original.Height);

        var effectiveZoom = MathF.Max(1f, zoom);
        var crop = Math.Max(1, (int)(square / effectiveZoom));

        var cropX = (int)(Math.Clamp(panX, 0f, 1f) * (original.Width - crop));
        var cropY = (int)(Math.Clamp(panY, 0f, 1f) * (original.Height - crop));
        var cropRect = new Rectangle(cropX, cropY, crop, crop);

        using var resized = new Bitmap(size, size);

        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(original, new Rectangle(0, 0, size, size), cropRect, GraphicsUnit.Pixel);
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

    /// Decodes a portrait and resizes it to something near the size it will be drawn at, as raw BGRA.
    public static (byte[] Pixels, int Side)? ToBgra(byte[] encoded, int maximumSide)
    {
        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            using var original = new Bitmap(stream);

            var side = Math.Min(maximumSide, Math.Min(original.Width, original.Height));

            if (side <= 0)
                return null;

            using var scaled = new Bitmap(side, side, PixelFormat.Format32bppArgb);

            using (var graphics = Graphics.FromImage(scaled))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);

                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;

                graphics.DrawImage(
                    original,
                    new Rectangle(0, 0, side, side),
                    0, 0, original.Width, original.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }

            var data = scaled.LockBits(
                new Rectangle(0, 0, side, side), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                var pixels = new byte[side * side * 4];
                var row = side * 4;

                for (var y = 0; y < side; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + (y * data.Stride), pixels, y * row, row);
                }

                return (pixels, side);
            }
            finally
            {
                scaled.UnlockBits(data);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// The pixel size of an image on disk, or null if it is not one.
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
