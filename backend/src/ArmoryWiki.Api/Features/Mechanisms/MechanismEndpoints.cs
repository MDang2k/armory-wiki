using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Mechanisms;

public sealed record MechanismNodeDto(string Slug, string Name, IReadOnlyList<MechanismNodeDto> Children);

public sealed record MechanismDetailDto(
    string Slug,
    string Name,
    string? Explanation,
    /// <summary>From the root down to the direct parent.</summary>
    IReadOnlyList<SlugName> Ancestors,
    IReadOnlyList<SlugName> Children,
    /// <summary>Firearms using this mechanism or any mechanism below it.</summary>
    IReadOnlyList<FirearmRefDto> Firearms);

public static class MechanismEndpoints
{
    private const string Route = "/api/mechanisms";

    public static void MapMechanisms(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/mechanisms").WithTags("Mechanisms");
        group.MapGet("/", Tree);
        group.MapGet("/{slug}", Detail).ProducesSlugMisses();
    }

    private sealed record Row(int Id, int? ParentId, string Slug, string Name);

    private static async Task<Ok<IReadOnlyList<MechanismNodeDto>>> Tree(ArmoryWikiDbContext db, CancellationToken ct)
    {
        var rows = await db.OperatingMechanisms
            .OrderBy(m => m.Name)
            .Select(m => new Row(m.Id, m.ParentId, m.Slug, m.Name))
            .ToListAsync(ct);
        var byParent = rows.ToLookup(r => r.ParentId);

        IReadOnlyList<MechanismNodeDto> Build(int? parentId) =>
            [.. byParent[parentId].Select(r => new MechanismNodeDto(r.Slug, r.Name, Build(r.Id)))];

        return TypedResults.Ok(Build(null));
    }

    private static async Task<Results<Ok<MechanismDetailDto>, RedirectHttpResult, ProblemHttpResult>> Detail(
        string slug, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var mechanism = await db.OperatingMechanisms
            .Where(m => m.Slug == slug)
            .Select(m => new
            {
                m.Slug,
                m.Name,
                m.Path,
                m.Explanation,
                Children = m.Children.OrderBy(c => c.Name).Select(c => new SlugName(c.Slug, c.Name)).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (mechanism is null)
        {
            if (await SlugResults.FindCurrentSlugAsync(db, SluggedEntity.Mechanism, slug, ct) is { } current)
            {
                return TypedResults.Redirect($"{Route}/{current}", permanent: true);
            }

            return SlugResults.NotFound("Mechanism", slug);
        }

        // The path lists the ancestors' slugs: "/gas-operated/short-stroke-piston/" → ["gas-operated"].
        var ancestorSlugs = mechanism.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).ToList();
        var ancestors = await db.OperatingMechanisms
            .Where(m => ancestorSlugs.Contains(m.Slug))
            .OrderBy(m => m.Path.Length)
            .Select(m => new SlugName(m.Slug, m.Name))
            .ToListAsync(ct);

        var firearms = await db.Firearms
            .WithMechanisms([mechanism.Slug])
            .OrderByDesignDate()
            .Select(f => new FirearmRefDto(f.Slug, f.Name, FuzzyDateDto.From(f.DesignDate)))
            .ToListAsync(ct);

        return TypedResults.Ok(new MechanismDetailDto(
            mechanism.Slug, mechanism.Name, mechanism.Explanation, ancestors, mechanism.Children, firearms));
    }
}
