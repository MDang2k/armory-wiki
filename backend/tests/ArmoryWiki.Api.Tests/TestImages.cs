using SkiaSharp;

namespace ArmoryWiki.Api.Tests;

/// <summary>Small generated images for upload tests: white with a red 4×4 block in the top-left corner.</summary>
internal static class TestImages
{
    public static byte[] Png(int width, int height) => Encode(width, height, SKEncodedImageFormat.Png);

    public static byte[] Jpeg(int width, int height) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    /// <summary>A 1×1 GIF: a real image, in a format the site does not accept.</summary>
    public static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>
    /// Inserts a minimal EXIF block (APP1) right after the JPEG start marker, holding one tag: Orientation.
    /// Skia cannot write EXIF, so it is built by hand: "Exif\0\0", a big-endian TIFF header, one IFD entry.
    /// </summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, length 34
            (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 0, 0x08, // TIFF header, first IFD at offset 8
            0x00, 0x01,                                     // one entry
            0x01, 0x12, 0x00, 0x03, 0, 0, 0, 0x01,          // tag 0x0112 Orientation, SHORT, count 1
            (byte)(orientation >> 8), (byte)orientation, 0, 0,
            0, 0, 0, 0,                                     // no next IFD
        ];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 4, 4, red);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    public static bool IsRed(SKColor c) => c.Red > 180 && c.Green < 100 && c.Blue < 100;
}
