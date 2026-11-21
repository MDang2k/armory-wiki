using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Common;

public sealed record LicenseDto(string Id, string Name, string Url);

/// <summary>An image with everything its attribution needs (author, license, source).</summary>
public sealed record ImageDto(
    string ThumbUrl,
    string MediumUrl,
    string OriginalUrl,
    int Width,
    int Height,
    string? Caption,
    string Author,
    LicenseDto License,
    string SourceUrl)
{
    /// <summary>Runs client-side inside EF projections.</summary>
    public static ImageDto From(Image i)
    {
        var license = ImageLicenses.Find(i.License) ?? new ImageLicense(i.License, i.License, "");
        return new(ImageUrls.Thumb(i.ContentHash), ImageUrls.Medium(i.ContentHash), ImageUrls.Original(i.ContentHash, i.OriginalExtension),
            i.Width, i.Height, i.Caption, i.Author, new LicenseDto(license.Id, license.Name, license.Url), i.SourceUrl);
    }
}

/// <summary>An approved YouTube video, embedded by id (never re-hosted).</summary>
public sealed record VideoDto(string YouTubeVideoId, string Title, string? Channel, DateTime? PublishedAt);

/// <summary>Where image files are served: /media/{hash}/{file}, by Caddy in production and the API in Development.</summary>
public static class ImageUrls
{
    public const string Prefix = "/media/";
    public const string ThumbFile = "/thumb.webp";

    public static string Thumb(string hash) => Prefix + hash + ThumbFile;
    public static string Medium(string hash) => $"{Prefix}{hash}/medium.webp";
    public static string Original(string hash, string extension) => $"{Prefix}{hash}/original.{extension}";
}

public static partial class YouTube
{
    private static readonly HashSet<string> Hosts =
        ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "www.youtube-nocookie.com", "youtube-nocookie.com"];

    /// <summary>
    /// Reads the 11-character video id from the usual YouTube links: watch?v=, youtu.be/, /embed/, /shorts/, /live/.
    /// </summary>
    public static bool TryParseId(string? url, [NotNullWhen(true)] out string? id)
    {
        id = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !Hosts.Contains(uri.Host))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidate = uri.Host == "youtu.be"
            ? segments.FirstOrDefault()
            : segments switch
            {
                ["watch"] => System.Web.HttpUtility.ParseQueryString(uri.Query)["v"],
                ["embed" or "shorts" or "live" or "v", var value, ..] => value,
                _ => null,
            };

        if (candidate is null || !VideoId().IsMatch(candidate))
        {
            return false;
        }

        id = candidate;
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{11}\z")]
    private static partial Regex VideoId();
}
