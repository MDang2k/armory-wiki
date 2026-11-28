using System.Linq.Expressions;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using static ArmoryWiki.Api.Features.Firearms.FirearmQuery;

namespace ArmoryWiki.Api.Features.Firearms;

public static class FirearmEndpoints
{
    private const string Route = "/api/firearms";
    private const int MaxCompared = 4;
    private const int MaxTreeDepth = 5;
    private const int MaxTreeNodes = 150;

    public static void MapFirearms(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/firearms").WithTags("Firearms");
        group.MapGet("/", List).WithCacheControl("public, max-age=60").ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/facets", Facets).WithCacheControl("public, max-age=60");
        group.MapGet("/compare", Compare)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{slug}", Detail).ProducesSlugMisses();
        group.MapGet("/{slug}/family-tree", FamilyTree).ProducesSlugMisses();
    }

    internal static readonly Expression<Func<Firearm, FirearmListItemDto>> ToListItem = f => new FirearmListItemDto(
        f.Slug,
        f.Name,
        f.Tags.OrderBy(t => t.Name).Select(t => new TagDto(t.Slug, t.Name, t.Group)).ToList(),
        f.CountryOfOrigin == null ? null : new SlugName(f.CountryOfOrigin.Slug, f.CountryOfOrigin.Name),
        FuzzyDateDto.From(f.DesignDate),
        f.OperatingMechanism == null ? null : new SlugName(f.OperatingMechanism.Slug, f.OperatingMechanism.Name),
        f.Variants.Where(v => v.IsBaseVariant).SelectMany(v => v.Chamberings).Where(c => c.IsPrimary)
            .Select(c => new SlugName(c.Cartridge.Slug, c.Cartridge.Name)).FirstOrDefault(),
        f.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => ImageUrls.Prefix + i.ContentHash + ImageUrls.ThumbFile).FirstOrDefault());

    private static async Task<Results<Ok<PagedResult<FirearmListItemDto>>, ProblemHttpResult>> List(
        [AsParameters] FirearmQuery query, string? sort, int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var filtered = query.ApplyFilters(db.Firearms);
        var mode = await query.ResolveSearchModeAsync(filtered, ct);
        var matches = query.ApplySearch(filtered, mode);

        IQueryable<Firearm>? ordered = (sort, mode) switch
        {
            ("name", _) => matches.OrderBy(f => f.Name).ThenBy(f => f.Slug),
            ("designYear", _) => matches.OrderByDesignDate(),
            ("-designYear", _) => matches.OrderByDesignDate(descending: true),
            ("relevance" or null, SearchMode.FullText) => matches.OrderByFullTextRank(query.SearchText!),
            ("relevance" or null, SearchMode.Trigram) => matches.OrderByTrigramSimilarity(query.SearchText!),
            ("relevance" or null, SearchMode.None) => matches.OrderBy(f => f.Name).ThenBy(f => f.Slug),
            _ => null,
        };

        if (ordered is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown sort",
                detail: "sort must be one of: name, designYear, -designYear, relevance.");
        }

        return TypedResults.Ok(await ordered.ToPagedAsync(ToListItem, page, pageSize, ct));
    }

    private static async Task<Ok<FirearmFacetsDto>> Facets([AsParameters] FirearmQuery query, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var mode = await query.ResolveSearchModeAsync(query.ApplyFilters(db.Firearms), ct);
        IQueryable<Firearm> WithoutOwnFilter(Facet facet) => query.ApplySearch(query.ApplyFilters(db.Firearms, facet), mode);

        var tags = await WithoutOwnFilter(Facet.Tag)
            .SelectMany(f => f.Tags)
            .GroupBy(t => new { t.Slug, t.Name, t.Group })
            .Select(g => new TagFacetDto(g.Key.Slug, g.Key.Name, g.Key.Group, g.Count()))
            .ToListAsync(ct);

        var countries = await WithoutOwnFilter(Facet.Country)
            .Where(f => f.CountryOfOrigin != null)
            .GroupBy(f => new { f.CountryOfOrigin!.Slug, f.CountryOfOrigin.Name })
            .Select(g => new FacetCountDto(g.Key.Slug, g.Key.Name, g.Count()))
            .ToListAsync(ct);

        // Count per exact mechanism, then roll counts up the tree in memory (the tree is small).
        var perPath = await WithoutOwnFilter(Facet.Mechanism)
            .Where(f => f.OperatingMechanism != null)
            .GroupBy(f => f.OperatingMechanism!.Path)
            .Select(g => new { Path = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var mechanisms = (await db.OperatingMechanisms.OrderBy(m => m.Path).Select(m => new { m.Slug, m.Name, m.Path }).ToListAsync(ct))
            .Select(m => new FacetCountDto(m.Slug, m.Name, perPath.Where(p => p.Path.StartsWith(m.Path, StringComparison.Ordinal)).Sum(p => p.Count)))
            .Where(m => m.Count > 0)
            .ToList();

        // ponytail: a design-date range counts only in its first decade; spread across decades if timelines need it.
        var decades = await WithoutOwnFilter(Facet.Decade)
            .Where(f => f.DesignDate != null)
            .GroupBy(f => f.DesignDate!.Year / 10 * 10)
            .Select(g => new DecadeFacetDto(g.Key, g.Count()))
            .ToListAsync(ct);

        return TypedResults.Ok(new FirearmFacetsDto(
            [.. tags.OrderBy(t => t.Group).ThenByDescending(t => t.Count).ThenBy(t => t.Name)],
            [.. countries.OrderByDescending(c => c.Count).ThenBy(c => c.Name)],
            mechanisms,
            [.. decades.OrderBy(d => d.Decade)]));
    }

    private static async Task<Results<Ok<FirearmDetailDto>, RedirectHttpResult, ProblemHttpResult>> Detail(
        string slug, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var dto = await db.Firearms
            .Where(f => f.Slug == slug)
            .AsSplitQuery()
            .Select(f => new FirearmDetailDto(
                f.Slug,
                f.Name,
                f.AlternativeNames,
                f.WikidataId,
                f.Tags.OrderBy(t => t.Name).Select(t => new TagDto(t.Slug, t.Name, t.Group)).ToList(),
                f.CountryOfOrigin == null ? null : new SlugName(f.CountryOfOrigin.Slug, f.CountryOfOrigin.Name),
                FuzzyDateDto.From(f.DesignDate),
                FuzzyDateDto.From(f.ProductionStart),
                FuzzyDateDto.From(f.ProductionEnd),
                f.OperatingMechanism == null ? null : new SlugName(f.OperatingMechanism.Slug, f.OperatingMechanism.Name),
                f.FeedSystem,
                f.Summary,
                f.DevelopmentHistory,
                f.ServiceHistory,
                f.WikipediaTitle == null ? null : new WikipediaSourceDto(f.WikipediaTitle, f.WikipediaRevisionId),
                f.Variants
                    .OrderByDescending(v => v.IsBaseVariant).ThenBy(v => v.Name)
                    .Select(v => new VariantDto(
                        v.Slug,
                        v.Name,
                        v.IsBaseVariant,
                        FuzzyDateDto.From(v.IntroducedDate),
                        SpecsDto.From(v),
                        v.ExtraSpecs,
                        v.Chamberings
                            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Cartridge.Name)
                            .Select(c => new ChamberingDto(new SlugName(c.Cartridge.Slug, c.Cartridge.Name), c.IsPrimary))
                            .ToList()))
                    .ToList(),
                // A credit whose person or organization is still a draft comes back with neither set; skip it.
                f.Designers
                    .Where(d => d.Person != null || d.Organization != null)
                    .OrderBy(d => d.Role)
                    .Select(d => new DesignerDto(
                        d.Role,
                        d.Person == null ? null : new SlugName(d.Person.Slug, d.Person.FullName),
                        d.Organization == null ? null : new SlugName(d.Organization.Slug, d.Organization.Name)))
                    .ToList(),
                f.Manufacturers
                    .OrderBy(m => m.Organization.Name)
                    .Select(m => new ManufacturerDto(
                        new SlugName(m.Organization.Slug, m.Organization.Name),
                        FuzzyDateDto.From(m.From),
                        FuzzyDateDto.From(m.To),
                        m.IsLicensed))
                    .ToList(),
                f.Adoptions
                    .OrderBy(a => a.Country.Name)
                    .Select(a => new AdoptionDto(
                        new SlugName(a.Country.Slug, a.Country.Name),
                        FuzzyDateDto.From(a.From),
                        FuzzyDateDto.From(a.To),
                        a.Note))
                    .ToList(),
                f.RelationsFrom
                    .OrderBy(r => r.Type).ThenBy(r => r.ToFirearm.Name)
                    .Select(r => new RelationDto(r.Type, new SlugName(r.ToFirearm.Slug, r.ToFirearm.Name), r.Note))
                    .ToList(),
                f.RelationsTo
                    .OrderBy(r => r.Type).ThenBy(r => r.FromFirearm.Name)
                    .Select(r => new RelationDto(r.Type, new SlugName(r.FromFirearm.Slug, r.FromFirearm.Name), r.Note))
                    .ToList(),
                Array.Empty<CitationDto>(),
                f.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => ImageDto.From(i)).ToList(),
                f.Videos.Where(v => v.Status == VideoStatus.Approved)
                    .OrderByDescending(v => v.PublishedAt).ThenBy(v => v.Id)
                    .Select(v => new VideoDto(v.YouTubeVideoId, v.Title, v.Channel, v.PublishedAt))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        if (dto is not null)
        {
            // Filter by id so the citations.firearm_id / variant_id indexes apply.
            var firearmId = await db.Firearms.Where(f => f.Slug == slug).Select(f => f.Id).SingleAsync(ct);
            var citations = await db.Citations
                .Where(c => c.FirearmId == firearmId || c.Variant!.FirearmId == firearmId)
                .OrderBy(c => c.Id)
                .Select(CitationProjection.ToDto)
                .ToListAsync(ct);
            return TypedResults.Ok(dto with { Citations = citations });
        }

        if (await SlugResults.FindCurrentSlugAsync(db, SluggedEntity.Firearm, slug, ct) is { } current)
        {
            return TypedResults.Redirect($"{Route}/{current}", permanent: true);
        }

        return SlugResults.NotFound("Firearm", slug);
    }

    /// <summary>
    /// The family tree of a firearm (GLOSSARY.md): its DerivedFrom and CopyOf neighbourhood, walked in both directions
    /// up to <paramref name="depth"/> steps, nearest first, and at most <see cref="MaxTreeNodes"/> firearms. Keeps a
    /// visited set because imported relations may contain cycles.
    /// </summary>
    private static async Task<Results<Ok<FamilyTreeDto>, RedirectHttpResult, ProblemHttpResult>> FamilyTree(
        string slug, int? depth, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var rootId = await db.Firearms.Where(f => f.Slug == slug).Select(f => (int?)f.Id).FirstOrDefaultAsync(ct);
        if (rootId is not { } root)
        {
            if (await SlugResults.FindCurrentSlugAsync(db, SluggedEntity.Firearm, slug, ct) is { } current)
            {
                return TypedResults.Redirect($"{Route}/{current}/family-tree", permanent: true);
            }

            return SlugResults.NotFound("Firearm", slug);
        }

        var visited = new HashSet<int> { root };
        var edges = new Dictionary<(int Parent, int Child), bool>(); // value: is a copy
        var frontier = new List<int> { root };
        var truncated = false;
        for (var step = 0; step < Math.Clamp(depth ?? 3, 1, MaxTreeDepth) && frontier.Count > 0 && !truncated; step++)
        {
            var current = frontier;
            var links = await db.FirearmRelations
                .Where(r => (r.Type == RelationType.DerivedFrom || r.Type == RelationType.CopyOf)
                    && (current.Contains(r.FromFirearmId) || current.Contains(r.ToFirearmId)))
                .Select(r => new { Parent = r.ToFirearmId, Child = r.FromFirearmId, IsCopy = r.Type == RelationType.CopyOf })
                .ToListAsync(ct);

            frontier = [];
            // A stable order, so which firearms make the cut does not change between requests.
            foreach (var link in links.OrderBy(l => l.Parent).ThenBy(l => l.Child))
            {
                foreach (var id in new[] { link.Parent, link.Child }.Where(id => !visited.Contains(id)))
                {
                    if (visited.Count >= MaxTreeNodes)
                    {
                        truncated = true;
                        break;
                    }

                    visited.Add(id);
                    frontier.Add(id);
                }

                // Derived wins over copy when a pair has both.
                edges[(link.Parent, link.Child)] = edges.GetValueOrDefault((link.Parent, link.Child), true) && link.IsCopy;
            }
        }

        var nodes = await db.Firearms
            .Where(f => visited.Contains(f.Id))
            .Select(f => new { f.Id, Node = new FamilyTreeNodeDto(f.Slug, f.Name, FuzzyDateDto.From(f.DesignDate), f.Id == root) })
            .ToDictionaryAsync(x => x.Id, x => x.Node, ct);

        return TypedResults.Ok(new FamilyTreeDto(
            [.. nodes.Values.OrderBy(n => n.DesignDate?.Year ?? int.MaxValue).ThenBy(n => n.Name)],
            // Relations already hide drafts; the key check keeps a filtered-out node from ever becoming a 500.
            [.. edges.Where(e => nodes.ContainsKey(e.Key.Parent) && nodes.ContainsKey(e.Key.Child))
                .Select(e => new FamilyTreeEdgeDto(nodes[e.Key.Parent].Slug, nodes[e.Key.Child].Slug, e.Value))
                .OrderBy(e => e.Source).ThenBy(e => e.Target)],
            truncated));
    }

    /// <summary>Base variants of up to four firearms, in the order requested.</summary>
    private static async Task<Results<Ok<IReadOnlyList<CompareItemDto>>, ProblemHttpResult>> Compare(
        string? slugs, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var requested = QueryValues.Csv(slugs);
        if (requested.Count is 0 or > MaxCompared)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid comparison",
                detail: $"slugs must name between 1 and {MaxCompared} firearms.");
        }

        var items = await db.Variants
            .Where(v => v.IsBaseVariant && requested.Contains(v.Firearm.Slug))
            .Select(v => new CompareItemDto(
                v.Firearm.Slug,
                v.Firearm.Name,
                v.Name,
                v.Chamberings.Where(c => c.IsPrimary).Select(c => new SlugName(c.Cartridge.Slug, c.Cartridge.Name)).FirstOrDefault(),
                SpecsDto.From(v)))
            .ToListAsync(ct);

        var missing = requested.Except(items.Select(i => i.Slug)).ToList();
        if (missing.Count > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Firearm not found",
                detail: $"No published firearm has the slug: {string.Join(", ", missing)}.");
        }

        return TypedResults.Ok<IReadOnlyList<CompareItemDto>>([.. items.OrderBy(i => requested.ToList().IndexOf(i.Slug))]);
    }
}
