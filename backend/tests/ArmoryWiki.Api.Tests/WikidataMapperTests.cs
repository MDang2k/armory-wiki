using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArmoryWiki.Api.Import;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Tests;

public sealed class WikidataMapperTests
{
    private static readonly FakeWikidata Fixtures = new();

    private static WikidataEntity Item(string qid) => Parse(Fixtures.Entity(qid));

    private static WikidataEntity Parse(JsonNode node) => WikidataEntity.Parse(JsonDocument.Parse(node.ToJsonString()).RootElement);

    [Fact]
    public void Ak47_maps_to_metric_specs_and_an_unspecified_weight()
    {
        var facts = WikidataMapper.ToFirearm(Item("Q37116"));

        Assert.Equal("AK-47", facts.Name); // the item has only a language-neutral "mul" label
        Assert.Equal("AK-47", facts.WikipediaTitle);
        Assert.Equivalent(FuzzyDate.InYear(1947), facts.DesignDate);
        Assert.Equal("Q15180", facts.CountryQid); // Soviet Union
        Assert.Equal(["Q167573"], facts.DesignerQids);
        Assert.Equal(["Q259196"], facts.AmmunitionQids);
        Assert.Contains("Q177456", facts.ClassQids); // assault rifle
        Assert.Equal(870, facts.OverallLengthMm); // 87 cm
        Assert.Equal(715, facts.MuzzleVelocityMps);
        Assert.Equal((600, 600), (facts.RateOfFireMinRpm, facts.RateOfFireMaxRpm));
        // GLOSSARY.md: a weight that does not say what it includes is not an empty weight.
        Assert.Equal(new Dictionary<string, string> { [WikidataMapper.UnspecifiedWeight] = "4.3 kg" }, facts.ExtraSpecs);
    }

    [Fact]
    public void Several_cartridges_are_all_kept_and_a_missing_inception_stays_empty()
    {
        var facts = WikidataMapper.ToFirearm(Item("Q204046")); // M1 Garand: .30-06 and 7.62×51mm NATO

        Assert.Equal(["Q158721", "Q259210"], facts.AmmunitionQids);
        Assert.Null(facts.DesignDate);
        Assert.Equal(["Q1193204"], facts.ManufacturerQids);
    }

    [Fact]
    public void Historical_country_of_origin_is_read_from_the_item()
    {
        Assert.Equal("Q28513", WikidataMapper.ToFirearm(Item("Q983967")).CountryQid); // Austria-Hungary
        Assert.True(Item("Q28513").Has(Wd.Dissolved));
    }

    [Theory]
    [InlineData("+1947-00-00T00:00:00Z", 9, 1947, null, null, DatePrecision.Year, null)]
    [InlineData("+1942-03-00T00:00:00Z", 10, 1942, 3, null, DatePrecision.Month, null)]
    [InlineData("+1855-01-23T00:00:00Z", 11, 1855, 1, 23, DatePrecision.Day, null)]
    [InlineData("+1953-00-00T00:00:00Z", 8, 1950, null, null, DatePrecision.Decade, 1959)]
    public void Wikidata_precision_becomes_a_fuzzy_date(string time, int precision, int year, int? month, int? day, DatePrecision expected, int? yearTo)
    {
        var date = Parse(WithClaim(Wd.Inception, TimeValue(time, precision))).Date(Wd.Inception)!;

        Assert.Equal((year, month, day, expected, yearTo), (date.Year, date.Month, date.Day, date.Precision, date.YearTo));
    }

    [Fact]
    public void Dates_we_cannot_represent_are_skipped_and_circa_is_approximate()
    {
        Assert.Null(Parse(WithClaim(Wd.Inception, TimeValue("-0300-00-00T00:00:00Z", 9))).Date(Wd.Inception));
        Assert.Null(Parse(WithClaim(Wd.Inception, TimeValue("+1900-00-00T00:00:00Z", 7))).Date(Wd.Inception)); // century

        var circa = WithClaim(Wd.Inception, TimeValue("+1940-00-00T00:00:00Z", 9));
        circa["claims"]![Wd.Inception]![0]!["qualifiers"] = new JsonObject
        {
            [Wd.SourcingCircumstances] = new JsonArray(Snak(new JsonObject { ["id"] = Wd.Circa })),
        };
        Assert.True(Parse(circa).Date(Wd.Inception)!.IsApproximate);
    }

    [Fact]
    public void Old_style_julian_dates_become_gregorian()
    {
        var birth = TimeValue("+1849-05-12T00:00:00Z", 11);
        birth["calendarmodel"] = "http://www.wikidata.org/entity/" + Wd.JulianCalendar;

        var date = Parse(WithClaim(Wd.BirthDate, birth)).Date(Wd.BirthDate)!;

        Assert.Equal((1849, 5, 24), (date.Year, date.Month!.Value, date.Day!.Value));
    }

    [Fact]
    public void Conflicting_inceptions_are_reported()
    {
        var item = WithClaim(Wd.Inception, TimeValue("+1942-00-00T00:00:00Z", 9));
        item["claims"]![Wd.Inception]!.AsArray().Add(Statement(TimeValue("+1943-00-00T00:00:00Z", 9)));

        var facts = WikidataMapper.ToFirearm(Parse(item));

        Assert.Null(facts.DesignDate);
        Assert.Contains("design date: 2 values", facts.Skipped);
    }

    [Fact]
    public void Unknown_units_and_conflicting_values_are_skipped_and_reported()
    {
        var item = WithClaim(Wd.MuzzleVelocity, Amount("+900", "Q999999"));
        item["claims"]![Wd.Length] = new JsonArray(Statement(Amount("+100", "Q174789")), Statement(Amount("+110", "Q174789")));

        var facts = WikidataMapper.ToFirearm(Parse(item));

        Assert.Null(facts.MuzzleVelocityMps);
        Assert.Null(facts.OverallLengthMm);
        Assert.Contains("muzzle velocity: unknown unit Q999999", facts.Skipped);
        Assert.Contains("length: 2 values", facts.Skipped);
    }

    [Fact]
    public void Preferred_rank_wins_and_deprecated_is_ignored()
    {
        var item = WithClaim(Wd.CountryOfOrigin, new JsonObject { ["id"] = "Q1" });
        var statements = item["claims"]![Wd.CountryOfOrigin]!.AsArray();
        statements.Add(Statement(new JsonObject { ["id"] = "Q2" }, "preferred"));
        statements.Add(Statement(new JsonObject { ["id"] = "Q3" }, "deprecated"));

        Assert.Equal(["Q2"], Parse(item).Items(Wd.CountryOfOrigin));
    }

    [Fact]
    public void Merged_and_deleted_items_are_told_apart()
    {
        var response = JsonDocument.Parse("""
            {"entities":{
              "Q16043146":{"redirects":{"from":"Q16043146","to":"Q140"},"type":"item","id":"Q140","labels":{"en":{"language":"en","value":"lion"}}},
              "Q6":{"id":"Q6","missing":""}}}
            """).RootElement;

        var parsed = WikidataEntity.ParseResponse(response);

        Assert.Equal("Q140", parsed["Q16043146"]!.Qid);
        Assert.Null(parsed["Q6"]);
    }

    [Fact]
    public async Task Maxlag_and_429_answers_are_retried_after_backing_off()
    {
        var answers = new Queue<HttpResponseMessage>([
            new(HttpStatusCode.OK) { Content = new StringContent("""{"error":{"code":"maxlag","info":"Waiting"}}"""), Headers = { RetryAfter = new(TimeSpan.FromSeconds(7)) } },
            new(HttpStatusCode.TooManyRequests),
            new(HttpStatusCode.OK) { Content = new StringContent("""{"entities":{"Q6":{"id":"Q6","missing":""}}}""") },
        ]);
        var waits = new List<TimeSpan>();
        var http = WikimediaClient.CreateHttpClient(WikimediaClient.DefaultContact, new Answering(answers));
        var client = new WikimediaClient(http, (delay, _) => { waits.Add(delay); return Task.CompletedTask; });

        var result = await client.GetEntitiesAsync(["Q6"], CancellationToken.None);

        Assert.Null(result["Q6"]);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(5)], waits);
        Assert.Equal("ArmoryWiki-importer/1.0 (https://github.com/MDang2k/armory-wiki)", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task Truncated_answers_are_completed_by_asking_again()
    {
        var wikidata = new FakeWikidata { MaxEntitiesPerAnswer = 2 };
        string[] qids = ["Q37116", "Q204046", "Q983967", "Q15180", "Q28513"];

        var result = await wikidata.Client().GetEntitiesAsync(qids, CancellationToken.None);

        Assert.Equal(qids.Order(), result.Keys.Order());
        Assert.All(result.Values, Assert.NotNull);
    }

    private sealed class Answering(Queue<HttpResponseMessage> answers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Contains("maxlag=5", request.RequestUri!.Query);
            return Task.FromResult(answers.Dequeue());
        }
    }

    private static JsonObject WithClaim(string property, JsonNode value) => new()
    {
        ["id"] = "Q1",
        ["labels"] = new JsonObject { ["en"] = new JsonObject { ["language"] = "en", ["value"] = "Test rifle" } },
        ["claims"] = new JsonObject { [property] = new JsonArray(Statement(value)) },
    };

    private static JsonObject Statement(JsonNode value, string rank = "normal") =>
        new() { ["mainsnak"] = Snak(value), ["rank"] = rank };

    private static JsonObject Snak(JsonNode value) => new() { ["snaktype"] = "value", ["datavalue"] = new JsonObject { ["value"] = value } };

    private static JsonObject TimeValue(string time, int precision) => new() { ["time"] = time, ["precision"] = precision };

    private static JsonObject Amount(string amount, string unit) =>
        new() { ["amount"] = amount, ["unit"] = "http://www.wikidata.org/entity/" + unit };
}
