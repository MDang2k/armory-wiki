using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Admin.Resources;
using ArmoryWiki.Api.Features.Mechanisms;
using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// Slice 3b through HTTP: people, organizations, cartridges, countries, tags and mechanisms on the shared admin
/// lifecycle. Everything a test creates has a slug starting with "test-" and is removed afterwards.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AdminResourceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AdminClientAsync();

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await using var db = fixture.NewDbContext();
        await db.SlugRedirects.Where(r => r.OldSlug.StartsWith("test-")).ExecuteDeleteAsync();
        await db.People.IgnoreQueryFilters().Where(x => x.Slug.StartsWith("test-")).ExecuteDeleteAsync();
        await db.Organizations.IgnoreQueryFilters().Where(x => x.Slug.StartsWith("test-")).ExecuteDeleteAsync();
        await db.Cartridges.IgnoreQueryFilters().Where(x => x.Slug.StartsWith("test-")).ExecuteUpdateAsync(s => s.SetProperty(x => x.ParentCartridgeId, (int?)null));
        await db.Cartridges.IgnoreQueryFilters().Where(x => x.Slug.StartsWith("test-")).ExecuteDeleteAsync();
        await db.Countries.Where(x => x.Slug.StartsWith("test-")).ExecuteDeleteAsync();
        await db.Tags.Where(x => x.Slug.StartsWith("test-")).ExecuteDeleteAsync();
        foreach (var m in (await db.OperatingMechanisms.Where(x => x.Path.Contains("/test-")).ToListAsync()).OrderByDescending(x => x.Path.Length))
        {
            db.OperatingMechanisms.Remove(m);
            await db.SaveChangesAsync(); // deepest first: children before parents
        }
    }

    // Helpers ---------------------------------------------------------------------------------------------------

    private async Task<AdminEntityDto<T>> CreateAsync<T>(string route, object body)
    {
        var response = await _admin.PostAsJsonAsync($"/api/admin/{route}", body, DatabaseFixture.Json);
        return await ReadAsync<T>(response, HttpStatusCode.Created);
    }

    private static async Task<AdminEntityDto<T>> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<AdminEntityDto<T>>(body, DatabaseFixture.Json)!;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private Task<HttpResponseMessage> PutAsync<T>(string route, AdminEntityDto<T> e, object body) =>
        _admin.PutAsJsonAsync($"/api/admin/{route}/{e.Id}?version={e.Version}", body, DatabaseFixture.Json);

    private Task<HttpResponseMessage> PostAsync<T>(string route, AdminEntityDto<T> e, string action) =>
        _admin.PostAsync($"/api/admin/{route}/{e.Id}/{action}?version={e.Version}", null);

    private Task<HttpResponseMessage> DeleteAsync<T>(string route, AdminEntityDto<T> e) =>
        _admin.DeleteAsync($"/api/admin/{route}/{e.Id}?version={e.Version}");

    private async Task<HttpResponseMessage> DeleteSeededAsync(string route, string slug)
    {
        var page = await _admin.GetFromJsonAsync<PagedResult<AdminEntityListItemDto>>($"/api/admin/{route}?q={slug}&pageSize=100", DatabaseFixture.Json);
        var item = page!.Items.Single(i => i.Slug == slug);
        var detail = await _admin.GetFromJsonAsync<JsonElement>($"/api/admin/{route}/{item.Id}");
        return await _admin.DeleteAsync($"/api/admin/{route}/{item.Id}?version={detail.GetProperty("version").GetUInt32()}");
    }

    // People ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_person_goes_from_draft_to_public_page_with_locks_versions_and_redirects()
    {
        var p = await CreateAsync<PersonInput>("people", new { fullName = "Test Person", nationality = "united-states" });
        Assert.Equal(("test-person", PublishStatus.Draft), (p.Details.Slug, p.Status));
        Assert.Contains("fullName", p.LockedFields);
        Assert.Empty(p.MissingForPublish); // a name is enough

        p = await ReadAsync<PersonInput>(await PostAsync("people", p, "publish"));
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client().GetAsync("/api/people/test-person")).StatusCode);

        var stale = await _admin.PutAsJsonAsync($"/api/admin/people/{p.Id}?version={p.Version + 1}", p.Details, DatabaseFixture.Json);
        await ProblemAsync(stale, HttpStatusCode.Conflict);

        p = await ReadAsync<PersonInput>(await PutAsync("people", p, p.Details with { Slug = "test-person-renamed" }));
        using var publicClient = fixture.Client(followRedirects: false);
        Assert.Equal("/api/people/test-person-renamed", (await publicClient.GetAsync("/api/people/test-person")).Headers.Location?.OriginalString);

        p = await ReadAsync<PersonInput>(await _admin.DeleteAsync($"/api/admin/people/{p.Id}/locks/fullName?version={p.Version}"));
        Assert.DoesNotContain("fullName", p.LockedFields);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync("people", p)).StatusCode);
    }

    [Fact]
    public async Task People_referenced_by_firearms_cannot_be_deleted()
    {
        var refused = await ProblemAsync(await DeleteSeededAsync("people", "mikhail-kalashnikov"), HttpStatusCode.Conflict);

        Assert.Contains("firearm ak-47", refused.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Admin_lists_filter_by_status()
    {
        var created = await CreateAsync<PersonInput>("people", new { fullName = "Test Draft Person" });

        var drafts = await _admin.GetFromJsonAsync<PagedResult<AdminEntityListItemDto>>("/api/admin/people?status=Draft", DatabaseFixture.Json);

        var row = Assert.Single(drafts!.Items, i => i.Slug == "test-draft-person");
        Assert.All(drafts.Items, i => Assert.Equal(PublishStatus.Draft, i.Status));
        Assert.Equal(created.Version, row.Version); // so the row can publish without loading the person
    }

    // Organizations and cartridges ------------------------------------------------------------------------------

    [Fact]
    public async Task Unknown_references_are_rejected()
    {
        var response = await _admin.PostAsJsonAsync("/api/admin/organizations", new { name = "Test Arsenal", type = "StateArsenal", country = "atlantis" }, DatabaseFixture.Json);

        Assert.Equal("Unknown country", (await ProblemAsync(response, HttpStatusCode.BadRequest)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_cartridge_needs_a_country_before_publishing()
    {
        var k = await CreateAsync<CartridgeInput>("cartridges", new { name = "Test 9x99mm", type = "Pistol" });
        Assert.Equal(["country"], k.MissingForPublish);
        await ProblemAsync(await PostAsync("cartridges", k, "publish"), HttpStatusCode.BadRequest);

        k = await ReadAsync<CartridgeInput>(await PutAsync("cartridges", k, k.Details with { Country = "belgium" }));
        k = await ReadAsync<CartridgeInput>(await PostAsync("cartridges", k, "publish"));

        Assert.Equal(PublishStatus.Published, k.Status);
    }

    [Fact]
    public async Task Cartridge_parents_never_loop_and_parents_cannot_be_deleted()
    {
        var a = await CreateAsync<CartridgeInput>("cartridges", new { name = "Test Parent Round", type = "Intermediate" });
        var b = await CreateAsync<CartridgeInput>("cartridges", new { name = "Test Child Round", type = "Intermediate", parent = a.Details.Slug });

        var loop = await PutAsync("cartridges", a, a.Details with { Parent = b.Details.Slug });
        Assert.Equal("Parent loop", (await ProblemAsync(loop, HttpStatusCode.BadRequest)).GetProperty("title").GetString());

        var refused = await ProblemAsync(await DeleteAsync("cartridges", a), HttpStatusCode.Conflict);
        Assert.Contains("cartridge test-child-round", refused.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_cartridge_type_is_required_to_publish_and_locked_when_given()
    {
        var untyped = await CreateAsync<CartridgeInput>("cartridges", new { name = "Test Untyped Round", country = "belgium" });
        var typed = await CreateAsync<CartridgeInput>("cartridges", new { name = "Test Typed Round", type = "Pistol" });

        Assert.Equal(["type"], untyped.MissingForPublish);
        Assert.Contains("type", typed.LockedFields); // even the first enum value counts as supplied
    }

    [Fact]
    public async Task An_organization_can_be_published_unpublished_and_deleted()
    {
        var o = await CreateAsync<OrganizationInput>("organizations", new { name = "Test Arsenal Works", type = "StateArsenal", country = "united-states" });
        Assert.Contains("type", o.LockedFields);

        o = await ReadAsync<OrganizationInput>(await PostAsync("organizations", o, "publish"));
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client().GetAsync("/api/organizations/test-arsenal-works")).StatusCode);

        o = await ReadAsync<OrganizationInput>(await PostAsync("organizations", o, "unpublish"));
        Assert.Equal(PublishStatus.Draft, o.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client().GetAsync("/api/organizations/test-arsenal-works")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync("organizations", o)).StatusCode);
    }

    // Countries and tags ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Countries_in_use_cannot_be_deleted_and_unused_ones_can()
    {
        var refused = await ProblemAsync(await DeleteSeededAsync("countries", "soviet-union"), HttpStatusCode.Conflict);
        Assert.Contains("firearm ak-47", refused.GetProperty("detail").GetString());

        var badIso = await _admin.PostAsJsonAsync("/api/admin/countries", new { name = "Test Land", isoCode = "usa" });
        Assert.Equal(HttpStatusCode.BadRequest, badIso.StatusCode);

        var land = await CreateAsync<CountryInput>("countries", new { name = "Test Land", isoCode = "TL", isHistorical = true });
        Assert.Null(land.Status); // countries have no draft/published lifecycle
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync("countries", land)).StatusCode);
    }

    [Fact]
    public async Task Tags_in_use_cannot_be_deleted()
    {
        var tag = await CreateAsync<TagInput>("tags", new { name = "Test Era", group = "Era" });
        Assert.Equal("test-era", tag.Details.Slug);

        var refused = await ProblemAsync(await DeleteSeededAsync("tags", "carbine"), HttpStatusCode.Conflict);
        Assert.Contains("Wikidata class Q183302", refused.GetProperty("detail").GetString());
        Assert.Contains("firearm sks", refused.GetProperty("detail").GetString());
    }

    // Mechanisms ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Moving_a_mechanism_moves_its_whole_subtree_and_never_beneath_itself()
    {
        var root = await CreateAsync<MechanismInput>("mechanisms", new { name = "Test Root" });
        var child = await CreateAsync<MechanismInput>("mechanisms", new { name = "Test Child", parent = "test-root" });

        root = await ReadAsync<MechanismInput>(await PutAsync("mechanisms", root, root.Details with { Parent = "gas-operated" }));

        var moved = await fixture.GetJsonAsync<MechanismDetailDto>("/api/mechanisms/test-child");
        Assert.Equal(["gas-operated", "test-root"], moved.Ancestors.Select(a => a.Slug));

        var gas = (await _admin.GetFromJsonAsync<PagedResult<AdminEntityListItemDto>>("/api/admin/mechanisms?q=gas-operated", DatabaseFixture.Json))!
            .Items.Single(i => i.Slug == "gas-operated");
        var gasDetail = await _admin.GetFromJsonAsync<AdminEntityDto<MechanismInput>>($"/api/admin/mechanisms/{gas.Id}", DatabaseFixture.Json);
        var loop = await PutAsync("mechanisms", gasDetail!, gasDetail!.Details with { Parent = "test-child" });
        Assert.Equal("Mechanism loop", (await ProblemAsync(loop, HttpStatusCode.BadRequest)).GetProperty("title").GetString());

        var inUse = await ProblemAsync(await DeleteAsync("mechanisms", root), HttpStatusCode.Conflict);
        Assert.Equal("Mechanism is in use", inUse.GetProperty("title").GetString()); // has a child

        // Moving the root rewrote the child's path, so the child has a new version.
        await ProblemAsync(await DeleteAsync("mechanisms", child), HttpStatusCode.Conflict);
        child = (await _admin.GetFromJsonAsync<AdminEntityDto<MechanismInput>>($"/api/admin/mechanisms/{child.Id}", DatabaseFixture.Json))!;
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync("mechanisms", child)).StatusCode);
    }

    [Fact]
    public async Task Renaming_a_mechanism_slug_carries_its_subtree_along()
    {
        var root = await CreateAsync<MechanismInput>("mechanisms", new { name = "Test Branch" });
        await CreateAsync<MechanismInput>("mechanisms", new { name = "Test Leaf", parent = "test-branch" });

        await ReadAsync<MechanismInput>(await PutAsync("mechanisms", root, root.Details with { Slug = "test-branch-renamed" }));

        var leaf = await fixture.GetJsonAsync<MechanismDetailDto>("/api/mechanisms/test-leaf");
        Assert.Equal(["test-branch-renamed"], leaf.Ancestors.Select(a => a.Slug));
    }
}
