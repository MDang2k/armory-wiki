using System.Linq.Expressions;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>A reference to another public entity.</summary>
public sealed record SlugName(string Slug, string Name);

public sealed record FuzzyDateDto(int Year, int? Month, int? Day, int? YearTo, DatePrecision Precision, bool IsApproximate)
{
    /// <summary>Runs client-side inside EF projections.</summary>
    public static FuzzyDateDto? From(FuzzyDate? d) =>
        d is null ? null : new(d.Year, d.Month, d.Day, d.YearTo, d.Precision, d.IsApproximate);
}

public static class FuzzyDateDtoExtensions
{
    public static FuzzyDate? ToEntity(this FuzzyDateDto? d) =>
        d is null ? null : new FuzzyDate { Year = d.Year, Month = d.Month, Day = d.Day, YearTo = d.YearTo, Precision = d.Precision, IsApproximate = d.IsApproximate };
}

public sealed record TagDto(string Slug, string Name, TagGroup Group);

/// <summary>A firearm mentioned on another page (a designer's firearms, a cartridge's firearms, ...).</summary>
public sealed record FirearmRefDto(string Slug, string Name, FuzzyDateDto? DesignDate);

public static class Paging
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 100;

    /// <summary>Counts and pages the source query, then projects only the requested page.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<TSource, T>(
        this IQueryable<TSource> query, Expression<Func<TSource, T>> selector, int? page, int? pageSize, CancellationToken ct)
    {
        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).Select(selector).ToListAsync(ct);
        return new PagedResult<T>(items, p, size, total);
    }
}

public static class QueryValues
{
    /// <summary>Comma-separated filter values: "a,b , c" → [a, b, c]. Empty means "no filter".</summary>
    public static IReadOnlyList<string> Csv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.ToLowerInvariant())
                .Distinct()
                .ToList();
}

public static class SlugResults
{
    public static ProblemHttpResult NotFound(string entity, string slug) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: $"{entity} not found",
            detail: $"No published {entity.ToLowerInvariant()} has the slug '{slug}'.");

    /// <summary>
    /// For a slug that no longer exists: the entity's current slug when it was renamed (and is published), else null.
    /// Endpoints answer 301 to the current slug, and the frontend turns that into its own permanent redirect.
    /// </summary>
    public static async Task<string?> FindCurrentSlugAsync(ArmoryWikiDbContext db, SluggedEntity type, string slug, CancellationToken ct)
    {
        var id = await db.SlugRedirects
            .Where(r => r.EntityType == type && r.OldSlug == slug)
            .Select(r => (int?)r.EntityId)
            .FirstOrDefaultAsync(ct);

        return id is not { } entityId ? null : type switch
        {
            SluggedEntity.Firearm => await db.Firearms.Where(x => x.Id == entityId).Select(x => x.Slug).FirstOrDefaultAsync(ct),
            SluggedEntity.Person => await db.People.Where(x => x.Id == entityId).Select(x => x.Slug).FirstOrDefaultAsync(ct),
            SluggedEntity.Organization => await db.Organizations.Where(x => x.Id == entityId).Select(x => x.Slug).FirstOrDefaultAsync(ct),
            SluggedEntity.Cartridge => await db.Cartridges.Where(x => x.Id == entityId).Select(x => x.Slug).FirstOrDefaultAsync(ct),
            SluggedEntity.Mechanism => await db.OperatingMechanisms.Where(x => x.Id == entityId).Select(x => x.Slug).FirstOrDefaultAsync(ct),
            _ => null,
        };
    }
}

public static class ResponseMetadataExtensions
{
    /// <summary>Documents the 301 (renamed slug) and 404 (unknown or draft) answers of a slug endpoint.</summary>
    public static RouteHandlerBuilder ProducesSlugMisses(this RouteHandlerBuilder builder) =>
        builder.Produces(StatusCodes.Status301MovedPermanently).ProducesProblem(StatusCodes.Status404NotFound);
}

public static class CacheControlExtensions
{
    /// <summary>Sets Cache-Control before the handler runs, so an endpoint's value overrides its group's.</summary>
    public static TBuilder WithCacheControl<TBuilder>(this TBuilder builder, string value)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = value;
            return await next(context);
        });
}
