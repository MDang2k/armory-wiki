using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Resources;

/// <summary>
/// Admin view of one entity: <see cref="Details"/> is the same shape the PUT accepts. <see cref="Status"/>,
/// <see cref="LockedFields"/> and <see cref="MissingForPublish"/> are empty for entities without a lifecycle
/// (countries, tags, mechanisms).
/// </summary>
public sealed record AdminEntityDto<TInput>(
    int Id,
    uint Version,
    PublishStatus? Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> LockedFields,
    IReadOnlyList<string> MissingForPublish,
    TInput Details);

public sealed record AdminEntityListItemDto(
    int Id,
    string Slug,
    string Name,
    PublishStatus? Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> MissingForPublish);

/// <summary>Referenced entities resolved from an input's slugs, or the problem that stops the write.</summary>
internal sealed record Resolved<TRefs>(TRefs Refs, ProblemHttpResult? Problem);

/// <summary>
/// Everything that differs between admin resources; <see cref="AdminResource"/> supplies the shared lifecycle
/// (list, detail, create, update, publish, unpublish, delete, unlock) from it.
/// </summary>
internal sealed class ResourceSpec<TEntity, TInput, TRefs>
    where TEntity : Entity, ISlugged
    where TInput : class
{
    /// <summary>Singular display name, e.g. "Person".</summary>
    public required string Title { get; init; }
    public required SluggedEntity SlugType { get; init; }
    /// <summary>Renames write redirects only for entities with public detail pages.</summary>
    public required bool HasPublicPage { get; init; }
    public required Func<ArmoryWikiDbContext, IQueryable<TEntity>> Set { get; init; }
    /// <summary>The navigations <see cref="ToInput"/> reads.</summary>
    public Func<IQueryable<TEntity>, IQueryable<TEntity>> Include { get; init; } = q => q;
    public required Func<TEntity, string> NameOf { get; init; }
    public required Func<TEntity, TInput> ToInput { get; init; }
    public required TInput Blank { get; init; }
    public required Func<TInput, string?> SlugOf { get; init; }
    public required Func<TInput, string> InputName { get; init; }
    public required Func<TInput, string, TInput> WithSlug { get; init; }
    public Func<TInput, string?> WikidataIdOf { get; init; } = _ => null;
    public required Func<string, TEntity> New { get; init; }
    /// <summary>Looks up referenced slugs and checks rules that need the database (the entity is null on create).</summary>
    public required Func<ArmoryWikiDbContext, TInput, TEntity?, CancellationToken, Task<Resolved<TRefs>>> ResolveAsync { get; init; }
    /// <summary>Copies the input onto the entity through <see cref="Changes"/>, which locks what changed.</summary>
    public required Action<TEntity, TInput, TInput, TRefs, Changes> Apply { get; init; }
    /// <summary>Runs after <see cref="Apply"/> inside the save, e.g. rewriting descendant mechanism paths.</summary>
    public Func<ArmoryWikiDbContext, TEntity, CancellationToken, Task> BeforeSaveAsync { get; init; } = (_, _, _) => Task.CompletedTask;
    /// <summary>What still points at the entity, as "firearm ak-47"; deleting is refused while any remain.</summary>
    public required Func<ArmoryWikiDbContext, int, CancellationToken, Task<List<string>>> ReferencedByAsync { get; init; }
    /// <summary>Publish requirements (GLOSSARY.md). Only for entities with a lifecycle.</summary>
    public Func<TEntity, List<string>> MissingForPublish { get; init; } = _ => [];
}

internal static class AdminResource
{
    public static void MapResource<TEntity, TInput, TRefs>(this RouteGroupBuilder admin, string route, ResourceSpec<TEntity, TInput, TRefs> spec)
        where TEntity : Entity, ISlugged
        where TInput : class
    {
        var group = admin.MapGroup(route).WithTags($"Admin: {route}");
        var publishable = typeof(IPublishable).IsAssignableFrom(typeof(TEntity));

        group.MapGet("/", (PublishStatus? status, string? q, int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct) =>
            ListAsync(spec, status, q, page, pageSize, db, ct));
        group.MapGet("/{id:int}", (int id, ArmoryWikiDbContext db, CancellationToken ct) => DetailAsync(spec, id, db, ct))
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", (TInput input, ArmoryWikiDbContext db, CancellationToken ct) => CreateAsync(spec, route, input, db, ct))
            .Validate<TInput>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{id:int}", (int id, uint version, TInput input, ArmoryWikiDbContext db, CancellationToken ct) =>
            UpdateAsync(spec, id, version, input, db, ct)).Validate<TInput>().ProducesWriteProblems();
        group.MapDelete("/{id:int}", (int id, uint version, ArmoryWikiDbContext db, CancellationToken ct) =>
            DeleteAsync(spec, id, version, db, ct)).ProducesWriteProblems();

        if (publishable)
        {
            group.MapPost("/{id:int}/publish", (int id, uint version, ArmoryWikiDbContext db, CancellationToken ct) =>
                SetStatusAsync(spec, id, version, PublishStatus.Published, db, ct)).ProducesWriteProblems();
            group.MapPost("/{id:int}/unpublish", (int id, uint version, ArmoryWikiDbContext db, CancellationToken ct) =>
                SetStatusAsync(spec, id, version, PublishStatus.Draft, db, ct)).ProducesWriteProblems();
            group.MapDelete("/{id:int}/locks/{**key}", (int id, string key, uint version, ArmoryWikiDbContext db, CancellationToken ct) =>
                UnlockAsync(spec, id, key, version, db, ct)).ProducesWriteProblems();
        }
    }

    private static RouteHandlerBuilder ProducesWriteProblems(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static IQueryable<TEntity> Graph<TEntity, TInput, TRefs>(ArmoryWikiDbContext db, ResourceSpec<TEntity, TInput, TRefs> spec)
        where TEntity : Entity, ISlugged where TInput : class =>
        spec.Include(spec.Set(db).IgnoreQueryFilters().AsTracking());

    private static AdminEntityDto<TInput> ToDto<TEntity, TInput, TRefs>(ArmoryWikiDbContext db, ResourceSpec<TEntity, TInput, TRefs> spec, TEntity e)
        where TEntity : Entity, ISlugged where TInput : class =>
        new(e.Id, db.VersionOf(e), (e as IPublishable)?.Status, e.UpdatedAt,
            (e as IPublishable)?.LockedFields ?? [], spec.MissingForPublish(e), spec.ToInput(e));

    private static async Task<AdminEntityDto<TInput>> ReloadAsync<TEntity, TInput, TRefs>(
        ArmoryWikiDbContext db, ResourceSpec<TEntity, TInput, TRefs> spec, int id, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        db.ChangeTracker.Clear();
        return ToDto(db, spec, await Graph(db, spec).SingleAsync(e => e.Id == id, ct));
    }

    private static async Task<(TEntity Entity, ProblemHttpResult? Problem)> LoadForWriteAsync<TEntity, TInput, TRefs>(
        ArmoryWikiDbContext db, ResourceSpec<TEntity, TInput, TRefs> spec, int id, uint version, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var entity = await Graph(db, spec).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entity is null)
        {
            return (null!, AdminProblems.NotFound(spec.Title, id)); // callers return the problem first
        }

        var current = db.VersionOf(entity);
        return current == version ? (entity, null) : (null!, AdminProblems.VersionConflict(current));
    }

    private static async Task<Results<Ok<PagedResult<AdminEntityListItemDto>>, ProblemHttpResult>> ListAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, PublishStatus? status, string? q, int? page, int? pageSize,
        ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var query = spec.Set(db).IgnoreQueryFilters();
        if (status is { } s && typeof(IPublishable).IsAssignableFrom(typeof(TEntity)))
        {
            query = query.Where(e => EF.Property<PublishStatus>(e, nameof(IPublishable.Status)) == s);
        }

        // Search by the slug form of the text, which works for every entity whatever its name field is called.
        if (!string.IsNullOrWhiteSpace(q) && Slug.FromName(q) is { Length: > 0 } term)
        {
            query = query.Where(e => e.Slug.Contains(term));
        }

        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? Paging.DefaultPageSize, 1, Paging.MaxPageSize);
        var ordered = query.OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Slug);
        var total = await ordered.CountAsync(ct);
        var ids = await ordered.Skip((p - 1) * size).Take(size).Select(e => e.Id).ToListAsync(ct);
        var entities = await Graph(db, spec).Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

        var items = ids.Select(id => entities[id]).Select(e => new AdminEntityListItemDto(
            e.Id, e.Slug, spec.NameOf(e), (e as IPublishable)?.Status, e.UpdatedAt, spec.MissingForPublish(e))).ToList();
        return TypedResults.Ok(new PagedResult<AdminEntityListItemDto>(items, p, size, total));
    }

    private static async Task<Results<Ok<AdminEntityDto<TInput>>, ProblemHttpResult>> DetailAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, int id, ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class =>
        await Graph(db, spec).FirstOrDefaultAsync(e => e.Id == id, ct) is { } entity
            ? TypedResults.Ok(ToDto(db, spec, entity))
            : AdminProblems.NotFound(spec.Title, id);

    /// <summary>Creates a draft (for entities with a lifecycle). Every field given is locked, as an admin chose it.</summary>
    private static async Task<Results<Created<AdminEntityDto<TInput>>, ProblemHttpResult>> CreateAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, string route, TInput input, ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var resolved = await spec.ResolveAsync(db, input, null, ct);
        if (resolved.Problem is { } problem)
        {
            return problem;
        }

        if (spec.SlugOf(input) is { } wanted && await spec.Set(db).SlugTakenAsync(wanted, null, ct))
        {
            return AdminProblems.Conflict("Slug taken", $"Another {spec.Title.ToLowerInvariant()} already uses the slug '{wanted}'.");
        }

        if (await WikidataIdTakenAsync(db, spec, spec.WikidataIdOf(input), null, ct) is { } taken)
        {
            return taken;
        }

        var slug = spec.SlugOf(input) ?? await spec.Set(db).UniqueSlugAsync(spec.InputName(input), spec.SlugType.ToString().ToLowerInvariant(), ct);
        var entity = spec.New(slug);
        spec.Apply(entity, spec.Blank, spec.WithSlug(input, slug), resolved.Refs, new Changes((entity as IPublishable)?.LockedFields));
        await spec.BeforeSaveAsync(db, entity, ct);
        await db.ClaimSlugAsync(spec.SlugType, slug, ct);
        db.Add(entity);
        if (await db.TrySaveAsync<TEntity>(null, ct) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Created($"/api/admin/{route}/{entity.Id}", await ReloadAsync(db, spec, entity.Id, ct));
    }

    private static async Task<Results<Ok<AdminEntityDto<TInput>>, ProblemHttpResult>> UpdateAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, int id, uint version, TInput input, ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var (entity, problem) = await LoadForWriteAsync(db, spec, id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var resolved = await spec.ResolveAsync(db, input, entity, ct);
        if (resolved.Problem is { } bad)
        {
            return bad;
        }

        // A PUT without a slug keeps the current one.
        var slug = spec.SlugOf(input) ?? entity.Slug;
        if (slug != entity.Slug)
        {
            if (await spec.Set(db).SlugTakenAsync(slug, entity.Id, ct))
            {
                return AdminProblems.Conflict("Slug taken", $"Another {spec.Title.ToLowerInvariant()} already uses the slug '{slug}'.");
            }

            if (spec.HasPublicPage)
            {
                await db.RecordRenameAsync(spec.SlugType, entity.Slug, entity.Id, ct);
            }

            await db.ClaimSlugAsync(spec.SlugType, slug, ct);
        }

        var before = spec.ToInput(entity);
        if (spec.WikidataIdOf(input) is { } qid && qid != spec.WikidataIdOf(before)
            && await WikidataIdTakenAsync(db, spec, qid, entity.Id, ct) is { } taken)
        {
            return taken;
        }

        var changes = new Changes((entity as IPublishable)?.LockedFields);
        spec.Apply(entity, before, spec.WithSlug(input, slug), resolved.Refs, changes);
        if (!changes.Any)
        {
            return TypedResults.Ok(ToDto(db, spec, entity)); // unchanged: no new version, no locks
        }

        await spec.BeforeSaveAsync(db, entity, ct);
        return await db.TrySaveAsync<TEntity>(id, ct) is { } conflict
            ? conflict
            : TypedResults.Ok(await ReloadAsync(db, spec, id, ct));
    }

    private static async Task<Results<Ok<AdminEntityDto<TInput>>, ProblemHttpResult>> SetStatusAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, int id, uint version, PublishStatus status, ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var (entity, problem) = await LoadForWriteAsync(db, spec, id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (status == PublishStatus.Published && spec.MissingForPublish(entity) is { Count: > 0 } missing)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Publish requirements not met",
                detail: $"Fill in: {string.Join(", ", missing)}.",
                extensions: new Dictionary<string, object?> { ["missingForPublish"] = missing });
        }

        ((IPublishable)entity).Status = status;
        return await db.TrySaveAsync<TEntity>(id, ct) is { } conflict
            ? conflict
            : TypedResults.Ok(await ReloadAsync(db, spec, id, ct));
    }

    /// <summary>Refused while anything still points at the entity. Its slug redirects go with it.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, int id, uint version, ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var (entity, problem) = await LoadForWriteAsync(db, spec, id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await spec.ReferencedByAsync(db, id, ct) is { Count: > 0 } references)
        {
            return AdminProblems.Conflict($"{spec.Title} is in use", $"Still referenced by: {string.Join(", ", references)}.");
        }

        await db.RemoveRedirectsAsync(spec.SlugType, id, ct);
        db.Remove(entity);
        return await db.TrySaveAsync<TEntity>(id, ct) is { } conflict ? conflict : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<AdminEntityDto<TInput>>, ProblemHttpResult>> UnlockAsync<TEntity, TInput, TRefs>(
        ResourceSpec<TEntity, TInput, TRefs> spec, int id, string key, uint version, ArmoryWikiDbContext db, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class
    {
        var (entity, problem) = await LoadForWriteAsync(db, spec, id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (!((IPublishable)entity).LockedFields.Remove(key))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not locked", detail: $"'{key}' is not locked.");
        }

        return await db.TrySaveAsync<TEntity>(id, ct) is { } conflict
            ? conflict
            : TypedResults.Ok(await ReloadAsync(db, spec, id, ct));
    }

    private static async Task<ProblemHttpResult?> WikidataIdTakenAsync<TEntity, TInput, TRefs>(
        ArmoryWikiDbContext db, ResourceSpec<TEntity, TInput, TRefs> spec, string? qid, int? exceptId, CancellationToken ct)
        where TEntity : Entity, ISlugged where TInput : class =>
        qid is not null && await spec.Set(db).IgnoreQueryFilters()
            .AnyAsync(e => EF.Property<string?>(e, "WikidataId") == qid && e.Id != exceptId, ct)
            ? AdminProblems.Conflict("Wikidata id taken", $"Another {spec.Title.ToLowerInvariant()} already has {qid}.")
            : null;
}
