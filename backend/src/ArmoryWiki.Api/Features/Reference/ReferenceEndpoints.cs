using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Reference;

public sealed record CountryDto(string Slug, string Name, string? IsoCode, bool IsHistorical);

public sealed record TimelineItemDto(string Slug, string Name, int DesignYear, int? DesignYearTo, IReadOnlyList<string> Tags);

/// <summary>Small lookup lists, plus the timeline (a thin view over firearm filters).</summary>
public static class ReferenceEndpoints
{
    public static void MapReference(this RouteGroupBuilder api)
    {
        api.MapGet("/countries", Countries).WithTags("Reference");
        api.MapGet("/tags", Tags).WithTags("Reference");
        api.MapGet("/timeline", Timeline).WithTags("Firearms");
    }

    private static async Task<Ok<List<CountryDto>>> Countries(ArmoryWikiDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Countries
            .OrderBy(c => c.Name)
            .Select(c => new CountryDto(c.Slug, c.Name, c.IsoCode, c.IsHistorical))
            .ToListAsync(ct));

    private static async Task<Ok<List<TagDto>>> Tags(ArmoryWikiDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Tags
            .OrderBy(t => t.Group).ThenBy(t => t.Name)
            .Select(t => new TagDto(t.Slug, t.Name, t.Group))
            .ToListAsync(ct));

    /// <summary>Firearms with a known design date, oldest first. Country and tag accept comma-separated slugs.</summary>
    // ponytail: unpaged; page or cap it if the imported dataset makes responses too large.
    private static async Task<Ok<List<TimelineItemDto>>> Timeline(
        int? from, int? to, string? country, string? tag, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var query = new FirearmQuery { Country = country, Tag = tag, DesignedFrom = from, DesignedTo = to };
        return TypedResults.Ok(await query.ApplyFilters(db.Firearms)
            .Where(f => f.DesignDate != null)
            .OrderBy(f => f.DesignDate!.Year).ThenBy(f => f.Name)
            .Select(f => new TimelineItemDto(
                f.Slug,
                f.Name,
                f.DesignDate!.Year,
                f.DesignDate.YearTo,
                f.Tags.OrderBy(t => t.Name).Select(t => t.Slug).ToList()))
            .ToListAsync(ct));
    }
}
