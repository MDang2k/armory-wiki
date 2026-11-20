using System.Net;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Tests;

[Collection(nameof(DatabaseCollection))]
public sealed class FirearmDetailTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Detail_includes_variants_credits_and_relations_in_both_directions()
    {
        var ak47 = await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47");

        Assert.Equal("Soviet Union", ak47.Country?.Name);
        Assert.Equal(FeedSystem.DetachableBoxMagazine, ak47.FeedSystem);
        var baseVariant = Assert.Single(ak47.Variants);
        Assert.True(baseVariant.IsBaseVariant);
        Assert.Equal([FiringModes.SemiAutomatic, FiringModes.Automatic], baseVariant.Specs.FiringModes);
        Assert.Equal(30, baseVariant.Specs.MagazineCapacityStandard);
        Assert.Equal("7-62x39mm", Assert.Single(baseVariant.Chamberings, c => c.IsPrimary).Cartridge.Slug);
        var designer = Assert.Single(ak47.Designers);
        Assert.Equal((DesignerRole.LeadDesigner, "mikhail-kalashnikov"), (designer.Role, designer.Person?.Slug));
        Assert.Contains(ak47.RelationsFrom, r => r is { Type: RelationType.InfluencedBy, Firearm.Slug: "stg-44" });
        Assert.Contains(ak47.RelationsFrom, r => r is { Type: RelationType.Replaced, Firearm.Slug: "sks" });
        Assert.Contains(ak47.RelationsTo, r => r is { Type: RelationType.DerivedFrom, Firearm.Slug: "akm" });
        Assert.DoesNotContain(ak47.RelationsTo, r => r.Firearm.Slug == DatabaseFixture.DraftSlug);
    }

    [Fact]
    public async Task Manually_operated_firearms_report_no_firing_modes()
    {
        var kar98k = await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/karabiner-98k");

        Assert.Empty(kar98k.Variants.Single().Specs.FiringModes);
    }

    [Fact]
    public async Task Detail_is_not_publicly_cacheable()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/firearms/ak-47");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_renamed_slug_answers_with_a_permanent_redirect()
    {
        using var client = fixture.Client(followRedirects: false);
        var response = await client.GetAsync("/api/firearms/kalashnikov-rifle");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/api/firearms/ak-47", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/api/firearms/does-not-exist")]
    [InlineData("/api/firearms/" + DatabaseFixture.DraftSlug)]
    [InlineData("/api/firearms/old-draft-carbine")] // a redirect to a draft must not reveal it
    [InlineData("/api/firearms/does-not-exist/family-tree")]
    public async Task Unknown_and_draft_slugs_are_404_problem_details(string path)
    {
        using var client = fixture.Client(followRedirects: false);
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Family_tree_walks_derived_from_up_and_down()
    {
        var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/m16/family-tree?depth=3");

        Assert.Equal(["ar-10", "ar-15", "m16", "m4-carbine"], tree.Nodes.Select(n => n.Slug).Order());
        Assert.Equal("m16", tree.Nodes.Single(n => n.IsRoot).Slug);
        Assert.Equal(
            [new FamilyTreeEdgeDto("ar-10", "ar-15"), new FamilyTreeEdgeDto("ar-15", "m16"), new FamilyTreeEdgeDto("m16", "m4-carbine")],
            tree.Edges);
    }

    [Fact]
    public async Task Family_tree_depth_limits_the_walk()
    {
        var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/m16/family-tree?depth=1");

        Assert.Equal(["ar-15", "m16", "m4-carbine"], tree.Nodes.Select(n => n.Slug).Order());
    }

    [Fact]
    public async Task Family_tree_survives_cycles_in_relations()
    {
        // Tests in this collection run one at a time, so these published rows are invisible to the others.
        await using var db = fixture.NewDbContext();
        var a = new Firearm { Slug = "cycle-a", Name = "Cycle A", Status = PublishStatus.Published };
        var b = new Firearm { Slug = "cycle-b", Name = "Cycle B", Status = PublishStatus.Published };
        db.Firearms.AddRange(a, b);
        db.FirearmRelations.AddRange(
            new FirearmRelation { FromFirearm = a, ToFirearm = b, Type = RelationType.DerivedFrom },
            new FirearmRelation { FromFirearm = b, ToFirearm = a, Type = RelationType.DerivedFrom });
        await db.SaveChangesAsync();
        try
        {
            var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/cycle-a/family-tree?depth=5");

            Assert.Equal(["cycle-a", "cycle-b"], tree.Nodes.Select(n => n.Slug).Order());
            Assert.Equal(2, tree.Edges.Count);
        }
        finally
        {
            db.Firearms.RemoveRange(a, b);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Family_tree_never_shows_drafts()
    {
        var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/ak-47/family-tree");

        Assert.Equal(["ak-47", "ak-74", "akm"], tree.Nodes.Select(n => n.Slug).Order());
    }

    [Fact]
    public async Task Compare_returns_base_variant_specs_in_the_requested_order()
    {
        var items = await fixture.GetJsonAsync<List<CompareItemDto>>("/api/firearms/compare?slugs=m16,ak-47");

        Assert.Equal(["m16", "ak-47"], items.Select(i => i.Slug));
        Assert.Equal("5-56x45mm-nato", items[0].PrimaryCartridge?.Slug);
        Assert.Equal(30, items[1].Specs.MagazineCapacityStandard);
    }

    [Theory]
    [InlineData("", HttpStatusCode.BadRequest)]
    [InlineData("ak-47,akm,ak-74,sks,m16", HttpStatusCode.BadRequest)]
    [InlineData("ak-47," + DatabaseFixture.DraftSlug, HttpStatusCode.NotFound)]
    public async Task Compare_rejects_empty_oversized_and_unknown_requests(string slugs, HttpStatusCode expected)
    {
        using var client = fixture.Client();
        var response = await client.GetAsync($"/api/firearms/compare?slugs={slugs}");

        Assert.Equal(expected, response.StatusCode);
    }
}
