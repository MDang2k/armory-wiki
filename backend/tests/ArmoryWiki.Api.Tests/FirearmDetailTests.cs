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

        Assert.True(response.Headers.CacheControl is { NoStore: true }, $"Cache-Control: {response.Headers.CacheControl}");
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
            [new FamilyTreeEdgeDto("ar-10", "ar-15", false), new FamilyTreeEdgeDto("ar-15", "m16", false), new FamilyTreeEdgeDto("m16", "m4-carbine", false)],
            tree.Edges);
        Assert.False(tree.Truncated);
    }

    [Fact]
    public async Task Family_tree_includes_copies_and_stops_at_150_firearms_nearest_first()
    {
        // A root with one copy and 200 derivatives: the copy is kept and marked, and the family is cut at 150.
        // Tests in this collection run one at a time, so these published rows are invisible to the others.
        await using var db = fixture.NewDbContext();
        var root = new Firearm { Slug = "big-family", Name = "Big Family", Status = PublishStatus.Published };
        var copy = new Firearm { Slug = "big-family-copy", Name = "Big Family Copy", Status = PublishStatus.Published };
        var children = Enumerable.Range(1, 200)
            .Select(i => new Firearm { Slug = $"big-family-{i}", Name = $"Big Family {i}", Status = PublishStatus.Published }).ToList();
        db.Firearms.AddRange([root, copy, .. children]);
        db.FirearmRelations.Add(new FirearmRelation { FromFirearm = copy, ToFirearm = root, Type = RelationType.CopyOf });
        db.FirearmRelations.AddRange(children.Select(c => new FirearmRelation { FromFirearm = c, ToFirearm = root, Type = RelationType.DerivedFrom }));
        // A grandchild of the first derivative: two steps away, so it never beats a direct derivative to the cut.
        var grandchild = new Firearm { Slug = "big-family-grandchild", Name = "Big Family Grandchild", Status = PublishStatus.Published };
        db.Firearms.Add(grandchild);
        db.FirearmRelations.Add(new FirearmRelation { FromFirearm = grandchild, ToFirearm = children[0], Type = RelationType.DerivedFrom });
        await db.SaveChangesAsync();
        try
        {
            var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/big-family/family-tree?depth=2");

            Assert.True(tree.Truncated);
            Assert.Equal(150, tree.Nodes.Count);
            Assert.DoesNotContain(tree.Nodes, n => n.Slug == "big-family-grandchild");
            Assert.All(tree.Edges, e => Assert.Contains(tree.Nodes, n => n.Slug == e.Target));
            if (tree.Edges.SingleOrDefault(e => e.Target == "big-family-copy") is { } copyEdge)
            {
                Assert.True(copyEdge.IsCopy);
            }

            Assert.All(tree.Edges.Where(e => e.Target != "big-family-copy"), e => Assert.False(e.IsCopy));
        }
        finally
        {
            db.FirearmRelations.RemoveRange(db.FirearmRelations.Local.ToList());
            db.Firearms.RemoveRange([root, copy, grandchild, .. children]);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Family_tree_marks_copies()
    {
        await using var db = fixture.NewDbContext();
        var original = new Firearm { Slug = "copy-original", Name = "Copy Original", Status = PublishStatus.Published };
        var licensed = new Firearm { Slug = "copy-licensed", Name = "Copy Licensed", Status = PublishStatus.Published };
        db.Firearms.AddRange(original, licensed);
        db.FirearmRelations.Add(new FirearmRelation { FromFirearm = licensed, ToFirearm = original, Type = RelationType.CopyOf });
        await db.SaveChangesAsync();
        try
        {
            var tree = await fixture.GetJsonAsync<FamilyTreeDto>("/api/firearms/copy-licensed/family-tree");

            Assert.Equal([new FamilyTreeEdgeDto("copy-original", "copy-licensed", true)], tree.Edges);
        }
        finally
        {
            db.Firearms.RemoveRange(original, licensed);
            await db.SaveChangesAsync();
        }
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
