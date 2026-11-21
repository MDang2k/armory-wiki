using System.Text;
using ArmoryWiki.Infrastructure.Media;
using SkiaSharp;

namespace ArmoryWiki.Api.Tests;

public sealed class ImageProcessorTests
{
    /// <summary>
    /// The source is 40×20 with a red block in its top-left corner. Each EXIF orientation says how the stored pixels
    /// must be turned to display upright; after processing, the red corner must be where the viewer would see it.
    /// </summary>
    [Theory]
    [InlineData(1, 40, 20, "top-left")]     // as stored
    [InlineData(2, 40, 20, "top-right")]    // mirrored
    [InlineData(3, 40, 20, "bottom-right")] // upside down
    [InlineData(4, 40, 20, "bottom-left")]  // flipped vertically
    [InlineData(5, 20, 40, "top-left")]     // transposed
    [InlineData(6, 20, 40, "top-right")]    // rotated 90° clockwise
    [InlineData(7, 20, 40, "bottom-right")] // transversed
    [InlineData(8, 20, 40, "bottom-left")]  // rotated 90° counter-clockwise
    public void Exif_orientation_is_applied_and_the_metadata_dropped(ushort orientation, int width, int height, string redCorner)
    {
        var upload = TestImages.WithExifOrientation(TestImages.Jpeg(40, 20), orientation);
        Assert.Contains("Exif", Encoding.ASCII.GetString(upload));

        var result = ImageProcessor.Process(upload);

        Assert.Equal((width, height), (result.Width, result.Height));
        var original = result.Files["original.jpg"];
        Assert.DoesNotContain("Exif", Encoding.ASCII.GetString(original));
        using var stored = SKBitmap.Decode(original);
        var corners = new Dictionary<string, SKColor>
        {
            ["top-left"] = stored.GetPixel(1, 1),
            ["top-right"] = stored.GetPixel(width - 2, 1),
            ["bottom-left"] = stored.GetPixel(1, height - 2),
            ["bottom-right"] = stored.GetPixel(width - 2, height - 2),
        };
        Assert.Equal([redCorner], corners.Where(c => TestImages.IsRed(c.Value)).Select(c => c.Key));
    }

    [Fact]
    public void Medium_and_thumb_are_webp_and_never_upscaled()
    {
        var result = ImageProcessor.Process(TestImages.Png(2000, 1000));

        using var medium = SKCodec.Create(SKData.CreateCopy(result.Files["medium.webp"]));
        using var thumb = SKCodec.Create(SKData.CreateCopy(result.Files["thumb.webp"]));
        Assert.Equal(SKEncodedImageFormat.Webp, medium.EncodedFormat);
        Assert.Equal((1280, 640), (medium.Info.Width, medium.Info.Height));
        Assert.Equal((320, 160), (thumb.Info.Width, thumb.Info.Height));

        using var small = SKCodec.Create(SKData.CreateCopy(ImageProcessor.Process(TestImages.Png(100, 50)).Files["medium.webp"]));
        Assert.Equal((100, 50), (small.Info.Width, small.Info.Height));
    }

    [Fact]
    public void The_hash_is_the_sha256_of_the_upload_so_identical_files_share_storage()
    {
        var bytes = TestImages.Png(10, 10);

        Assert.Equal(ImageProcessor.Process(bytes).Hash, ImageProcessor.Process([.. bytes]).Hash);
        Assert.Equal(64, ImageProcessor.Process(bytes).Hash.Length);
    }

    [Fact]
    public void Formats_are_judged_by_content_not_name()
    {
        Assert.Throws<InvalidImageException>(() => ImageProcessor.Process(TestImages.Gif()));
        Assert.Throws<InvalidImageException>(() => ImageProcessor.Process(Encoding.UTF8.GetBytes("not an image at all")));
        Assert.Throws<InvalidImageException>(() => ImageProcessor.Process([]));
    }
}
