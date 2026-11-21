using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using ArmoryWiki.Api.Import;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// Answers Wikidata's SPARQL and wbgetentities endpoints from <c>Fixtures/wikidata-entities.json</c> (real items,
/// trimmed to the properties the importer reads). No live network.
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

    public WikimediaClient Client() => new(new HttpClient(this), (_, _) => Task.CompletedTask);

    public JsonObject Entity(string qid) => _entities[qid]!.AsObject();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
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
