using System.Net;
using System.Text.Json;
using ArmoryWiki.Infrastructure.Media;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// Sequential, polite HTTP for Wikimedia APIs: a descriptive User-Agent with the project's public contact URL
/// (never a personal address), <c>maxlag</c> on API calls, and backoff on 429/503 and maxlag answers.
/// </summary>
public sealed class WikimediaClient(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const string DefaultContact = "https://github.com/MDang2k/armory-wiki";
    /// <summary>Width of the Commons rendition the importer downloads; smaller originals come back unscaled.</summary>
    public const int CommonsWidth = 2560;
    private const int MaxAttempts = 6;
    /// <summary>Bounds a whole request, body included: HttpClient.Timeout stops at the headers when streaming.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public static HttpClient CreateHttpClient(string contact, HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ArmoryWiki-importer/1.0 ({contact})");
        client.Timeout = TimeSpan.FromMinutes(2);
        return client;
    }

    /// <summary>QIDs a SPARQL query selects in its first variable.</summary>
    public async Task<List<string>> SparqlQidsAsync(string query, CancellationToken ct)
    {
        using var doc = await GetJsonAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "https://query.wikidata.org/sparql")
            {
                Content = new FormUrlEncodedContent([new("query", query), new("format", "json")]),
            },
            ct);
        var variable = doc.RootElement.GetProperty("head").GetProperty("vars")[0].GetString()!;
        return [.. doc.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray()
            .Select(b => b.GetProperty(variable).GetProperty("value").GetString()!.Split('/')[^1])
            .Distinct()];
    }

    /// <summary>wbgetentities in batches of 50 (the API maximum), keyed by the requested QID.</summary>
    public async Task<Dictionary<string, WikidataEntity?>> GetEntitiesAsync(IEnumerable<string> qids, CancellationToken ct)
    {
        var result = new Dictionary<string, WikidataEntity?>();
        var pending = new Queue<string[]>(qids.Distinct().Chunk(50));
        while (pending.TryDequeue(out var batch))
        {
            var url = "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&maxlag=5"
                + "&props=labels%7Caliases%7Cclaims%7Csitelinks&languages=en%7Cmul&sitefilter=enwiki&ids="
                + string.Join("%7C", batch);
            using var doc = await GetJsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            foreach (var (qid, entity) in WikidataEntity.ParseResponse(doc.RootElement))
            {
                result[qid] = entity;
            }

            // Answers over the API's size limit (12 MB: fifty countries easily) come back truncated, with only a
            // warning. Ask again for what is missing, in halves.
            var missing = batch.Where(q => !result.ContainsKey(q)).ToArray();
            if (missing.Length == 1 && batch.Length == 1)
            {
                throw new InvalidOperationException($"Wikidata answered without {missing[0]}.");
            }

            foreach (var half in missing.Chunk(Math.Max(1, (missing.Length + 1) / 2)))
            {
                pending.Enqueue(half);
            }
        }

        return result;
    }

    /// <summary>
    /// Commons file metadata (license, author, a <see cref="CommonsWidth"/>-wide rendition) in batches of 50,
    /// keyed by the file name as asked for (without "File:"). Missing files are null.
    /// </summary>
    public async Task<Dictionary<string, CommonsFile?>> GetCommonsFilesAsync(IEnumerable<string> fileNames, CancellationToken ct)
    {
        var result = new Dictionary<string, CommonsFile?>();
        var pending = new Queue<string[]>(fileNames.Distinct().Chunk(50));
        while (pending.TryDequeue(out var batch))
        {
            using var doc = await GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, "https://commons.wikimedia.org/w/api.php")
                {
                    // POST: fifty titles do not fit a URL.
                    Content = new FormUrlEncodedContent([
                        new("action", "query"), new("format", "json"), new("formatversion", "2"), new("maxlag", "5"), new("redirects", "1"),
                        new("prop", "imageinfo"), new("iiprop", "url|size|mime|extmetadata"),
                        new("iiurlwidth", CommonsWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new("iiextmetadatafilter", "License|LicenseShortName|Artist"),
                        new("titles", string.Join("|", batch.Select(n => "File:" + n))),
                    ]),
                },
                ct);
            foreach (var (name, file) in CommonsFile.ParseResponse(doc.RootElement))
            {
                result[name] = file;
            }

            AskAgainForMissing(batch, result, pending);
        }

        return result;
    }

    /// <summary>
    /// A batch cut short by the result-size limit leaves pages out: ask again for those, in halves. A title still
    /// unanswered on its own (an interwiki or special title is reported elsewhere, never as a page) counts as missing.
    /// </summary>
    private static void AskAgainForMissing<T>(string[] batch, Dictionary<string, T?> result, Queue<string[]> pending)
        where T : class
    {
        var missing = batch.Where(n => !result.ContainsKey(n)).ToArray();
        if (batch.Length == 1)
        {
            foreach (var name in missing)
            {
                result[name] = null;
            }

            return;
        }

        foreach (var half in missing.Chunk(Math.Max(1, (missing.Length + 1) / 2)))
        {
            pending.Enqueue(half);
        }
    }

    /// <summary>
    /// The latest revision of English Wikipedia articles in batches of 50, keyed by the title as asked for
    /// (redirects followed: <see cref="ArticleRevision.Title"/> is where the article lives now). Missing pages are null.
    /// </summary>
    public async Task<Dictionary<string, ArticleRevision?>> GetLatestRevisionsAsync(IEnumerable<string> titles, CancellationToken ct)
    {
        var result = new Dictionary<string, ArticleRevision?>();
        var pending = new Queue<string[]>(titles.Distinct().Chunk(50));
        while (pending.TryDequeue(out var batch))
        {
            using var doc = await GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, "https://en.wikipedia.org/w/api.php")
                {
                    Content = new FormUrlEncodedContent([
                        new("action", "query"), new("format", "json"), new("formatversion", "2"), new("maxlag", "5"),
                        new("redirects", "1"), new("prop", "revisions"), new("rvprop", "ids"),
                        new("titles", string.Join("|", batch)),
                    ]),
                },
                ct);
            var query = doc.RootElement.GetProperty("query");
            var asked = new AskedTitles(query);
            foreach (var page in query.GetProperty("pages").EnumerateArray())
            {
                ArticleRevision? revision = null;
                if (page.TryGetProperty("revisions", out var revisions) && revisions.GetArrayLength() > 0)
                {
                    revision = new ArticleRevision(page.GetProperty("title").GetString()!, revisions[0].GetProperty("revid").GetInt64());
                }
                else if (!page.TryGetProperty("missing", out _) && !page.TryGetProperty("invalid", out _))
                {
                    continue; // left out of a truncated answer: asked for again below
                }

                foreach (var title in asked.For(page.GetProperty("title").GetString()!))
                {
                    result[title] = revision;
                }
            }

            AskAgainForMissing(batch, result, pending);
        }

        return result;
    }

    /// <summary>
    /// An article's Parsoid HTML and the revision it renders (Wikimedia core REST API). Failures of this one
    /// article (status, timeout, broken connection, garbled answer) surface as <see cref="HttpRequestException"/>.
    /// </summary>
    public Task<(long RevisionId, string Html)> GetArticleHtmlAsync(string title, CancellationToken ct)
    {
        var url = "https://en.wikipedia.org/w/rest.php/v1/page/" + Uri.EscapeDataString(title.Replace(' ', '_')) + "/with_html";
        return OneItemAsync(
            async () =>
            {
                using var doc = await GetJsonAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
                return (doc.RootElement.GetProperty("latest").GetProperty("id").GetInt64(), doc.RootElement.GetProperty("html").GetString()!);
            },
            ct);
    }

    /// <summary>
    /// Downloads a file, refusing anything larger than the image processor accepts. Failures of this one file
    /// (status, timeout, broken connection) surface as <see cref="HttpRequestException"/>.
    /// </summary>
    public async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
    {
        using var deadline = Deadline(ct);
        return await OneItemAsync(() => DownloadWithinAsync(url, deadline.Token), ct);
    }

    /// <summary>Turns the ways fetching one item can fail into <see cref="HttpRequestException"/>, so callers skip just it.</summary>
    private static async Task<T> OneItemAsync<T>(Func<Task<T>> fetch, CancellationToken ct)
    {
        try
        {
            return await fetch();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException($"Timed out after {RequestTimeout.TotalMinutes:0} minutes.");
        }
        catch (IOException e)
        {
            throw new HttpRequestException($"Connection failed: {e.Message}", e);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new HttpRequestException($"Unexpected answer: {e.Message}", e);
        }
    }

    private async Task<byte[]> DownloadWithinAsync(string url, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (response.Content.Headers.ContentLength > ImageProcessor.MaxBytes)
        {
            throw new InvalidImageException($"Larger than {ImageProcessor.MaxBytes / (1024 * 1024)} MB.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > ImageProcessor.MaxBytes)
            {
                throw new InvalidImageException($"Larger than {ImageProcessor.MaxBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var deadline = Deadline(ct);
            using var response = await SendAsync(request, deadline.Token);
            var retryAfter = response.Headers.RetryAfter?.Delta;
            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
            // maxlag: the API answers 200 with an error object when its replicas lag behind.
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.GetProperty("code").GetString();
                doc.Dispose();
                if (code == "maxlag")
                {
                    await BackOffAsync(attempt, retryAfter, "maxlag", ct);
                    continue;
                }

                throw new InvalidOperationException($"Wikimedia API error: {code}");
            }

            return doc;
        }
    }

    /// <summary>Sends, waiting and retrying on 429/503; any other failure status throws.</summary>
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await http.SendAsync(request(), HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                response.Dispose();
                await BackOffAsync(attempt, retryAfter, $"HTTP {(int)response.StatusCode}", ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} from {response.RequestMessage?.RequestUri?.Host}.", null, response.StatusCode);
            }

            return response;
        }
    }

    private static CancellationTokenSource Deadline(CancellationToken ct)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RequestTimeout);
        return deadline;
    }

    private Task BackOffAsync(int attempt, TimeSpan? retryAfter, string reason, CancellationToken ct) =>
        attempt >= MaxAttempts
            ? throw new HttpRequestException($"Gave up after {attempt} attempts ({reason}).")
            : _delay(retryAfter ?? TimeSpan.FromSeconds(5 * attempt), ct);
}

/// <summary>The latest revision of a Wikipedia article, under the title it lives at now.</summary>
public sealed record ArticleRevision(string Title, long RevisionId);
