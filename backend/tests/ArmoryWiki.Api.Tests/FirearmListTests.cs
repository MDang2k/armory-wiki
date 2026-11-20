using System.Net;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Firearms;

namespace ArmoryWiki.Api.Tests;

[Collection(nameof(DatabaseCollection))]
public sealed class FirearmListTests(DatabaseFixture fixture)
{
    private async Task<PagedResult<FirearmListItemDto>> ListAsync(string query) =>
        await fixture.GetJsonAsync<PagedResult<FirearmListItemDto>>($"/api/firearms?pageSize=100&{query}");

    private async Task<string[]> SlugsAsync(string query) =>
        [.. (await ListAsync($"sort=name&{query}")).Items.Select(i => i.Slug).Order()];

    [Theory]
    [InlineData("tag=carbine", "m4-carbine,sks")]
    [InlineData("country=nazi-germany", "karabiner-98k,stg-44")]
    [InlineData("designer=eugene-stoner", "ar-10,ar-15,m16")]
    [InlineData("designer=springfield-armory", "m14")]
    [InlineData("manufacturer=colts-manufacturing-company", "ar-15,m16,m1911,m4-carbine")]
    [InlineData("cartridge=7-62x39mm", "ak-47,akm,sks")]
    [InlineData("mechanism=recoil-operated", "browning-hi-power,luger-p08,m1911")]
    [InlineData("designedFrom=1940&designedTo=1945", "sks,stg-44")]
    public async Task Each_filter_alone_returns_exactly_the_matching_published_firearms(string query, string expected)
    {
        Assert.Equal(expected.Split(','), await SlugsAsync(query));
    }

    [Theory]
    [InlineData("tag=carbine,battle-rifle", "ar-10,m14,m4-carbine,sks")]
    [InlineData("country=nazi-germany,belgium", "browning-hi-power,karabiner-98k,stg-44")]
    public async Task Values_within_a_filter_are_ored(string query, string expected)
    {
        Assert.Equal(expected.Split(','), await SlugsAsync(query));
    }

    [Theory]
    [InlineData("mechanism=%25")] // "%"
    [InlineData("mechanism=gas_operated")]
    public async Task Mechanism_values_are_not_like_wildcards(string query)
    {
        Assert.Empty(await SlugsAsync(query));
    }

    [Fact]
    public async Task Different_filters_are_anded()
    {
        Assert.Equal(["ak-47", "ak-74", "akm"], await SlugsAsync("tag=assault-rifle&country=soviet-union"));
    }

    [Fact]
    public async Task Mechanism_filter_includes_descendants()
    {
        var slugs = await SlugsAsync("mechanism=gas-operated");

        Assert.Equal(11, slugs.Length);
        Assert.Contains("sks", slugs); // short-stroke piston
        Assert.Contains("ak-47", slugs); // long-stroke piston
        Assert.Contains("m16", slugs); // direct impingement
    }

    [Fact]
    public async Task Design_year_filter_matches_ranges_that_overlap()
    {
        // The AKM is dated "1950s" (1950–1959); no seeded firearm has a single design year in 1955–1956.
        Assert.Equal(["akm"], await SlugsAsync("designedFrom=1955&designedTo=1956"));
    }

    [Fact]
    public async Task Full_text_search_ranks_the_best_match_first()
    {
        var result = await ListAsync("q=kalashnikov");

        Assert.Equal("ak-47", result.Items[0].Slug);
        Assert.DoesNotContain(result.Items, i => i.Slug == DatabaseFixture.DraftSlug);
    }

    [Fact]
    public async Task Search_falls_back_to_trigrams_when_full_text_finds_nothing()
    {
        var result = await ListAsync("q=ak47");

        Assert.Equal("ak-47", result.Items[0].Slug);
    }

    [Fact]
    public async Task List_items_carry_country_mechanism_date_and_primary_cartridge()
    {
        var ak47 = (await ListAsync("q=kalashnikov")).Items[0];

        Assert.Equal("soviet-union", ak47.Country?.Slug);
        Assert.Equal("long-stroke-piston", ak47.Mechanism?.Slug);
        Assert.Equal(1947, ak47.DesignDate?.Year);
        Assert.Equal("7-62x39mm", ak47.PrimaryCartridge?.Slug);
        Assert.Contains(ak47.Tags, t => t.Slug == "assault-rifle");
    }

    [Theory]
    [InlineData("pageSize=5&page=3", 3, 5, 5)]
    [InlineData("pageSize=5&page=4", 4, 5, 0)]
    [InlineData("page=0", 1, 24, 15)]
    [InlineData("pageSize=1000", 1, 100, 15)]
    public async Task Paging_is_clamped_and_reports_the_total(string query, int page, int pageSize, int itemCount)
    {
        var result = await fixture.GetJsonAsync<PagedResult<FirearmListItemDto>>($"/api/firearms?{query}");

        Assert.Equal(page, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.Equal(itemCount, result.Items.Count);
        Assert.Equal(15, result.TotalCount);
    }

    [Fact]
    public async Task Unknown_sort_is_a_bad_request()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/firearms?sort=price");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task List_is_publicly_cacheable_for_a_minute()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/firearms");

        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Facets_ignore_their_own_filter_but_apply_the_others()
    {
        var facets = await fixture.GetJsonAsync<FirearmFacetsDto>("/api/firearms/facets?tag=carbine");

        // Tag counts ignore tag=carbine: the other tags stay selectable.
        Assert.Equal(7, facets.Tags.Single(t => t.Slug == "assault-rifle").Count);
        Assert.Equal(2, facets.Tags.Single(t => t.Slug == "carbine").Count); // the draft carbine is not counted
        // Every other facet is narrowed to the carbines (SKS, M4).
        Assert.Equal([("soviet-union", 1), ("united-states", 1)], facets.Countries.Select(c => (c.Slug, c.Count)).Order());
        Assert.Equal(2, facets.Mechanisms.Single(m => m.Slug == "gas-operated").Count);
        Assert.Equal(1, facets.Mechanisms.Single(m => m.Slug == "short-stroke-piston").Count);
        Assert.Equal([new DecadeFacetDto(1940, 1)], facets.Decades);
    }

    [Fact]
    public async Task Decade_facet_ignores_the_design_year_filter()
    {
        var facets = await fixture.GetJsonAsync<FirearmFacetsDto>("/api/firearms/facets?designedFrom=1940&designedTo=1949");

        // Other decades stay selectable...
        Assert.Contains(facets.Decades, d => d is { Decade: 1970, Count: 1 }); // AK-74
        // ...while the country facet is narrowed to 1940s designs (StG 44, SKS, AK-47).
        Assert.Equal([("nazi-germany", 1), ("soviet-union", 2)], facets.Countries.Select(c => (c.Slug, c.Count)).Order());
    }
}
