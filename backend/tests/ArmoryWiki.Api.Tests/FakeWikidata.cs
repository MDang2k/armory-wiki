using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using ArmoryWiki.Api.Import;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// Answers Wikidata's SPARQL and wbgetentities endpoints from <c>Fixtures/wikidata-entities.json</c> (real items,
/// trimmed to the properties the importer reads), Commons imageinfo from <c>Fixtures/commons-files.json</c>, and
/// file downloads with generated JPEGs. No live network.
/// </summary>
public sealed class FakeWikidata : HttpMessageHandler
{
    public static readonly string[] FixtureFirearms = ["Q37116", "Q204046", "Q983967"]; // AK-47, M1 Garand, Mannlicher M1895

    private readonly JsonObject _entities =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wikidata-entities.json")))!["entities"]!.AsObject();

    /// <summary>What the small-arms query finds.</summary>
    public List<string> Discovered { get; } = [.. FixtureFirearms];
    /// <summary>Merged items: old QID → the item it was merged into.</summary>
    public Dictionary<string, string> Redirects { get; } = [];
    public List<string> Requests { get; } = [];
    /// <summary>Imitates the API's result-size limit: answers hold at most this many entities.</summary>
    public int MaxEntitiesPerAnswer { get; set; } = int.MaxValue;

    private readonly JsonArray _commonsPages =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "commons-files.json")))!["query"]!["pages"]!.AsArray();

    /// <summary>Bytes served for a download URL; any other URL gets a small JPEG unique to that URL.</summary>
    public Dictionary<string, byte[]> Downloads { get; } = [];
    /// <summary>Download URLs that answer 404.</summary>
    public HashSet<string> BrokenDownloads { get; } = [];

    /// <summary>A Commons file's imageinfo entry (without "File:"), to adjust before a run.</summary>
    public JsonObject CommonsFile(string name) =>
        _commonsPages.Single(p => p!["title"]!.GetValue<string>() == "File:" + name)!["imageinfo"]![0]!.AsObject();

    /// <summary>English Wikipedia articles by title: latest revision and Parsoid HTML (M1 Garand from the fixture).</summary>
    public Dictionary<string, (long RevisionId, string Html)> Articles { get; } = new()
    {
        ["M1 Garand"] = (1386668058, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wikipedia-m1-garand.html"))),
    };

    /// <summary>Articles whose revision lookup works but whose HTML answers 404.</summary>
    public HashSet<string> BrokenArticles { get; } = [];
    /// <summary>Titles the revision lookup leaves out of its pages (interwiki or special titles).</summary>
    public HashSet<string> UnlistedTitles { get; } = [];

    public WikimediaClient Client() => new(new HttpClient(this), (_, _) => Task.CompletedTask);

    public JsonObject Entity(string qid) => _entities[qid]!.AsObject();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        if (request.RequestUri.Host == "commons.wikimedia.org")
        {
            var titles = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken))["titles"]!.Split('|');
            var pages = new JsonArray([.. titles.Select(t =>
                _commonsPages.FirstOrDefault(p => p!["title"]!.GetValue<string>() == t)?.DeepClone()
                ?? new JsonObject { ["ns"] = 6, ["title"] = t, ["missing"] = true })]);
            return Json(new JsonObject { ["batchcomplete"] = true, ["query"] = new JsonObject { ["pages"] = pages } });
        }

        if (request.RequestUri.Host == "en.wikipedia.org" && request.RequestUri.AbsolutePath == "/w/api.php")
        {
            var titles = HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken))["titles"]!.Split('|');
            var pages = new JsonArray([.. titles.Where(t => !UnlistedTitles.Contains(t)).Select(t => (JsonNode)(Articles.TryGetValue(t, out var a)
                ? new JsonObject { ["title"] = t, ["revisions"] = new JsonArray(new JsonObject { ["revid"] = a.RevisionId }) }
                : new JsonObject { ["title"] = t, ["missing"] = true }))]);
            return Json(new JsonObject { ["batchcomplete"] = true, ["query"] = new JsonObject { ["pages"] = pages } });
        }

        if (request.RequestUri.Host == "en.wikipedia.org")
        {
            // /w/rest.php/v1/page/{title}/with_html
            var title = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Split('/')[^2]).Replace('_', ' ');
            return !BrokenArticles.Contains(title) && Articles.TryGetValue(title, out var article)
                ? Json(new JsonObject { ["title"] = title, ["latest"] = new JsonObject { ["id"] = article.RevisionId }, ["html"] = article.Html })
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.RequestUri.Host == "upload.wikimedia.org")
        {
            var url = request.RequestUri.OriginalString;
            if (BrokenDownloads.Contains(url))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var size = 40 + (Math.Abs(url.GetHashCode(StringComparison.Ordinal)) % 200);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Downloads.GetValueOrDefault(url) ?? TestImages.Jpeg(size, size / 2)) };
        }

        if (request.RequestUri.Host == "query.wikidata.org")
        {
            Assert.Contains("wdt:P279*", HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken))["query"]);
            var bindings = new JsonArray([.. Discovered.Select(q => (JsonNode)new JsonObject
            {
                ["item"] = new JsonObject { ["type"] = "uri", ["value"] = "http://www.wikidata.org/entity/" + q },
            })]);
            return Json(new JsonObject { ["head"] = new JsonObject { ["vars"] = new JsonArray("item") }, ["results"] = new JsonObject { ["bindings"] = bindings } });
        }

        var answer = new JsonObject();
        foreach (var qid in HttpUtility.ParseQueryString(request.RequestUri.Query)["ids"]!.Split('|').Take(MaxEntitiesPerAnswer))
        {
            var target = Redirects.GetValueOrDefault(qid, qid);
            if (_entities[target] is JsonObject entity)
            {
                var copy = entity.DeepClone().AsObject();
                if (target != qid)
                {
                    copy["redirects"] = new JsonObject { ["from"] = qid, ["to"] = target };
                }

                answer[qid] = copy;
            }
            else
            {
                answer[qid] = new JsonObject { ["id"] = qid, ["missing"] = "" };
            }
        }

        return Json(new JsonObject { ["entities"] = answer, ["success"] = 1 });
    }

    private static HttpResponseMessage Json(JsonNode body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
