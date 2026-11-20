using ArmoryWiki.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static ArmoryWiki.Api.Common.QueryValues;

namespace ArmoryWiki.Api.Features.Firearms;

/// <summary>Query parameters shared by the firearm list and its facets. List parameters are comma-separated slugs.</summary>
public sealed class FirearmQuery
{
    [FromQuery(Name = "q")] public string? Q { get; init; }
    [FromQuery(Name = "tag")] public string? Tag { get; init; }
    [FromQuery(Name = "country")] public string? Country { get; init; }
    [FromQuery(Name = "designer")] public string? Designer { get; init; }
    [FromQuery(Name = "manufacturer")] public string? Manufacturer { get; init; }
    [FromQuery(Name = "cartridge")] public string? Cartridge { get; init; }
    [FromQuery(Name = "mechanism")] public string? Mechanism { get; init; }
    [FromQuery(Name = "designedFrom")] public int? DesignedFrom { get; init; }
    [FromQuery(Name = "designedTo")] public int? DesignedTo { get; init; }

    /// <summary>The facet whose own filter is skipped when counting it (disjunctive faceting).</summary>
    public enum Facet { None, Tag, Country, Mechanism, Decade }

    public IQueryable<Firearm> ApplyFilters(IQueryable<Firearm> q, Facet except = Facet.None)
    {
        if (except != Facet.Tag)
        {
            q = q.WithTags(Csv(Tag));
        }

        if (except != Facet.Country)
        {
            q = q.WithCountries(Csv(Country));
        }

        if (except != Facet.Mechanism)
        {
            q = q.WithMechanisms(Csv(Mechanism));
        }

        if (except != Facet.Decade)
        {
            q = q.DesignedBetween(DesignedFrom, DesignedTo);
        }

        return q.WithDesigners(Csv(Designer)).WithManufacturers(Csv(Manufacturer)).WithCartridges(Csv(Cartridge));
    }

    public string? SearchText => string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();

    public enum SearchMode { None, FullText, Trigram }

    /// <summary>Full-text first; trigram word similarity only when full-text finds nothing under the same filters.</summary>
    public async Task<SearchMode> ResolveSearchModeAsync(IQueryable<Firearm> filtered, CancellationToken ct)
    {
        if (SearchText is not { } text)
        {
            return SearchMode.None;
        }

        return await filtered.MatchingFullText(text).AnyAsync(ct) ? SearchMode.FullText : SearchMode.Trigram;
    }

    public IQueryable<Firearm> ApplySearch(IQueryable<Firearm> q, SearchMode mode) => mode switch
    {
        SearchMode.FullText => q.MatchingFullText(SearchText!),
        SearchMode.Trigram => q.MatchingTrigrams(SearchText!),
        _ => q,
    };
}
