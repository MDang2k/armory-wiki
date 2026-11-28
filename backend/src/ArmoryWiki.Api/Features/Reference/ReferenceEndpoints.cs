using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Reference;

public sealed record CountryDto(string Slug, string Name, string? IsoCode, bool IsHistorical);

public sealed record TimelineItemDto(string Slug, string Name, int DesignYear, int? DesignYearTo, IReadOnlyList<string> Tags);

/// <summary>The first <see cref="ReferenceEndpoints.MaxTimelineItems"/> matches, oldest first; <see cref="Total"/> counts them all.</summary>
public sealed record TimelineDto(IReadOnlyList<TimelineItemDto> Items, int Total);

/// <summary>Small lookup lists, plus the timeline (a thin view over firearm filters).</summary>
public static class ReferenceEndpoints
{
    /// <summary>More than this is unreadable on one timeline: the page asks to narrow the range or filters.</summary>
    public const int MaxTimelineItems = 500;

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
    private static async Task<Ok<TimelineDto>> Timeline(
        int? from, int? to, string? country, string? tag, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var query = new FirearmQuery { Country = country, Tag = tag, DesignedFrom = from, DesignedTo = to };
        var dated = query.ApplyFilters(db.Firearms).Where(f => f.DesignDate != null);
        var total = await dated.CountAsync(ct);
        var items = await dated
            .OrderBy(f => f.DesignDate!.Year).ThenBy(f => f.Name)
            .Take(MaxTimelineItems)
            .Select(f => new TimelineItemDto(
                f.Slug,
                f.Name,
                f.DesignDate!.Year,
                f.DesignDate.YearTo,
                f.Tags.OrderBy(t => t.Name).Select(t => t.Slug).ToList()))
            .ToListAsync(ct);
        return TypedResults.Ok(new TimelineDto(items, total));
    }
}
