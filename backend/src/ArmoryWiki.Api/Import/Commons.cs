using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Import;

/// <summary>One Wikimedia Commons file as the imageinfo API describes it. Pure: no I/O.</summary>
public sealed record CommonsFile(
    string Name,
    string DownloadUrl,
    string PageUrl,
    string? LicenseCode,
    string? LicenseName,
    string? ArtistHtml)
{
    /// <summary>
    /// Parses a query/imageinfo response (formatversion=2, redirects=1), keyed by the file name as it was asked
    /// for: titles the API normalized or followed through a redirect (a renamed file) are mapped back. Missing
    /// files are null; pages a truncated answer left without imageinfo are left out, to be asked for again.
    /// </summary>
    public static Dictionary<string, CommonsFile?> ParseResponse(JsonElement root)
    {
        var query = root.GetProperty("query");
        var asked = new AskedTitles(query);

        var result = new Dictionary<string, CommonsFile?>();
        foreach (var page in query.GetProperty("pages").EnumerateArray())
        {
            var title = page.GetProperty("title").GetString()!;
            CommonsFile? file = null;
            if (page.TryGetProperty("imageinfo", out var infos) && infos.GetArrayLength() > 0)
            {
                var info = infos[0];
                var meta = info.GetProperty("extmetadata");
                file = new CommonsFile(
                    StripPrefix(title),
                    (info.TryGetProperty("thumburl", out var thumb) ? thumb : info.GetProperty("url")).GetString()!,
                    info.GetProperty("descriptionurl").GetString()!,
                    Meta(meta, "License"),
                    Meta(meta, "LicenseShortName"),
                    Meta(meta, "Artist"));
            }
            else if (!page.TryGetProperty("missing", out _))
            {
                continue;
            }

            foreach (var name in asked.For(title))
            {
                result[StripPrefix(name)] = file;
            }
        }

        return result;
    }

    private static string StripPrefix(string title) => title.StartsWith("File:", StringComparison.Ordinal) ? title[5..] : title;

    private static string? Meta(JsonElement meta, string key) =>
        meta.TryGetProperty(key, out var entry) && entry.TryGetProperty("value", out var value) ? value.GetString() : null;
}

/// <summary>Pure mapping of Commons metadata to our image fields (GLOSSARY.md: *image*, *license*).</summary>
public static partial class CommonsMapper
{
    /// <summary>
    /// The <see cref="ImageLicenses"/> id for a Commons license code ("cc-by-sa-4.0", "cc0", "pd"), or null when
    /// the license is not accepted. Ported licenses ("cc-by-sa-3.0-de") are other licenses and are refused too.
    /// </summary>
    public static string? License(string? code)
    {
        var id = code?.ToLowerInvariant() switch
        {
            "cc0" => "CC0-1.0",
            "pd" => "PD",
            { } c when CreativeCommons().Match(c) is { Success: true } m => $"CC-{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}",
            _ => null,
        };
        return ImageLicenses.Find(id)?.Id;
    }

    /// <summary>The author as plain text: Commons gives HTML (links, spans, entities).</summary>
    public static string PlainText(string? html) =>
        html is null ? "" : Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    /// <summary>
    /// The author line to store, or null when an attribution-requiring license names nobody. Only the Artist
    /// field counts: Credit describes provenance ("Own work", "Transferred from en.wikipedia"), not a person.
    /// </summary>
    public static string? Author(CommonsFile file, string licenseId) =>
        PlainText(file.ArtistHtml) is { Length: > 0 } artist ? artist
        : licenseId is "PD" or "CC0-1.0" ? "Unknown author"
        : null;

    [GeneratedRegex(@"^cc-(by|by-sa)-(\d\.\d)$")]
    private static partial Regex CreativeCommons();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
