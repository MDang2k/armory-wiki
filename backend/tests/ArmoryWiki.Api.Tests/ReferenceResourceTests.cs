using System.Net;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Cartridges;
using ArmoryWiki.Api.Features.Mechanisms;
using ArmoryWiki.Api.Features.Organizations;
using ArmoryWiki.Api.Features.People;
using ArmoryWiki.Api.Features.Reference;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Tests;

/// <summary>People, organizations, cartridges, mechanisms, countries, tags and the timeline.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class ReferenceResourceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task People_list_is_paged_by_name()
    {
        var people = await fixture.GetJsonAsync<PagedResult<PersonListItemDto>>("/api/people");

        Assert.Equal(6, people.TotalCount);
        Assert.Equal("Eugene Stoner", people.Items[0].FullName);
    }

    [Fact]
    public async Task Person_lists_published_firearms_by_design_year()
    {
        var kalashnikov = await fixture.GetJsonAsync<PersonDetailDto>("/api/people/mikhail-kalashnikov");

        Assert.Equal(["ak-47", "akm", "ak-74"], kalashnikov.Firearms.Select(f => f.Slug));
        Assert.Equal(1919, kalashnikov.BirthDate?.Year);
    }

    [Fact]
    public async Task Organizations_list_and_detail()
    {
        var orgs = await fixture.GetJsonAsync<PagedResult<OrganizationListItemDto>>("/api/organizations");
        var colt = await fixture.GetJsonAsync<OrganizationDetailDto>("/api/organizations/colts-manufacturing-company");

        Assert.Equal(7, orgs.TotalCount);
        Assert.Equal(["m4-carbine"], colt.Designed.Select(f => f.Slug));
        Assert.True(colt.Manufactured.Single(f => f.Slug == "ar-15").IsLicensed);
        Assert.DoesNotContain(colt.Manufactured, f => f.Slug == DatabaseFixture.DraftSlug);
    }

    [Fact]
    public async Task Cartridges_list_and_detail_with_lineage_and_firearms()
    {
        var cartridges = await fixture.GetJsonAsync<PagedResult<CartridgeListItemDto>>("/api/cartridges");
        var m43 = await fixture.GetJsonAsync<CartridgeDetailDto>("/api/cartridges/7-62x39mm");
        var rem223 = await fixture.GetJsonAsync<CartridgeDetailDto>("/api/cartridges/223-remington");

        Assert.Equal(12, cartridges.TotalCount);
        Assert.Equal(["sks", "ak-47", "akm"], m43.Firearms.Select(f => f.Slug)); // no draft carbine
        Assert.Equal(["5-56x45mm-nato"], rem223.Children.Select(c => c.Slug));
        Assert.Equal("223-remington", (await fixture.GetJsonAsync<CartridgeDetailDto>("/api/cartridges/5-56x45mm-nato")).Parent?.Slug);
    }

    [Fact]
    public async Task Mechanism_tree_nests_children_under_their_roots()
    {
        var roots = await fixture.GetJsonAsync<List<MechanismNodeDto>>("/api/mechanisms");

        Assert.Equal(["blowback", "gas-operated", "manual", "recoil-operated"], roots.Select(r => r.Slug).Order());
        Assert.Equal(4, roots.Single(r => r.Slug == "gas-operated").Children.Count);
    }

    [Fact]
    public async Task Mechanism_detail_includes_ancestors_and_firearms_below_it()
    {
        var gas = await fixture.GetJsonAsync<MechanismDetailDto>("/api/mechanisms/gas-operated");
        var shortStroke = await fixture.GetJsonAsync<MechanismDetailDto>("/api/mechanisms/short-stroke-piston");

        Assert.Equal(11, gas.Firearms.Count);
        Assert.Empty(gas.Ancestors);
        Assert.Equal(["gas-operated"], shortStroke.Ancestors.Select(a => a.Slug));
        Assert.Equal(["sks", "m14"], shortStroke.Firearms.Select(f => f.Slug));
    }

    [Fact]
    public async Task Countries_and_tags_are_listed()
    {
        Assert.Equal(13, (await fixture.GetJsonAsync<List<CountryDto>>("/api/countries")).Count);
        Assert.Equal(16, (await fixture.GetJsonAsync<List<TagDto>>("/api/tags")).Count);
    }

    [Fact]
    public async Task Timeline_lists_dated_firearms_in_range_oldest_first()
    {
        var timeline = await fixture.GetJsonAsync<TimelineDto>("/api/timeline?from=1940&to=1950");

        Assert.Equal(["stg-44", "sks", "ak-47", "akm"], timeline.Items.Select(i => i.Slug));
        Assert.Equal(1959, timeline.Items[^1].DesignYearTo);
        Assert.Equal(4, timeline.Total);
    }

    [Fact]
    public async Task Timeline_stops_at_500_and_counts_the_rest()
    {
        // 501 firearms designed in 1700, a year nothing else uses. Tests in this collection run one at a time.
        await using var db = fixture.NewDbContext();
        var many = Enumerable.Range(1, 501).Select(i => new Firearm
        {
            Slug = $"timeline-probe-{i}",
            Name = $"Timeline Probe {i:000}",
            Status = PublishStatus.Published,
            DesignDate = FuzzyDate.InYear(1700),
        }).ToList();
        db.Firearms.AddRange(many);
        await db.SaveChangesAsync();
        try
        {
            var timeline = await fixture.GetJsonAsync<TimelineDto>("/api/timeline?from=1700&to=1700");

            Assert.Equal(501, timeline.Total);
            Assert.Equal(500, timeline.Items.Count);
            Assert.Equal("Timeline Probe 001", timeline.Items[0].Name); // oldest first, then by name
        }
        finally
        {
            db.Firearms.RemoveRange(many);
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("/api/people/nobody")]
    [InlineData("/api/organizations/nobody")]
    [InlineData("/api/cartridges/nothing")]
    [InlineData("/api/mechanisms/nothing")]
    public async Task Unknown_slugs_are_404(string path)
    {
        using var client = fixture.Client();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }
}
