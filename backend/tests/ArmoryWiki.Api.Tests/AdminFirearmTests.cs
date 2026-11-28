using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Admin.Firearms;
using ArmoryWiki.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// Slice 3a through HTTP: login, CSRF, firearm writes, locks, versions, slugs, loops, base variants, publishing.
/// Every firearm a test creates is deleted afterwards, so the public tests keep seeing only the seed.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AdminFirearmTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Firearms = "/api/admin/firearms";
    private readonly List<int> _created = [];
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AdminClientAsync();

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await using var db = fixture.NewDbContext();
        await db.SlugRedirects.Where(r => r.EntityType == SluggedEntity.Firearm && _created.Contains(r.EntityId)).ExecuteDeleteAsync();
        await db.Firearms.IgnoreQueryFilters().Where(f => _created.Contains(f.Id)).ExecuteDeleteAsync();
    }

    // Helpers ---------------------------------------------------------------------------------------------------

    private static async Task<AdminFirearmDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<AdminFirearmDto>(DatabaseFixture.Json))!;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<AdminFirearmDto> CreateAsync(object body)
    {
        var response = await _admin.PostAsJsonAsync(Firearms, body, DatabaseFixture.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await ReadAsync(response);
        _created.Add(dto.Id);
        return dto;
    }

    private Task<HttpResponseMessage> PutAsync(AdminFirearmDto f, string path, object body) =>
        _admin.PutAsJsonAsync($"{Firearms}/{f.Id}{path}?version={f.Version}", body, DatabaseFixture.Json);

    private Task<HttpResponseMessage> PostAsync(AdminFirearmDto f, string path) =>
        _admin.PostAsync($"{Firearms}/{f.Id}{path}?version={f.Version}", null);

    private static FirearmInput Core(AdminFirearmDto f) => f.Details;

    /// <summary>Fills every publish requirement with seeded references, then publishes.</summary>
    private async Task<AdminFirearmDto> PublishAsync(AdminFirearmDto f) => await ReadAsync(await PostAsync(await MakeReadyAsync(f), "/publish"));

    /// <summary>Fills in every publish requirement.</summary>
    private async Task<AdminFirearmDto> MakeReadyAsync(AdminFirearmDto f)
    {
        f = await ReadAsync(await PutAsync(f, "", Core(f) with
        {
            Country = "soviet-union",
            DesignDate = new FuzzyDateDto(1950, null, null, null, DatePrecision.Year, false),
            Mechanism = "gas-operated",
        }));
        var baseVariant = f.Variants.Single(v => v.IsBaseVariant);
        return await ReadAsync(await PutAsync(f, $"/variants/{baseVariant.Id}/chamberings", new ChamberingsInput([new("7-62x39mm", true)])));
    }

    // Auth and CSRF ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Admin_endpoints_require_login()
    {
        using var anonymous = fixture.HttpsClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Firearms)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_and_five_failures_lock_the_account()
    {
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            if (await users.FindByEmailAsync("lockout@armorywiki.test") is null)
            {
                var created = await users.CreateAsync(new IdentityUser { UserName = "lockout@armorywiki.test", Email = "lockout@armorywiki.test" }, "Lockout-Password-1");
                Assert.True(created.Succeeded);
            }
        }

        using var client = fixture.HttpsClient();
        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/admin/auth/login", new { email = "lockout@armorywiki.test", password = "nope" });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // Locked: even the right password is refused, with the same answer as any failure (no account enumeration).
        var right = await client.PostAsJsonAsync("/api/admin/auth/login", new { email = "lockout@armorywiki.test", password = "Lockout-Password-1" });
        var unknown = await client.PostAsJsonAsync("/api/admin/auth/login", new { email = "nobody@armorywiki.test", password = "x" });
        Assert.Equal(
            (await ProblemAsync(unknown, HttpStatusCode.Unauthorized)).GetProperty("title").GetString(),
            (await ProblemAsync(right, HttpStatusCode.Unauthorized)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Writes_without_the_antiforgery_header_are_rejected()
    {
        using var client = fixture.HttpsClient();
        (await client.PostAsJsonAsync("/api/admin/auth/login", new { email = DatabaseFixture.AdminEmail, password = DatabaseFixture.AdminPassword })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(Firearms, new { name = "No token" });

        Assert.Contains("antiforgery", (await ProblemAsync(response, HttpStatusCode.BadRequest)).GetProperty("title").GetString());
    }

    // Create, update, locks, versions ---------------------------------------------------------------------------

    [Fact]
    public async Task Invalid_input_is_a_validation_problem_keyed_by_field()
    {
        var response = await _admin.PostAsJsonAsync(Firearms, new
        {
            name = "",
            slug = "Bad Slug",
            designDate = new { year = 1950, month = 13, precision = "Month" },
        });

        var errors = (await ProblemAsync(response, HttpStatusCode.BadRequest)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("slug", out _));
        Assert.True(errors.TryGetProperty("designDate.month", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Firearms, new { name = "Compare", slug = "compare" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Firearms, new { name = "Newline", slug = "newline\n" })).StatusCode);
    }

    [Fact]
    public async Task A_missing_list_is_a_validation_problem_not_a_crash()
    {
        var f = await CreateAsync(new { name = "Null List Probe" });

        var response = await PutAsync(f, "/tags", new { });

        Assert.True((await ProblemAsync(response, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("tags", out _));
    }

    [Fact]
    public async Task A_wikidata_id_belongs_to_one_firearm()
    {
        var response = await _admin.PostAsJsonAsync(Firearms, new { name = "QID Probe", wikidataId = "Q37116" }); // the AK-47's

        Assert.Equal("Wikidata id taken", (await ProblemAsync(response, HttpStatusCode.Conflict)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Saving_an_unchanged_form_keeps_the_version_and_locks_nothing()
    {
        var f = await CreateAsync(new { name = "No-op Probe" });

        var same = await ReadAsync(await PutAsync(f, "", Core(f)));

        Assert.Equal(f.Version, same.Version);
        Assert.Equal(f.LockedFields, same.LockedFields);
    }

    [Fact]
    public async Task Create_generates_a_unique_slug_and_a_base_variant_and_locks_what_was_given()
    {
        var first = await CreateAsync(new { name = "Test Rifle", summary = "Made by a test." });
        var second = await CreateAsync(new { name = "Test Rifle" });

        Assert.Equal(("test-rifle", "test-rifle-2"), (first.Details.Slug, second.Details.Slug));
        Assert.Equal(PublishStatus.Draft, first.Status);
        var variant = Assert.Single(first.Variants);
        Assert.True(variant.IsBaseVariant);
        Assert.Equal("test-rifle", variant.Details.Slug);
        Assert.Equal(["country", "designDate", "mechanism", "primaryCartridge"], first.MissingForPublish);
        Assert.Contains("name", first.LockedFields);
        Assert.Contains("summary", first.LockedFields);
        Assert.DoesNotContain("designDate", first.LockedFields);
    }

    [Fact]
    public async Task Update_needs_the_current_version_locks_changed_fields_and_unlock_releases_them()
    {
        var f = await CreateAsync(new { name = "Versioned Rifle" });

        var stale = await _admin.PutAsJsonAsync($"{Firearms}/{f.Id}?version={f.Version + 1}", Core(f), DatabaseFixture.Json);
        Assert.Equal(f.Version, (await ProblemAsync(stale, HttpStatusCode.Conflict)).GetProperty("currentVersion").GetUInt32());

        var updated = await ReadAsync(await PutAsync(f, "", Core(f) with { DesignDate = FuzzyDateDto.From(FuzzyDate.InDecade(1960)) }));
        Assert.NotEqual(f.Version, updated.Version);
        Assert.Equal(1969, updated.Details.DesignDate?.YearTo);
        Assert.Contains("designDate", updated.LockedFields);

        var unlocked = await ReadAsync(await _admin.DeleteAsync($"{Firearms}/{f.Id}/locks/designDate?version={updated.Version}"));
        Assert.DoesNotContain("designDate", unlocked.LockedFields);
        Assert.Equal(1960, unlocked.Details.DesignDate?.Year); // unlocking keeps the value
    }

    [Fact]
    public async Task Renaming_answers_old_links_with_a_redirect_and_a_live_slug_beats_a_redirect()
    {
        var f = await PublishAsync(await CreateAsync(new { name = "Rename Probe" }));
        await ReadAsync(await PutAsync(f, "", Core(f) with { Slug = "rename-probe-new" }));

        using var publicClient = fixture.Client(followRedirects: false);
        var redirect = await publicClient.GetAsync("/api/firearms/rename-probe");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("/api/firearms/rename-probe-new", redirect.Headers.Location?.OriginalString);

        // Another (draft) firearm takes the old slug: the redirect goes, and the draft stays hidden.
        await CreateAsync(new { name = "Rename Probe" });
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/api/firearms/rename-probe")).StatusCode);
    }

    [Fact]
    public async Task Renaming_back_to_an_old_slug_drops_its_redirect()
    {
        var f = await PublishAsync(await CreateAsync(new { name = "Return Probe" }));
        f = await ReadAsync(await PutAsync(f, "", Core(f) with { Slug = "return-probe-away" }));
        await ReadAsync(await PutAsync(f, "", Core(f) with { Slug = "return-probe" }));

        using var publicClient = fixture.Client(followRedirects: false);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync("/api/firearms/return-probe")).StatusCode);
        var away = await publicClient.GetAsync("/api/firearms/return-probe-away");
        Assert.Equal("/api/firearms/return-probe", away.Headers.Location?.OriginalString);
    }

    // Parts -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lists_are_replaced_whole_resolved_by_slug_and_locked()
    {
        var f = await CreateAsync(new { name = "List Probe" });

        f = await ReadAsync(await PutAsync(f, "/tags", new TagsInput(["carbine", "cold-war"])));
        f = await ReadAsync(await PutAsync(f, "/designers", new DesignersInput([new("mikhail-kalashnikov", null, DesignerRole.LeadDesigner)])));
        f = await ReadAsync(await PutAsync(f, "/manufacturers", new ManufacturersInput([new("colts-manufacturing-company", null, null, true)])));
        f = await ReadAsync(await PutAsync(f, "/adoptions", new AdoptionsInput([new("united-states", null, null, "Army")])));

        Assert.Equal(["carbine", "cold-war"], f.Tags);
        Assert.Equal("mikhail-kalashnikov", Assert.Single(f.Designers).Person);
        Assert.True(Assert.Single(f.Manufacturers).IsLicensed);
        Assert.Equal("Army", Assert.Single(f.Adoptions).Note);
        Assert.Superset(new HashSet<string> { "tags", "designers", "manufacturers", "adoptions" }, f.LockedFields.ToHashSet());

        var unknown = await PutAsync(f, "/tags", new TagsInput(["laser-rifle"]));
        Assert.Equal("Unknown tag", (await ProblemAsync(unknown, HttpStatusCode.BadRequest)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Derivation_links_that_would_loop_are_rejected()
    {
        var x = await CreateAsync(new { name = "Loop X" });
        var y = await CreateAsync(new { name = "Loop Y" });
        var z = await CreateAsync(new { name = "Loop Z" });
        await ReadAsync(await PutAsync(x, "/relations", new RelationsInput([new(RelationType.DerivedFrom, y.Details.Slug!, null)])));
        y = await ReadAsync(await PutAsync(y, "/relations", new RelationsInput([new(RelationType.DerivedFrom, z.Details.Slug!, null)])));

        var loop = await PutAsync(z, "/relations", new RelationsInput([new(RelationType.CopyOf, x.Details.Slug!, null)]));
        var mutual = await PutAsync(z, "/relations", new RelationsInput([new(RelationType.InfluencedBy, x.Details.Slug!, null)]));
        var self = await PutAsync(y, "/relations", new RelationsInput([new(RelationType.InfluencedBy, y.Details.Slug!, null)]));

        Assert.Equal("Loop Z → Loop X → Loop Y → Loop Z", (await ProblemAsync(loop, HttpStatusCode.BadRequest)).GetProperty("detail").GetString()!.Split(": ")[1].TrimEnd('.'));
        Assert.Equal(HttpStatusCode.OK, mutual.StatusCode); // influence may be mutual
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    [Fact]
    public async Task The_base_variant_can_be_moved_but_not_deleted()
    {
        var f = await CreateAsync(new { name = "Base Probe" });
        var original = f.Variants.Single();
        f = await ReadAsync(await _admin.PostAsJsonAsync($"{Firearms}/{f.Id}/variants?version={f.Version}",
            new { name = "Base Probe A1", magazineCapacityStandard = 30, firingModes = new[] { "SemiAutomatic", "Burst" } }, DatabaseFixture.Json));
        var a1 = f.Variants.Single(v => !v.IsBaseVariant);
        Assert.Equal("base-probe-a1", a1.Details.Slug);
        Assert.Contains($"variants/{a1.Id}/magazineCapacityStandard", f.LockedFields);

        var deleteBase = await _admin.DeleteAsync($"{Firearms}/{f.Id}/variants/{original.Id}?version={f.Version}");
        Assert.Equal(HttpStatusCode.Conflict, deleteBase.StatusCode);

        f = await ReadAsync(await PostAsync(f, $"/variants/{a1.Id}/make-base"));
        Assert.Equal(a1.Id, f.Variants.Single(v => v.IsBaseVariant).Id);
        Assert.Contains("baseVariant", f.LockedFields);

        f = await ReadAsync(await _admin.DeleteAsync($"{Firearms}/{f.Id}/variants/{original.Id}?version={f.Version}"));
        Assert.Equal(a1.Id, Assert.Single(f.Variants).Id);
    }

    // Publishing, listing, deleting -----------------------------------------------------------------------------

    [Fact]
    public async Task Publishing_enforces_the_requirements_and_makes_the_firearm_public()
    {
        var f = await CreateAsync(new { name = "Publish Probe" });

        var refused = await ProblemAsync(await PostAsync(f, "/publish"), HttpStatusCode.BadRequest);
        Assert.Equal(4, refused.GetProperty("missingForPublish").GetArrayLength());

        f = await PublishAsync(f);
        Assert.Equal(PublishStatus.Published, f.Status);
        var page = await fixture.GetJsonAsync<Features.Firearms.FirearmDetailDto>("/api/firearms/publish-probe");
        Assert.Equal("7-62x39mm", page.Variants.Single().Chamberings.Single().Cartridge.Slug);
    }

    [Fact]
    public async Task A_draft_primary_cartridge_blocks_publishing()
    {
        await using var db = fixture.NewDbContext();
        var draftCartridge = new Cartridge { Slug = "draft-cartridge", Name = "Draft cartridge", Status = PublishStatus.Draft };
        db.Cartridges.Add(draftCartridge);
        await db.SaveChangesAsync();
        try
        {
            var f = await CreateAsync(new { name = "Draft Ammo Probe" });
            f = await ReadAsync(await PutAsync(f, $"/variants/{f.Variants.Single().Id}/chamberings", new ChamberingsInput([new("draft-cartridge", true)])));

            Assert.Contains("primaryCartridgePublished", f.MissingForPublish);
            Assert.DoesNotContain("primaryCartridge", f.MissingForPublish);
        }
        finally
        {
            await db.Firearms.IgnoreQueryFilters().Where(x => _created.Contains(x.Id)).ExecuteDeleteAsync();
            await db.Cartridges.IgnoreQueryFilters().Where(c => c.Id == draftCartridge.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Published_firearms_must_be_unpublished_before_deletion()
    {
        var f = await PublishAsync(await CreateAsync(new { name = "Delete Probe" }));

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"{Firearms}/{f.Id}?version={f.Version}")).StatusCode);

        f = await ReadAsync(await PostAsync(f, "/unpublish"));
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"{Firearms}/{f.Id}?version={f.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Firearms}/{f.Id}")).StatusCode);
    }

    [Fact]
    public async Task Admin_list_includes_drafts_with_what_blocks_publishing()
    {
        var created = await CreateAsync(new { name = "Queue Probe", wikidataId = "Q99999991" });

        var page = await _admin.GetFromJsonAsync<PagedResult<AdminFirearmListItemDto>>($"{Firearms}?status=Draft&q=queue", DatabaseFixture.Json);

        var item = Assert.Single(page!.Items);
        Assert.Equal("queue-probe", item.Slug);
        Assert.Equal("Q99999991", item.WikidataId);
        Assert.Contains("mechanism", item.MissingForPublish);

        // The row's version publishes without loading the firearm first.
        Assert.Equal(created.Version, item.Version);
        var publish = await _admin.PostAsync($"{Firearms}/{item.Id}/publish?version={item.Version}", null);
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode); // a version mismatch would answer 409
    }

    [Fact]
    public async Task Ready_filter_lists_exactly_the_firearms_with_nothing_missing()
    {
        await MakeReadyAsync(await CreateAsync(new { name = "Ready Probe Done" }));
        await CreateAsync(new { name = "Ready Probe Not" });

        var ready = await _admin.GetFromJsonAsync<PagedResult<AdminFirearmListItemDto>>($"{Firearms}?status=Draft&ready=true&q=ready-probe", DatabaseFixture.Json);
        Assert.Equal("ready-probe-done", Assert.Single(ready!.Items).Slug);

        // The query and MissingForPublish are two copies of one rule: they must agree on every firearm.
        var all = await _admin.GetFromJsonAsync<PagedResult<AdminFirearmListItemDto>>($"{Firearms}?pageSize=100", DatabaseFixture.Json);
        var allReady = await _admin.GetFromJsonAsync<PagedResult<AdminFirearmListItemDto>>($"{Firearms}?ready=true&pageSize=100", DatabaseFixture.Json);
        Assert.True(all!.TotalCount <= 100, "the check reads one page");
        Assert.Equal(
            all.Items.Where(i => i.MissingForPublish.Count == 0).Select(i => i.Id).Order(),
            allReady!.Items.Select(i => i.Id).Order());
    }

    [Fact]
    public async Task Variant_and_list_locks_unlock_by_their_slash_keys()
    {
        var f = await CreateAsync(new { name = "Slash Lock Probe" });
        var baseVariant = f.Variants.Single(v => v.IsBaseVariant);
        f = await ReadAsync(await PutAsync(f, $"/variants/{baseVariant.Id}", new { name = "Slash Lock Probe", weightEmptyG = 3100 }));
        var key = $"variants/{baseVariant.Id}/weightEmptyG";
        Assert.Contains(key, f.LockedFields);

        // The admin UI sends the key with its slashes as path separators (a catch-all route segment).
        f = await ReadAsync(await _admin.DeleteAsync($"{Firearms}/{f.Id}/locks/{key}?version={f.Version}"));

        Assert.DoesNotContain(key, f.LockedFields);
    }

    // Merge into variant (ADR-0007) -----------------------------------------------------------------------------

    [Fact]
    public async Task Merge_turns_a_draft_into_a_variant_and_carries_over_what_hangs_off_it()
    {
        var target = await PublishAsync(await CreateAsync(new { name = "Merge Target" }));
        target = await ReadAsync(await PutAsync(target, "/designers", new DesignersInput([new("eugene-stoner", null, DesignerRole.LeadDesigner)])));

        var source = await CreateAsync(new { name = "Merge Source M", wikidataId = "Q99999992" });
        source = await ReadAsync(await PutAsync(source, "/designers", new DesignersInput(
            [new("eugene-stoner", null, DesignerRole.LeadDesigner), new("john-garand", null, DesignerRole.CoDesigner)])));
        source = await ReadAsync(await PutAsync(source, "/tags", new TagsInput(["assault-rifle"])));
        var sourceBase = source.Variants.Single(v => v.IsBaseVariant);
        source = await ReadAsync(await PutAsync(source, $"/variants/{sourceBase.Id}/chamberings", new ChamberingsInput([new("5-45x39mm", true)])));
        source = await ReadAsync(await PutAsync(source, "/relations", new RelationsInput([new(RelationType.InfluencedBy, "ak-47", null)])));
        var wikidataSource = (await _admin.GetFromJsonAsync<PagedResult<JsonElement>>("/api/admin/sources?q=Wikidata", DatabaseFixture.Json))!.Items[0].GetProperty("id").GetInt32();
        Assert.True((await _admin.PostAsJsonAsync($"{Firearms}/{source.Id}/citations", new { sourceId = wikidataSource }, DatabaseFixture.Json)).IsSuccessStatusCode);

        var child = await CreateAsync(new { name = "Merge Child" });
        await ReadAsync(await PutAsync(child, "/relations", new RelationsInput([new(RelationType.DerivedFrom, "merge-source-m", null)])));

        // The preview says what will happen ...
        var preview = (await _admin.GetFromJsonAsync<MergePreviewDto>($"{Firearms}/{source.Id}/merge-into/{target.Id}", DatabaseFixture.Json))!;
        Assert.Empty(preview.Blockers);
        Assert.Contains("Designer John Garand.", preview.Moves);
        Assert.Contains(preview.Drops, d => d.StartsWith("Designer Eugene Stoner: already credited", StringComparison.Ordinal));
        Assert.Contains(preview.Drops, d => d.StartsWith("Its own relation to AK-47", StringComparison.Ordinal));
        Assert.Contains("Merge Child's relation now points at Merge Target.", preview.Moves);

        // ... and the merge does it.
        var merged = await ReadAsync(await _admin.PostAsync($"{Firearms}/{source.Id}/merge-into/{target.Id}?version={source.Version}&targetVersion={target.Version}", null));

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Firearms}/{source.Id}")).StatusCode);
        var variant = merged.Variants.Single(v => v.Details.Slug == "merge-source-m");
        Assert.Equal(("Merge Source M", "Q99999992", false), (variant.Details.Name, variant.WikidataId, variant.IsBaseVariant));
        Assert.Equal([new ChamberingInput("5-45x39mm", true)], variant.Chamberings);
        Assert.Equal(["eugene-stoner", "john-garand"], merged.Designers.Select(d => d.Person).Order());
        Assert.Contains("assault-rifle", merged.Tags);
        Assert.Equal(PublishStatus.Published, merged.Status);

        var childAfter = await ReadAsync(await _admin.GetAsync($"{Firearms}/{child.Id}"));
        Assert.Equal([new RelationInput(RelationType.DerivedFrom, "merge-target", null)], childAfter.Relations);
        var citations = await _admin.GetFromJsonAsync<List<JsonElement>>($"{Firearms}/{target.Id}/citations", DatabaseFixture.Json);
        Assert.Single(citations!);

        // The old address leads to the target, and the QID stays taken (imports and admins can't reuse it).
        using var publicClient = fixture.Client(followRedirects: false);
        var redirect = await publicClient.GetAsync("/api/firearms/merge-source-m");
        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("/api/firearms/merge-target", redirect.Headers.Location?.OriginalString);
        var reuse = await _admin.PostAsJsonAsync(Firearms, new { name = "QID Reuse", wikidataId = "Q99999992" }, DatabaseFixture.Json);
        Assert.Equal(HttpStatusCode.Conflict, reuse.StatusCode);
    }

    [Fact]
    public async Task Only_drafts_merge_and_only_at_the_versions_previewed()
    {
        var target = await CreateAsync(new { name = "Merge Guard Target" });
        var published = await PublishAsync(await CreateAsync(new { name = "Merge Guard Published" }));

        var preview = (await _admin.GetFromJsonAsync<MergePreviewDto>($"{Firearms}/{published.Id}/merge-into/{target.Id}", DatabaseFixture.Json))!;
        Assert.Contains(preview.Blockers, b => b.Contains("unpublish it first", StringComparison.Ordinal));
        var refused = await _admin.PostAsync($"{Firearms}/{published.Id}/merge-into/{target.Id}?version={published.Version}&targetVersion={target.Version}", null);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        var self = (await _admin.GetFromJsonAsync<MergePreviewDto>($"{Firearms}/{target.Id}/merge-into/{target.Id}", DatabaseFixture.Json))!;
        Assert.NotEmpty(self.Blockers);

        var draft = await CreateAsync(new { name = "Merge Guard Draft" });
        var stale = await _admin.PostAsync($"{Firearms}/{draft.Id}/merge-into/{target.Id}?version={draft.Version}&targetVersion={target.Version + 1}", null);
        await ProblemAsync(stale, HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync($"{Firearms}/{draft.Id}")).StatusCode); // untouched
    }
}
