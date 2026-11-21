using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Api.Features.Admin.Media;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Media;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Firearms;

/// <summary>
/// A firearm's own fields and its lifecycle (create, publish, unpublish, delete, unlock). Variants and the lists
/// hanging off a firearm are in <see cref="AdminFirearmPartsEndpoints"/>. Every change takes the firearm's
/// <c>version</c> and answers with the updated firearm.
/// </summary>
public static class AdminFirearmEndpoints
{
    private const string Route = "/api/admin/firearms";

    public static RouteGroupBuilder MapAdminFirearms(this RouteGroupBuilder admin)
    {
        var group = admin.MapGroup("/firearms").WithTags("Admin: firearms");
        group.MapGet("/", List).ProducesProblem(StatusCodes.Status400BadRequest);
        group.MapGet("/{id:int}", Detail).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", Create).Validate<FirearmInput>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{id:int}", Update).Validate<FirearmInput>().ProducesWriteProblems();
        group.MapPost("/{id:int}/publish", Publish).ProducesWriteProblems();
        group.MapPost("/{id:int}/unpublish", Unpublish).ProducesWriteProblems();
        group.MapDelete("/{id:int}", Delete).ProducesWriteProblems();
        group.MapDelete("/{id:int}/locks/{**key}", Unlock).ProducesWriteProblems();
        return group;
    }

    /// <summary>400 (bad reference or rule), 404 (unknown id) and 409 (stale version or conflicting state).</summary>
    internal static RouteHandlerBuilder ProducesWriteProblems(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<Results<Ok<PagedResult<AdminFirearmListItemDto>>, ProblemHttpResult>> List(
        PublishStatus? status, string? q, string? sort, int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var query = db.Firearms.IgnoreQueryFilters();
        if (status is { } s)
        {
            query = query.Where(f => f.Status == s);
        }

        var search = new FirearmQuery { Q = q };
        query = search.ApplySearch(query, await search.ResolveSearchModeAsync(query, ct));

        IQueryable<Firearm>? ordered = sort switch
        {
            null or "-updatedAt" => query.OrderByDescending(f => f.UpdatedAt).ThenBy(f => f.Slug),
            "name" => query.OrderBy(f => f.Name).ThenBy(f => f.Slug),
            _ => null,
        };
        if (ordered is null)
        {
            return AdminProblems.BadRequest("Unknown sort", "sort must be one of: -updatedAt, name.");
        }

        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? Paging.DefaultPageSize, 1, Paging.MaxPageSize);
        var total = await ordered.CountAsync(ct);
        var ids = await ordered.Skip((p - 1) * size).Take(size).Select(f => f.Id).ToListAsync(ct);

        // Load the page's firearms whole, so "missing for publish" comes from the one rule that publish enforces.
        var firearms = await db.AdminGraph().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var items = ids.Select(id => firearms[id])
            .Select(f => new AdminFirearmListItemDto(f.Id, f.Slug, f.Name, f.Status, f.UpdatedAt, f.MissingForPublish()))
            .ToList();

        return TypedResults.Ok(new PagedResult<AdminFirearmListItemDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> Detail(int id, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var firearm = await db.AdminGraph().FirstOrDefaultAsync(f => f.Id == id, ct);
        return firearm is null ? AdminProblems.NotFound("Firearm", id) : TypedResults.Ok(db.ToAdminDto(firearm));
    }

    /// <summary>Creates a draft firearm and its base variant. Every field given is locked, as an admin chose it.</summary>
    private static async Task<Results<Created<AdminFirearmDto>, ProblemHttpResult>> Create(
        FirearmInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var references = await ResolveReferencesAsync(db, input, ct);
        if (references.Problem is { } problem)
        {
            return problem;
        }

        if (input.Slug is { } wanted && await db.FirearmSlugTakenAsync(wanted, null, ct))
        {
            return AdminProblems.Conflict("Slug taken", $"Another firearm already uses the slug '{wanted}'.");
        }

        if (await db.WikidataIdTakenAsync(input.WikidataId, null, ct))
        {
            return AdminProblems.Conflict("Wikidata id taken", $"Another firearm already has {input.WikidataId}.");
        }

        var firearm = new Firearm
        {
            Slug = input.Slug ?? await db.UniqueFirearmSlugAsync(input.Name, ct),
            Name = "",
            Status = PublishStatus.Draft,
        };
        Apply(firearm, Blank, input, references);
        firearm.Variants.Add(new Variant { Slug = firearm.Slug, Name = firearm.Name, IsBaseVariant = true });
        await db.ClaimSlugAsync(SluggedEntity.Firearm, firearm.Slug, ct);
        db.Firearms.Add(firearm);
        if (await db.TrySaveAsync<Firearm>(null, ct) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Created($"{Route}/{firearm.Id}", await db.ReloadDtoAsync(firearm.Id, ct));
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> Update(
        int id, uint version, FirearmInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var references = await ResolveReferencesAsync(db, input, ct);
        if (references.Problem is { } bad)
        {
            return bad;
        }

        // A PUT without a slug keeps the current one.
        var slug = input.Slug ?? firearm.Slug;
        if (slug != firearm.Slug)
        {
            if (await db.FirearmSlugTakenAsync(slug, firearm.Id, ct))
            {
                return AdminProblems.Conflict("Slug taken", $"Another firearm already uses the slug '{slug}'.");
            }

            await db.RecordRenameAsync(SluggedEntity.Firearm, firearm.Slug, firearm.Id, ct);
            await db.ClaimSlugAsync(SluggedEntity.Firearm, slug, ct);
        }

        if (input.WikidataId != firearm.WikidataId && await db.WikidataIdTakenAsync(input.WikidataId, firearm.Id, ct))
        {
            return AdminProblems.Conflict("Wikidata id taken", $"Another firearm already has {input.WikidataId}.");
        }

        // Saving an unchanged form must not bump the version (and 409 other editors) or lock anything.
        if (!Apply(firearm, firearm.ToInput(), input with { Slug = slug }, references))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        return await db.SaveAndReloadAsync(firearm, ct);
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> Publish(int id, uint version, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (firearm!.MissingForPublish() is { Count: > 0 } missing)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Publish requirements not met",
                detail: $"Fill in: {string.Join(", ", missing)}.",
                extensions: new Dictionary<string, object?> { ["missingForPublish"] = missing });
        }

        firearm.Status = PublishStatus.Published;
        return await db.SaveAndReloadAsync(firearm, ct);
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> Unpublish(int id, uint version, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        firearm!.Status = PublishStatus.Draft;
        return await db.SaveAndReloadAsync(firearm, ct);
    }

    /// <summary>Drafts only: published firearms must be unpublished first. Its slug redirects go with it.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(int id, uint version, ArmoryWikiDbContext db, FileStore store, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (firearm!.Status == PublishStatus.Published)
        {
            return AdminProblems.Conflict("Firearm is published", "Unpublish it before deleting it.");
        }

        db.SlugRedirects.RemoveRange(await db.SlugRedirects.AsTracking()
            .Where(r => r.EntityType == SluggedEntity.Firearm && r.EntityId == id).ToListAsync(ct));
        var imageHashes = await db.Images.Where(i => i.FirearmId == id).Select(i => i.ContentHash).ToListAsync(ct);
        db.Firearms.Remove(firearm); // variants, credits, relations, adoptions, citations, images and videos cascade
        if (await db.TrySaveAsync<Firearm>(id, ct) is { } conflict)
        {
            return conflict;
        }

        await db.DeleteUnusedImageFilesAsync(store, imageHashes, ct);
        return TypedResults.NoContent();
    }

    /// <summary>Removes a lock without changing the value; the next import may overwrite the field again.</summary>
    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> Unlock(
        int id, string key, uint version, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!firearm!.LockedFields.Remove(key))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not locked", detail: $"'{key}' is not locked.");
        }

        return await db.SaveAndReloadAsync(firearm, ct);
    }

    // Field-by-field apply ---------------------------------------------------------------------------------------

    private static readonly FirearmInput Blank = new(
        null, "", [], null, null, null, null, null, null, null, null, null, null, null, null);

    private sealed record References(int? CountryId, int? MechanismId, ProblemHttpResult? Problem);

    private static async Task<References> ResolveReferencesAsync(ArmoryWikiDbContext db, FirearmInput input, CancellationToken ct)
    {
        int? countryId = null, mechanismId = null;
        if (input.Country is { } country
            && (countryId = await db.Countries.Where(c => c.Slug == country).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)) is null)
        {
            return new(null, null, AdminProblems.UnknownReference("country", [country]));
        }

        if (input.Mechanism is { } mechanism
            && (mechanismId = await db.OperatingMechanisms.Where(m => m.Slug == mechanism).Select(m => (int?)m.Id).FirstOrDefaultAsync(ct)) is null)
        {
            return new(null, null, AdminProblems.UnknownReference("mechanism", [mechanism]));
        }

        return new(countryId, mechanismId, null);
    }

    /// <summary>
    /// Copies every field that differs from <paramref name="before"/> and locks it under its input field name.
    /// Returns whether anything changed.
    /// </summary>
    private static bool Apply(Firearm f, FirearmInput before, FirearmInput input, References refs)
    {
        var changed = false;
        void Set<T>(string key, T old, T value, Action apply)
        {
            if (!EqualityComparer<T>.Default.Equals(old, value))
            {
                apply();
                f.Lock(key);
                changed = true;
            }
        }

        var alternativeNames = input.AlternativeNames ?? [];
        Set("slug", before.Slug, input.Slug, () => f.Slug = input.Slug!);
        Set("name", before.Name, input.Name, () => f.Name = input.Name);
        if (!(before.AlternativeNames ?? []).SequenceEqual(alternativeNames))
        {
            f.AlternativeNames = [.. alternativeNames];
            f.Lock("alternativeNames");
            changed = true;
        }

        Set("wikidataId", before.WikidataId, input.WikidataId, () => f.WikidataId = input.WikidataId);
        Set("country", before.Country, input.Country, () => f.CountryOfOriginId = refs.CountryId);
        Set("designDate", before.DesignDate, input.DesignDate, () => f.DesignDate = input.DesignDate.ToEntity());
        Set("productionStart", before.ProductionStart, input.ProductionStart, () => f.ProductionStart = input.ProductionStart.ToEntity());
        Set("productionEnd", before.ProductionEnd, input.ProductionEnd, () => f.ProductionEnd = input.ProductionEnd.ToEntity());
        Set("mechanism", before.Mechanism, input.Mechanism, () => f.OperatingMechanismId = refs.MechanismId);
        Set("feedSystem", before.FeedSystem, input.FeedSystem, () => f.FeedSystem = input.FeedSystem);
        Set("summary", before.Summary, input.Summary, () => f.Summary = input.Summary);
        Set("developmentHistory", before.DevelopmentHistory, input.DevelopmentHistory, () => f.DevelopmentHistory = input.DevelopmentHistory);
        Set("serviceHistory", before.ServiceHistory, input.ServiceHistory, () => f.ServiceHistory = input.ServiceHistory);
        Set("wikipediaTitle", before.WikipediaTitle, input.WikipediaTitle, () => f.WikipediaTitle = input.WikipediaTitle);
        Set("wikipediaRevisionId", before.WikipediaRevisionId, input.WikipediaRevisionId, () => f.WikipediaRevisionId = input.WikipediaRevisionId);
        return changed;
    }
}
