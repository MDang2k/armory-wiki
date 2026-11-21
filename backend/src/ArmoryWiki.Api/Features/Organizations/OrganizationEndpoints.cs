using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.People;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Organizations;

public sealed record OrganizationListItemDto(string Slug, string Name, OrganizationType? Type, SlugName? Country, FuzzyDateDto? Founded);

public sealed record ManufacturedFirearmDto(string Slug, string Name, bool IsLicensed, FuzzyDateDto? From, FuzzyDateDto? To);

public sealed record OrganizationDetailDto(
    string Slug,
    string Name,
    OrganizationType? Type,
    SlugName? Country,
    FuzzyDateDto? Founded,
    string? Description,
    string? WikidataId,
    IReadOnlyList<DesignedFirearmDto> Designed,
    IReadOnlyList<ManufacturedFirearmDto> Manufactured,
    IReadOnlyList<CitationDto> Citations,
    IReadOnlyList<ImageDto> Images);

public static class OrganizationEndpoints
{
    private const string Route = "/api/organizations";

    public static void MapOrganizations(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations").WithTags("Organizations");
        group.MapGet("/", List);
        group.MapGet("/{slug}", Detail).ProducesSlugMisses();
    }

    private static async Task<Ok<PagedResult<OrganizationListItemDto>>> List(int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Organizations.OrderBy(o => o.Name).ThenBy(o => o.Slug).ToPagedAsync(
            o => new OrganizationListItemDto(
                o.Slug,
                o.Name,
                o.Type,
                o.Country == null ? null : new SlugName(o.Country.Slug, o.Country.Name),
                FuzzyDateDto.From(o.Founded)),
            page, pageSize, ct));

    private static async Task<Results<Ok<OrganizationDetailDto>, RedirectHttpResult, ProblemHttpResult>> Detail(
        string slug, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var org = await db.Organizations.Where(o => o.Slug == slug).Select(o => new
        {
            o.Id,
            Dto = new OrganizationDetailDto(
                o.Slug,
                o.Name,
                o.Type,
                o.Country == null ? null : new SlugName(o.Country.Slug, o.Country.Name),
                FuzzyDateDto.From(o.Founded),
                o.Description,
                o.WikidataId,
                Array.Empty<DesignedFirearmDto>(),
                Array.Empty<ManufacturedFirearmDto>(),
                Array.Empty<CitationDto>(),
                o.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => ImageDto.From(i)).ToList()),
        }).FirstOrDefaultAsync(ct);

        if (org is null)
        {
            if (await SlugResults.FindCurrentSlugAsync(db, SluggedEntity.Organization, slug, ct) is { } current)
            {
                return TypedResults.Redirect($"{Route}/{current}", permanent: true);
            }

            return SlugResults.NotFound("Organization", slug);
        }

        var designed = await db.FirearmDesigners
            .Where(d => d.OrganizationId == org.Id)
            .OrderBy(d => d.Firearm.DesignDate == null).ThenBy(d => d.Firearm.DesignDate!.Year).ThenBy(d => d.Firearm.Name)
            .Select(d => new DesignedFirearmDto(d.Firearm.Slug, d.Firearm.Name, d.Role, FuzzyDateDto.From(d.Firearm.DesignDate)))
            .ToListAsync(ct);

        var manufactured = await db.FirearmManufacturers
            .Where(m => m.OrganizationId == org.Id)
            .OrderBy(m => m.Firearm.Name)
            .Select(m => new ManufacturedFirearmDto(m.Firearm.Slug, m.Firearm.Name, m.IsLicensed, FuzzyDateDto.From(m.From), FuzzyDateDto.From(m.To)))
            .ToListAsync(ct);

        var citations = await db.Citations.Where(c => c.OrganizationId == org.Id).OrderBy(c => c.Id).Select(CitationProjection.ToDto).ToListAsync(ct);
        return TypedResults.Ok(org.Dto with { Designed = designed, Manufactured = manufactured, Citations = citations });
    }
}
