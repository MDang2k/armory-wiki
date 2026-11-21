using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.People;

public sealed record PersonListItemDto(string Slug, string FullName, FuzzyDateDto? BirthDate, FuzzyDateDto? DeathDate, SlugName? Nationality);

public sealed record DesignedFirearmDto(string Slug, string Name, DesignerRole Role, FuzzyDateDto? DesignDate);

public sealed record PersonDetailDto(
    string Slug,
    string FullName,
    FuzzyDateDto? BirthDate,
    FuzzyDateDto? DeathDate,
    SlugName? Nationality,
    string? Biography,
    string? WikidataId,
    IReadOnlyList<DesignedFirearmDto> Firearms,
    IReadOnlyList<CitationDto> Citations,
    IReadOnlyList<ImageDto> Images);

public static class PersonEndpoints
{
    private const string Route = "/api/people";

    public static void MapPeople(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/people").WithTags("People");
        group.MapGet("/", List);
        group.MapGet("/{slug}", Detail).ProducesSlugMisses();
    }

    private static async Task<Ok<PagedResult<PersonListItemDto>>> List(int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.People.OrderBy(p => p.FullName).ThenBy(p => p.Slug).ToPagedAsync(
            p => new PersonListItemDto(
                p.Slug,
                p.FullName,
                FuzzyDateDto.From(p.BirthDate),
                FuzzyDateDto.From(p.DeathDate),
                p.Nationality == null ? null : new SlugName(p.Nationality.Slug, p.Nationality.Name)),
            page, pageSize, ct));

    private static async Task<Results<Ok<PersonDetailDto>, RedirectHttpResult, ProblemHttpResult>> Detail(
        string slug, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var person = await db.People.Where(p => p.Slug == slug).Select(p => new
        {
            p.Id,
            Dto = new PersonDetailDto(
                p.Slug,
                p.FullName,
                FuzzyDateDto.From(p.BirthDate),
                FuzzyDateDto.From(p.DeathDate),
                p.Nationality == null ? null : new SlugName(p.Nationality.Slug, p.Nationality.Name),
                p.Biography,
                p.WikidataId,
                Array.Empty<DesignedFirearmDto>(),
                Array.Empty<CitationDto>(),
                p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => ImageDto.From(i)).ToList()),
        }).FirstOrDefaultAsync(ct);

        if (person is null)
        {
            if (await SlugResults.FindCurrentSlugAsync(db, SluggedEntity.Person, slug, ct) is { } current)
            {
                return TypedResults.Redirect($"{Route}/{current}", permanent: true);
            }

            return SlugResults.NotFound("Person", slug);
        }

        var firearms = await db.FirearmDesigners
            .Where(d => d.PersonId == person.Id)
            .OrderBy(d => d.Firearm.DesignDate == null).ThenBy(d => d.Firearm.DesignDate!.Year).ThenBy(d => d.Firearm.Name)
            .Select(d => new DesignedFirearmDto(d.Firearm.Slug, d.Firearm.Name, d.Role, FuzzyDateDto.From(d.Firearm.DesignDate)))
            .ToListAsync(ct);

        var citations = await db.Citations.Where(c => c.PersonId == person.Id).OrderBy(c => c.Id).Select(CitationProjection.ToDto).ToListAsync(ct);
        return TypedResults.Ok(person.Dto with { Firearms = firearms, Citations = citations });
    }
}
