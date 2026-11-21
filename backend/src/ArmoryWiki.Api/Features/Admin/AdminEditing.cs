using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArmoryWiki.Api.Features.Admin;

/// <summary>The ProblemDetails answers every admin endpoint shares.</summary>
public static class AdminProblems
{
    public static ProblemHttpResult NotFound(string what, int id) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"{what} not found", detail: $"No {what.ToLowerInvariant()} has the id {id}.");

    public static ProblemHttpResult VersionConflict(uint? current) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Version conflict",
            detail: "This changed since you loaded it. Reload, reapply your change and send the new version.",
            extensions: new Dictionary<string, object?> { ["currentVersion"] = current });

    public static ProblemHttpResult Conflict(string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail);

    public static ProblemHttpResult BadRequest(string title, string detail) =>
        TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: title, detail: detail);

    public static ProblemHttpResult UnknownReference(string kind, IEnumerable<string> slugs) =>
        BadRequest($"Unknown {kind}", $"No {kind} has the slug: {string.Join(", ", slugs)}.");
}

/// <summary>Records which fields an admin write changed, and locks them when the entity has locks.</summary>
internal sealed class Changes(List<string>? locks)
{
    public bool Any { get; private set; }

    public void Set<T>(string key, T old, T value, Action apply)
    {
        if (!EqualityComparer<T>.Default.Equals(old, value))
        {
            apply();
            Mark(key);
        }
    }

    public void Mark(string key)
    {
        Any = true;
        if (locks is not null && !locks.Contains(key))
        {
            locks.Add(key);
        }
    }
}

/// <summary>Mechanics every admin write shares: versions (xmin), safe saves and slugs.</summary>
internal static class AdminEditing
{
    public static uint VersionOf(this ArmoryWikiDbContext db, object entity) =>
        (uint)db.Entry(entity).Property(ArmoryWikiDbContext.Version).CurrentValue!;

    /// <summary>
    /// Saves, turning a lost race into 409s instead of 500s: a concurrent change (with the version now current),
    /// a unique value (slug, Wikidata id) someone else took, or a reference removed in the meantime.
    /// <paramref name="id"/> is the row being changed, or null for a new one.
    /// </summary>
    public static async Task<ProblemHttpResult?> TrySaveAsync<TEntity>(this ArmoryWikiDbContext db, int? id, CancellationToken ct)
        where TEntity : class
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await db.Set<TEntity>().IgnoreQueryFilters()
                .Where(e => EF.Property<int>(e, "Id") == id)
                .Select(e => (uint?)EF.Property<uint>(e, ArmoryWikiDbContext.Version))
                .FirstOrDefaultAsync(ct);
            return AdminProblems.VersionConflict(current);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            return AdminProblems.Conflict("Already in use", $"Another record already uses this value ({unique.ConstraintName}).");
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } reference)
        {
            return AdminProblems.Conflict("Reference changed", $"Something this refers to was removed, or something new refers to it ({reference.ConstraintName}). Reload and try again.");
        }
    }

    public static Task<bool> SlugTakenAsync<TEntity>(this IQueryable<TEntity> set, string slug, int? exceptId, CancellationToken ct)
        where TEntity : class =>
        set.IgnoreQueryFilters().AnyAsync(e => EF.Property<string>(e, "Slug") == slug && EF.Property<int>(e, "Id") != exceptId, ct);

    /// <summary>The name's slug, or the first free "-2", "-3", … variant of it.</summary>
    public static async Task<string> UniqueSlugAsync<TEntity>(
        this IQueryable<TEntity> set, string name, string fallback, CancellationToken ct, IReadOnlySet<string>? reserved = null)
        where TEntity : class
    {
        var root = Slug.FromName(name) is { Length: > 0 } s ? s : fallback;
        var candidate = root;
        for (var n = 2; (reserved?.Contains(candidate) ?? false) || await set.SlugTakenAsync(candidate, null, ct); n++)
        {
            candidate = $"{root}-{n}";
        }

        return candidate;
    }

    /// <summary>A live slug always beats a redirect: taking a slug removes any redirect that used it.</summary>
    public static async Task ClaimSlugAsync(this ArmoryWikiDbContext db, SluggedEntity type, string slug, CancellationToken ct) =>
        db.SlugRedirects.RemoveRange(await db.SlugRedirects.AsTracking().Where(r => r.EntityType == type && r.OldSlug == slug).ToListAsync(ct));

    /// <summary>Points the old slug at the entity, so old links answer 301.</summary>
    public static async Task RecordRenameAsync(this ArmoryWikiDbContext db, SluggedEntity type, string oldSlug, int id, CancellationToken ct)
    {
        var existing = await db.SlugRedirects.AsTracking().FirstOrDefaultAsync(r => r.EntityType == type && r.OldSlug == oldSlug, ct);
        if (existing is null)
        {
            db.SlugRedirects.Add(new SlugRedirect { EntityType = type, OldSlug = oldSlug, EntityId = id });
        }
        else
        {
            existing.EntityId = id;
        }
    }

    public static async Task RemoveRedirectsAsync(this ArmoryWikiDbContext db, SluggedEntity type, int id, CancellationToken ct) =>
        db.SlugRedirects.RemoveRange(await db.SlugRedirects.AsTracking().Where(r => r.EntityType == type && r.EntityId == id).ToListAsync(ct));
}
