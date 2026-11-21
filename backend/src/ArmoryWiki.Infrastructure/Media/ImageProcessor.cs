using System.Security.Cryptography;
using SkiaSharp;

namespace ArmoryWiki.Infrastructure.Media;

public sealed class InvalidImageException(string message) : Exception(message);

/// <summary>
/// The stored files for one uploaded picture. <see cref="Files"/> maps file names to bytes: the cleaned original
/// in its own format, plus WebP medium (longest side 1280 px) and thumb (320 px).
/// </summary>
public sealed record ProcessedImage(string Hash, string OriginalExtension, int Width, int Height, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>
/// Turns an upload into stored files. Only JPEG, PNG and WebP are accepted, judged by decoding the bytes rather
/// than trusting the file name. EXIF orientation is applied, then everything is re-encoded, which drops all
/// metadata (EXIF, GPS, camera serials).
/// </summary>
public static class ImageProcessor
{
    public const int MaxBytes = 20 * 1024 * 1024;
    public const int MediumSize = 1280;
    public const int ThumbSize = 320;
    // Refuses decompression bombs before decoding: 40 MP is about 160 MB per decoded copy.
    private const long MaxPixels = 40_000_000;

    public static ProcessedImage Process(byte[] upload)
    {
        if (upload.Length is 0 or > MaxBytes)
        {
            throw new InvalidImageException($"Images must be between 1 byte and {MaxBytes / 1024 / 1024} MB.");
        }

        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data) ?? throw new InvalidImageException("The file is not a readable image.");
        // The cleaned original keeps its format: PNG stays lossless, JPEG and WebP are re-encoded at high quality.
        var (extension, quality) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ("jpg", 92),
            SKEncodedImageFormat.Png => ("png", 100),
            SKEncodedImageFormat.Webp => ("webp", 92),
            var other => throw new InvalidImageException($"{other} images are not accepted; use JPEG, PNG or WebP."),
        };

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            throw new InvalidImageException("The image has too many pixels.");
        }

        using var decoded = SKBitmap.Decode(codec) ?? throw new InvalidImageException("The image could not be decoded.");
        using var upright = Orient(decoded, codec.EncodedOrigin);

        var original = Encode(upright, codec.EncodedFormat, quality);

        return new ProcessedImage(
            Convert.ToHexStringLower(SHA256.HashData(upload)),
            extension,
            upright.Width,
            upright.Height,
            new Dictionary<string, byte[]>
            {
                [$"original.{extension}"] = original,
                ["medium.webp"] = Encode(Shrink(upright, MediumSize), SKEncodedImageFormat.Webp, 85, dispose: true),
                ["thumb.webp"] = Encode(Shrink(upright, ThumbSize), SKEncodedImageFormat.Webp, 80, dispose: true),
            });
    }

    /// <summary>Applies the EXIF orientation so the stored pixels are upright.</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (source.Height, source.Width) : (source.Width, source.Height);
        var result = new SKBitmap(new SKImageInfo(w, h, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(result);
        // Transforms are applied to the drawn bitmap in reverse order of these calls.
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirrored
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // upside down
                canvas.Translate(w, h);
                canvas.Scale(-1, -1);
                break;
            case SKEncodedOrigin.BottomLeft: // flipped vertically
                canvas.Translate(0, h);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transposed
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // needs 90° clockwise
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transversed
                canvas.Translate(w, h);
                canvas.Scale(-1, -1);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftBottom: // needs 90° counter-clockwise
                canvas.Translate(0, h);
                canvas.RotateDegrees(-90);
                break;
        }

        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest)); // whole-pixel moves only
        return result;
    }

    /// <summary>Scales down so the longest side is at most <paramref name="max"/>; never scales up.</summary>
    private static SKBitmap Shrink(SKBitmap source, int max)
    {
        var scale = Math.Min(1.0, (double)max / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidImageException("The image could not be resized.");
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality, bool dispose = false)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(format, quality) ?? throw new InvalidImageException($"Could not encode {format}.");
            return encoded.ToArray();
        }
        finally
        {
            if (dispose)
            {
                bitmap.Dispose();
            }
        }
    }
}
