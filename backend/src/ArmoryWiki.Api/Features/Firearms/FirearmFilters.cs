using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace ArmoryWiki.Api.Features.Firearms;

/// <summary>
/// One composable filter per query parameter. An empty list means "no filter";
/// several values are OR-ed, and chaining filters AND-s them.
/// </summary>
public static class FirearmFilters
{
    public static IQueryable<Firearm> WithTags(this IQueryable<Firearm> q, IReadOnlyList<string> slugs) =>
        slugs.Count == 0 ? q : q.Where(f => f.Tags.Any(t => slugs.Contains(t.Slug)));

    public static IQueryable<Firearm> WithCountries(this IQueryable<Firearm> q, IReadOnlyList<string> slugs) =>
        slugs.Count == 0 ? q : q.Where(f => f.CountryOfOrigin != null && slugs.Contains(f.CountryOfOrigin.Slug));

    /// <summary>Designer slugs may name a person or an organization.</summary>
    public static IQueryable<Firearm> WithDesigners(this IQueryable<Firearm> q, IReadOnlyList<string> slugs) =>
        slugs.Count == 0 ? q : q.Where(f => f.Designers.Any(d =>
            (d.Person != null && slugs.Contains(d.Person.Slug))
            || (d.Organization != null && slugs.Contains(d.Organization.Slug))));

    public static IQueryable<Firearm> WithManufacturers(this IQueryable<Firearm> q, IReadOnlyList<string> slugs) =>
        slugs.Count == 0 ? q : q.Where(f => f.Manufacturers.Any(m => slugs.Contains(m.Organization.Slug)));

    /// <summary>Matches a cartridge any variant is chambered for, not only the primary one.</summary>
    public static IQueryable<Firearm> WithCartridges(this IQueryable<Firearm> q, IReadOnlyList<string> slugs) =>
        slugs.Count == 0 ? q : q.Where(f => f.Variants.Any(v => v.Chamberings.Any(c => slugs.Contains(c.Cartridge.Slug))));

    /// <summary>
    /// A mechanism includes all its descendants. Paths are made of slugs ("/gas-operated/short-stroke-piston/"),
    /// so "is gas-operated or below it" is "path contains /gas-operated/".
    /// </summary>
    public static IQueryable<Firearm> WithMechanisms(this IQueryable<Firearm> q, IReadOnlyList<string> slugs)
    {
        if (slugs.Count == 0)
        {
            return q;
        }

        var patterns = slugs.Select(s => $"%/{EscapeLike(s)}/%").ToArray();
        return q.Where(f => f.OperatingMechanism != null
            && patterns.Any(p => EF.Functions.Like(f.OperatingMechanism.Path, p)));
    }

    /// <summary>User input must not act as a wildcard. Backslash is Postgres's default LIKE escape character.</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>Undated firearms last; slug breaks ties so paging is deterministic.</summary>
    public static IOrderedQueryable<Firearm> OrderByDesignDate(this IQueryable<Firearm> q, bool descending = false)
    {
        var undatedLast = q.OrderBy(f => f.DesignDate == null);
        return (descending
                ? undatedLast.ThenByDescending(f => f.DesignDate!.Year)
                : undatedLast.ThenBy(f => f.DesignDate!.Year))
            .ThenBy(f => f.Name).ThenBy(f => f.Slug);
    }

    /// <summary>Overlap: a design date (or range) matches [from, to] when any of its years falls inside.</summary>
    public static IQueryable<Firearm> DesignedBetween(this IQueryable<Firearm> q, int? from, int? to)
    {
        if (from is null && to is null)
        {
            return q;
        }

        q = q.Where(f => f.DesignDate != null);
        if (from is { } start)
        {
            q = q.Where(f => (f.DesignDate!.YearTo ?? f.DesignDate.Year) >= start);
        }

        if (to is { } end)
        {
            q = q.Where(f => f.DesignDate!.Year <= end);
        }

        return q;
    }

    // Search -----------------------------------------------------------------------------------------------

    /// <summary>Below this word similarity a trigram match is noise ("ak47" vs AK-47 scores 0.6, vs AK-74 0.4).</summary>
    private const double MinWordSimilarity = 0.5;

    public static IQueryable<Firearm> MatchingFullText(this IQueryable<Firearm> q, string text) =>
        q.Where(f => EF.Property<NpgsqlTsVector>(f, ArmoryWikiDbContext.SearchVector)
            .Matches(EF.Functions.WebSearchToTsQuery("simple", text)));

    // ponytail: no index use (function call, not the <% operator); fine for thousands of rows,
    // switch to `<%` with SET pg_trgm.word_similarity_threshold if this gets slow.
    public static IQueryable<Firearm> MatchingTrigrams(this IQueryable<Firearm> q, string text) =>
        q.Where(f => EF.Functions.TrigramsWordSimilarity(text, EF.Property<string>(f, ArmoryWikiDbContext.SearchNames))
            >= MinWordSimilarity);

    public static IOrderedQueryable<Firearm> OrderByFullTextRank(this IQueryable<Firearm> q, string text) =>
        q.OrderByDescending(f => EF.Property<NpgsqlTsVector>(f, ArmoryWikiDbContext.SearchVector)
                .Rank(EF.Functions.WebSearchToTsQuery("simple", text)))
            .ThenBy(f => f.Name).ThenBy(f => f.Slug);

    public static IOrderedQueryable<Firearm> OrderByTrigramSimilarity(this IQueryable<Firearm> q, string text) =>
        q.OrderByDescending(f => EF.Functions.TrigramsWordSimilarity(text, EF.Property<string>(f, ArmoryWikiDbContext.SearchNames)))
            .ThenBy(f => f.Name).ThenBy(f => f.Slug);
}
