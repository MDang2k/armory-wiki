using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Admin.Sources;
using ArmoryWiki.Api.Features.Cartridges;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Api.Features.People;
using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>Slice 3c through HTTP: sources, citations per subject, and footnotes on public pages.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AdminSourceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AdminClientAsync();

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await using var db = fixture.NewDbContext();
        await db.Citations.Where(c => c.Source.Title.StartsWith("Test ")).ExecuteDeleteAsync();
        await db.Sources.Where(s => s.Title.StartsWith("Test ")).ExecuteDeleteAsync();
        await db.People.IgnoreQueryFilters().Where(p => p.Slug.StartsWith("test-")).ExecuteDeleteAsync();
    }

    private async Task<AdminSourceDto> CreateSourceAsync(string title, string type = "Book") =>
        await ReadAsync<AdminSourceDto>(
            await _admin.PostAsJsonAsync("/api/admin/sources", new { type, title, author = "A. Author", year = 1999, url = "https://example.org/book" }),
            HttpStatusCode.Created);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, DatabaseFixture.Json)!;
    }

    private async Task<int> IdAsync(string path)
    {
        await using var db = fixture.NewDbContext();
        return path switch
        {
            "ak-47" => await db.Firearms.Where(f => f.Slug == path).Select(f => f.Id).SingleAsync(),
            "ak-47/base" => await db.Variants.Where(v => v.Firearm.Slug == "ak-47" && v.IsBaseVariant).Select(v => v.Id).SingleAsync(),
            "mikhail-kalashnikov" => await db.People.Where(p => p.Slug == path).Select(p => p.Id).SingleAsync(),
            "7-62x39mm" => await db.Cartridges.Where(c => c.Slug == path).Select(c => c.Id).SingleAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
    }

    [Fact]
    public async Task Sources_are_validated_versioned_and_cannot_be_deleted_while_cited()
    {
        var bad = await _admin.PostAsJsonAsync("/api/admin/sources", new { title = "Test No Type", url = "ftp://example.org" });
        var errors = (await ReadAsync<JsonElement>(bad, HttpStatusCode.BadRequest)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("type", out _));
        Assert.True(errors.TryGetProperty("url", out _));

        var source = await CreateSourceAsync("Test Small Arms of the World");
        var stale = await _admin.PutAsJsonAsync($"/api/admin/sources/{source.Id}?version={source.Version + 1}", source.Details, DatabaseFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        source = await ReadAsync<AdminSourceDto>(await _admin.PutAsJsonAsync(
            $"/api/admin/sources/{source.Id}?version={source.Version}", source.Details with { Publisher = "Test Press" }, DatabaseFixture.Json));
        Assert.Equal("Test Press", source.Details.Publisher);

        var person = await IdAsync("mikhail-kalashnikov");
        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/people/{person}/citations", new { sourceId = source.Id }), HttpStatusCode.Created);

        var cited = await _admin.DeleteAsync($"/api/admin/sources/{source.Id}?version={source.Version}");
        Assert.Contains("person mikhail-kalashnikov", (await ReadAsync<JsonElement>(cited, HttpStatusCode.Conflict)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Citations_are_listed_per_subject_removed_by_id_and_then_the_source_can_go()
    {
        var source = await CreateSourceAsync("Test Detail Source");
        Assert.Equal("Test Detail Source", (await _admin.GetFromJsonAsync<AdminSourceDto>($"/api/admin/sources/{source.Id}", DatabaseFixture.Json))!.Details.Title);
        var person = await IdAsync("mikhail-kalashnikov");

        var citation = await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/people/{person}/citations",
            new { sourceId = source.Id, fieldName = "birthDate", locator = "p. 3" }), HttpStatusCode.Created);
        var listed = await _admin.GetFromJsonAsync<List<AdminCitationDto>>($"/api/admin/people/{person}/citations", DatabaseFixture.Json);
        Assert.Contains(listed!, c => c.Id == citation.Id && c.SourceTitle == "Test Detail Source" && c.FieldName == "birthDate");

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/admin/citations/{citation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/admin/citations/{citation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/admin/sources/{source.Id}?version={source.Version}")).StatusCode);
    }

    [Fact]
    public async Task Citations_belong_to_their_subject_and_go_with_it()
    {
        var source = await CreateSourceAsync("Test Cascade Source");
        var person = await ReadAsync<JsonElement>(await _admin.PostAsJsonAsync("/api/admin/people", new { fullName = "Test Cited Person" }), HttpStatusCode.Created);
        var personId = person.GetProperty("id").GetInt32();
        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/people/{personId}/citations", new { sourceId = source.Id }), HttpStatusCode.Created);
        await using var db = fixture.NewDbContext();
        var colt = await db.Organizations.Where(o => o.Slug == "colts-manufacturing-company").Select(o => o.Id).SingleAsync();
        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/organizations/{colt}/citations", new { sourceId = source.Id }), HttpStatusCode.Created);

        Assert.Single((await fixture.GetJsonAsync<Features.Organizations.OrganizationDetailDto>("/api/organizations/colts-manufacturing-company")).Citations);

        var deleted = await _admin.DeleteAsync($"/api/admin/people/{personId}?version={person.GetProperty("version").GetUInt32()}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1, (await _admin.GetFromJsonAsync<AdminSourceDto>($"/api/admin/sources/{source.Id}", DatabaseFixture.Json))!.CitationCount);
    }

    [Fact]
    public async Task A_field_name_must_look_like_an_api_field()
    {
        var source = await CreateSourceAsync("Test Field Source");

        var response = await _admin.PostAsJsonAsync($"/api/admin/people/{await IdAsync("mikhail-kalashnikov")}/citations",
            new { sourceId = source.Id, fieldName = "birth date!" });

        Assert.True((await ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("fieldName", out _));
    }

    [Fact]
    public async Task Citations_appear_as_footnotes_on_public_pages_in_the_order_added()
    {
        var book = await CreateSourceAsync("Test The AK-47 Story");
        var patent = await CreateSourceAsync("Test Patent SU-123", "Patent");
        var firearm = await IdAsync("ak-47");
        var variant = await IdAsync("ak-47/base");
        var cartridge = await IdAsync("7-62x39mm");

        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/firearms/{firearm}/citations",
            new { sourceId = book.Id, fieldName = "designDate", locator = "p. 112" }), HttpStatusCode.Created);
        var onVariant = await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/variants/{variant}/citations",
            new { sourceId = patent.Id, fieldName = "magazineCapacityStandard", locator = "claim 3" }), HttpStatusCode.Created);
        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/cartridges/{cartridge}/citations",
            new { sourceId = book.Id }), HttpStatusCode.Created);

        var page = await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47");
        Assert.Equal(
            [("Test The AK-47 Story", "designDate", "p. 112", (string?)null), ("Test Patent SU-123", "magazineCapacityStandard", "claim 3", "ak-47")],
            page.Citations.Select(c => (c.Source.Title, c.FieldName, c.Locator, c.Variant)));
        Assert.Equal(SourceType.Patent, page.Citations[1].Source.Type);
        Assert.Single((await fixture.GetJsonAsync<CartridgeDetailDto>("/api/cartridges/7-62x39mm")).Citations);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/admin/citations/{onVariant.Id}")).StatusCode);
        Assert.Single((await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47")).Citations);
    }

    [Fact]
    public async Task Citing_needs_an_existing_subject_and_source()
    {
        var source = await CreateSourceAsync("Test Reference");

        var noSubject = await _admin.PostAsJsonAsync("/api/admin/people/999999/citations", new { sourceId = source.Id });
        var noSource = await _admin.PostAsJsonAsync($"/api/admin/people/{await IdAsync("mikhail-kalashnikov")}/citations", new { sourceId = 999999 });

        Assert.Equal(HttpStatusCode.NotFound, noSubject.StatusCode);
        Assert.Equal("Unknown source", (await ReadAsync<JsonElement>(noSource, HttpStatusCode.BadRequest)).GetProperty("title").GetString());
        Assert.Empty((await fixture.GetJsonAsync<PersonDetailDto>("/api/people/mikhail-kalashnikov")).Citations);
    }

    [Fact]
    public async Task Admin_source_list_searches_titles_and_counts_citations()
    {
        var source = await CreateSourceAsync("Test Counted Source");
        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"/api/admin/firearms/{await IdAsync("ak-47")}/citations",
            new { sourceId = source.Id }), HttpStatusCode.Created);

        var page = await _admin.GetFromJsonAsync<PagedResult<AdminSourceDto>>("/api/admin/sources?q=counted", DatabaseFixture.Json);

        Assert.Equal(1, Assert.Single(page!.Items).CitationCount);
    }
}
