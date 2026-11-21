using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArmoryWiki.Api.Features.Admin.Firearms;
using ArmoryWiki.Api.Features.Admin.Media;
using ArmoryWiki.Api.Features.Admin.Sources;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// PLAN.md Phase 3 "Done when": an admin can, through the API, create a firearm with variants, designers,
/// chamberings, relations, an uploaded image and a citation, publish it, and see it on the public API.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class Phase3AcceptanceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AdminClientAsync();

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await using var db = fixture.NewDbContext();
        await db.Firearms.IgnoreQueryFilters().Where(f => f.Slug == "acceptance-rifle").ExecuteDeleteAsync();
        await db.Sources.Where(s => s.Title == "Acceptance Source").ExecuteDeleteAsync();
        await db.Images.ExecuteDeleteAsync();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, DatabaseFixture.Json)!;
    }

    [Fact]
    public async Task An_admin_builds_and_publishes_a_complete_firearm_page_through_the_api()
    {
        const string Firearms = "/api/admin/firearms";
        var f = await ReadAsync<AdminFirearmDto>(await _admin.PostAsJsonAsync(Firearms, new
        {
            name = "Acceptance Rifle",
            country = "soviet-union",
            designDate = new { year = 1950, precision = "Year" },
            mechanism = "long-stroke-piston",
            summary = "A rifle that exists only in the acceptance test.",
        }, DatabaseFixture.Json), HttpStatusCode.Created);
        string Q() => $"?version={f.Version}";

        // A second variant, then chamberings for the base variant.
        f = await ReadAsync<AdminFirearmDto>(await _admin.PostAsJsonAsync($"{Firearms}/{f.Id}/variants{Q()}",
            new { name = "Acceptance Rifle M", magazineCapacityStandard = 30, firingModes = new[] { "SemiAutomatic", "Automatic" } }, DatabaseFixture.Json));
        var baseVariant = f.Variants.Single(v => v.IsBaseVariant);
        f = await ReadAsync<AdminFirearmDto>(await _admin.PutAsJsonAsync($"{Firearms}/{f.Id}/variants/{baseVariant.Id}/chamberings{Q()}",
            new ChamberingsInput([new("7-62x39mm", true)]), DatabaseFixture.Json));

        // Designer and relation.
        f = await ReadAsync<AdminFirearmDto>(await _admin.PutAsJsonAsync($"{Firearms}/{f.Id}/designers{Q()}",
            new DesignersInput([new("mikhail-kalashnikov", null, DesignerRole.LeadDesigner)]), DatabaseFixture.Json));
        f = await ReadAsync<AdminFirearmDto>(await _admin.PutAsJsonAsync($"{Firearms}/{f.Id}/relations{Q()}",
            new RelationsInput([new(RelationType.DerivedFrom, "ak-47", null)]), DatabaseFixture.Json));

        // An uploaded image and a citation (neither takes the version).
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.Jpeg(64, 48));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "rifle.jpg");
        form.Add(new StringContent("Acceptance photographer"), "author");
        form.Add(new StringContent("CC-BY-4.0"), "license");
        form.Add(new StringContent("https://example.org/rifle"), "sourceUrl");
        await ReadAsync<AdminImageDto>(await _admin.PostAsync($"{Firearms}/{f.Id}/images", form), HttpStatusCode.Created);
        var source = await ReadAsync<AdminSourceDto>(await _admin.PostAsJsonAsync("/api/admin/sources",
            new { type = "Book", title = "Acceptance Source" }), HttpStatusCode.Created);
        await ReadAsync<AdminCitationDto>(await _admin.PostAsJsonAsync($"{Firearms}/{f.Id}/citations",
            new { sourceId = source.Id, fieldName = "designDate", locator = "p. 1" }), HttpStatusCode.Created);

        Assert.Empty(f.MissingForPublish);
        f = await ReadAsync<AdminFirearmDto>(await _admin.PostAsync($"{Firearms}/{f.Id}/publish{Q()}", null));
        Assert.Equal(PublishStatus.Published, f.Status);

        var page = await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/acceptance-rifle");
        Assert.Equal(2, page.Variants.Count);
        Assert.Equal("7-62x39mm", page.Variants.Single(v => v.IsBaseVariant).Chamberings.Single().Cartridge.Slug);
        Assert.Equal("mikhail-kalashnikov", page.Designers.Single().Person?.Slug);
        Assert.Contains(page.RelationsFrom, r => r is { Type: RelationType.DerivedFrom, Firearm.Slug: "ak-47" });
        Assert.Equal("Acceptance photographer", page.Images.Single().Author);
        Assert.Equal("Acceptance Source", page.Citations.Single().Source.Title);
        Assert.Contains(page.Variants, v => v.Specs.MagazineCapacityStandard == 30);

        // ...and it now shows up on the AK-47's family tree.
        var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/ak-47/family-tree");
        Assert.Contains(tree.Nodes, n => n.Slug == "acceptance-rifle");
    }
}
