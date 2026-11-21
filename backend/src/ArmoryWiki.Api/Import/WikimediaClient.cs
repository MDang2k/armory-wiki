using System.Net;
using System.Text.Json;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// Sequential, polite HTTP for Wikimedia APIs: a descriptive User-Agent with the project's public contact URL
/// (never a personal address), <c>maxlag</c> on API calls, and backoff on 429/503 and maxlag answers.
/// </summary>
public sealed class WikimediaClient(HttpClient http, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const string DefaultContact = "https://github.com/MDang2k/armory-wiki";
    private const int MaxAttempts = 6;
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
        using var doc = await SendAsync(
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
            using var doc = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
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

    private async Task<JsonDocument> SendAsync(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.SendAsync(request(), ct);
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                await BackOffAsync(attempt, retryAfter, $"HTTP {(int)response.StatusCode}", ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
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

                throw new InvalidOperationException($"Wikidata API error: {code}");
            }

            return doc;
        }
    }

    private Task BackOffAsync(int attempt, TimeSpan? retryAfter, string reason, CancellationToken ct) =>
        attempt >= MaxAttempts
            ? throw new HttpRequestException($"Gave up after {attempt} attempts ({reason}).")
            : _delay(retryAfter ?? TimeSpan.FromSeconds(5 * attempt), ct);
}
