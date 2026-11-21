using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Cartridges;

public sealed record CartridgeListItemDto(string Slug, string Name, CartridgeType? Type, SlugName? Country, FuzzyDateDto? DesignDate);

public sealed record CartridgeDetailDto(
    string Slug,
    string Name,
    IReadOnlyList<string> AlternativeNames,
    string? WikidataId,
    CartridgeType? Type,
    decimal? BulletDiameterMm,
    decimal? CaseLengthMm,
    decimal? OverallLengthMm,
    SlugName? Country,
    FuzzyDateDto? DesignDate,
    string? Description,
    SlugName? Parent,
    IReadOnlyList<SlugName> Children,
    IReadOnlyList<FirearmRefDto> Firearms,
    IReadOnlyList<CitationDto> Citations,
    IReadOnlyList<ImageDto> Images);

public static class CartridgeEndpoints
{
    private const string Route = "/api/cartridges";

    public static void MapCartridges(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/cartridges").WithTags("Cartridges");
        group.MapGet("/", List);
        group.MapGet("/{slug}", Detail).ProducesSlugMisses();
    }

    private static async Task<Ok<PagedResult<CartridgeListItemDto>>> List(int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Cartridges.OrderBy(c => c.Name).ThenBy(c => c.Slug).ToPagedAsync(
            c => new CartridgeListItemDto(
                c.Slug,
                c.Name,
                c.Type,
                c.CountryOfOrigin == null ? null : new SlugName(c.CountryOfOrigin.Slug, c.CountryOfOrigin.Name),
                FuzzyDateDto.From(c.DesignDate)),
            page, pageSize, ct));

    private static async Task<Results<Ok<CartridgeDetailDto>, RedirectHttpResult, ProblemHttpResult>> Detail(
        string slug, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var cartridge = await db.Cartridges.Where(c => c.Slug == slug).Select(c => new
        {
            c.Id,
            Dto = new CartridgeDetailDto(
                c.Slug,
                c.Name,
                c.AlternativeNames,
                c.WikidataId,
                c.Type,
                c.BulletDiameterMm,
                c.CaseLengthMm,
                c.OverallLengthMm,
                c.CountryOfOrigin == null ? null : new SlugName(c.CountryOfOrigin.Slug, c.CountryOfOrigin.Name),
                FuzzyDateDto.From(c.DesignDate),
                c.Description,
                c.ParentCartridge == null ? null : new SlugName(c.ParentCartridge.Slug, c.ParentCartridge.Name),
                Array.Empty<SlugName>(),
                Array.Empty<FirearmRefDto>(),
                Array.Empty<CitationDto>(),
                c.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => ImageDto.From(i)).ToList()),
        }).FirstOrDefaultAsync(ct);

        if (cartridge is null)
        {
            if (await SlugResults.FindCurrentSlugAsync(db, SluggedEntity.Cartridge, slug, ct) is { } current)
            {
                return TypedResults.Redirect($"{Route}/{current}", permanent: true);
            }

            return SlugResults.NotFound("Cartridge", slug);
        }

        var children = await db.Cartridges
            .Where(c => c.ParentCartridgeId == cartridge.Id)
            .OrderBy(c => c.Name)
            .Select(c => new SlugName(c.Slug, c.Name))
            .ToListAsync(ct);

        var firearms = await db.Firearms
            .Where(f => f.Variants.Any(v => v.Chamberings.Any(ch => ch.CartridgeId == cartridge.Id)))
            .OrderByDesignDate()
            .Select(f => new FirearmRefDto(f.Slug, f.Name, FuzzyDateDto.From(f.DesignDate)))
            .ToListAsync(ct);

        var citations = await db.Citations.Where(c => c.CartridgeId == cartridge.Id).OrderBy(c => c.Id).Select(CitationProjection.ToDto).ToListAsync(ct);
        return TypedResults.Ok(cartridge.Dto with { Children = children, Firearms = firearms, Citations = citations });
    }
}
