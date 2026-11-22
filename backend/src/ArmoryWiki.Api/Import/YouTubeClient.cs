using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ArmoryWiki.Api.Import;

/// <summary>One upload of a YouTube channel, reduced to what a suggested video stores (title only, no description).</summary>
public sealed record YouTubeUpload(string VideoId, string Title, string Channel, DateTime? PublishedAt);

/// <summary>
/// The YouTube Data API v3, read-only: a channel's uploads playlist, paged 50 at a time (1 quota unit per page out of
/// 10,000 a day; the whole Forgotten Weapons channel is about 60). The API key goes in the query string and never
/// into messages or reports.
/// </summary>
public sealed class YouTubeClient(HttpClient http, string apiKey, Func<TimeSpan, CancellationToken, Task>? wait = null)
{
    private const string Api = "https://www.googleapis.com/youtube/v3/";
    private const int MaxAttempts = 4;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = wait ?? Task.Delay;

    /// <summary>Every public upload of the channel with this handle (e.g. "@ForgottenWeapons"), newest first.</summary>
    public async Task<List<YouTubeUpload>> GetUploadsAsync(string handle, CancellationToken ct)
    {
        using var channel = await GetAsync($"channels?part=contentDetails&forHandle={Uri.EscapeDataString(handle)}", ct);
        if (!channel.RootElement.TryGetProperty("items", out var channels) || channels.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"YouTube has no channel {handle}.");
        }

        var playlist = channels[0].GetProperty("contentDetails").GetProperty("relatedPlaylists").GetProperty("uploads").GetString()!;
        var uploads = new List<YouTubeUpload>();
        string? page = null;
        do
        {
            using var doc = await GetAsync(
                $"playlistItems?part=snippet,contentDetails&maxResults=50&playlistId={Uri.EscapeDataString(playlist)}"
                + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page)),
                ct);
            uploads.AddRange(ParsePlaylistPage(doc.RootElement));
            page = doc.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (page is not null);

        return uploads;
    }

    /// <summary>
    /// The uploads on one playlistItems page. Private and deleted videos (listed without a publish date, titled
    /// "Private video" / "Deleted video") are left out. Pure: no I/O.
    /// </summary>
    public static IEnumerable<YouTubeUpload> ParsePlaylistPage(JsonElement page)
    {
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            var snippet = item.GetProperty("snippet");
            var details = item.TryGetProperty("contentDetails", out var d) ? d : default;
            if (details.ValueKind != JsonValueKind.Object || !details.TryGetProperty("videoPublishedAt", out var published))
            {
                continue;
            }

            yield return new YouTubeUpload(
                snippet.GetProperty("resourceId").GetProperty("videoId").GetString()!,
                snippet.GetProperty("title").GetString()!,
                snippet.TryGetProperty("videoOwnerChannelTitle", out var owner) ? owner.GetString()! : snippet.GetProperty("channelTitle").GetString()!,
                DateTime.Parse(published.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        }
    }

    private async Task<JsonDocument> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync(Api + pathAndQuery + "&key=" + Uri.EscapeDataString(apiKey), ct);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable
                && attempt < MaxAttempts)
            {
                await _delay(TimeSpan.FromSeconds(5 * attempt), ct);
                continue;
            }

            // Google's error object names the problem (invalid key, quota exceeded) without echoing the key; an
            // error page from the front end may not be JSON at all.
            string? message = null;
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                message = error.RootElement.GetProperty("error").GetProperty("message").GetString();
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
            }

            throw new HttpRequestException($"YouTube API: HTTP {(int)response.StatusCode}{(message is null ? "" : $" ({message})")}", null, response.StatusCode);
        }
    }
}
