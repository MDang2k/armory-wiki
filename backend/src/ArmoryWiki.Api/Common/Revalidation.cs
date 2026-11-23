using System.Net.Http.Json;
using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArmoryWiki.Api.Common;

/// <summary>
/// Cache tags of the frontend pages a request's saved changes affect (PLAN.md §5): the entity's own tag
/// (<c>firearm:&lt;slug&gt;</c>, the old slug's too on a rename) and its type tag (<c>firearms</c>, <c>people</c>, …),
/// which every page embedding that type carries.
/// </summary>
public sealed class PendingRevalidation
{
    public HashSet<string> Tags { get; } = [];
}

/// <summary>Records the tags of each successful save into <see cref="PendingRevalidation"/>. Failed saves add nothing.</summary>
public sealed class RevalidationInterceptor(PendingRevalidation pending) : SaveChangesInterceptor
{
    private readonly List<string> _saving = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Keep();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Keep();
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _saving.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving.Clear();
        return Task.CompletedTask;
    }

    private void Collect(DbContext? db)
    {
        _saving.Clear();
        foreach (var entry in db?.ChangeTracker.Entries() ?? [])
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                _saving.AddRange(TagsFor(entry));
            }
        }
    }

    private void Keep()
    {
        pending.Tags.UnionWith(_saving);
        _saving.Clear();
    }

    /// <summary>The tags a change to this entry makes stale.</summary>
    public static IEnumerable<string> TagsFor(EntityEntry entry)
    {
        var entity = entry.Entity;
        var type = entity switch
        {
            Firearm or Variant or VariantCartridge or FirearmTag or FirearmDesigner or FirearmManufacturer
                or FirearmRelation or Adoption or Video => "firearms",
            Person => "people",
            Organization => "organizations",
            Cartridge => "cartridges",
            OperatingMechanism => "mechanisms",
            Country or Tag or WikidataClassTagMap => "reference",
            Image i => OwnerType(i.FirearmId, null, i.PersonId, i.OrganizationId, i.CartridgeId),
            Citation c => OwnerType(c.FirearmId, c.VariantId, c.PersonId, c.OrganizationId, c.CartridgeId),
            Source => "all", // shown in the citations of any page
            _ => null,
        };
        if (type is null)
        {
            yield break;
        }

        yield return type;
        var prefix = entity switch
        {
            Firearm => "firearm",
            Person => "person",
            Organization => "organization",
            Cartridge => "cartridge",
            OperatingMechanism => "mechanism",
            _ => null, // countries and tags have no page of their own
        };
        if (prefix is not null && entity is ISlugged slugged)
        {
            yield return $"{prefix}:{slugged.Slug}";
            if (entry.State == EntityState.Modified && entry.Property(nameof(ISlugged.Slug)).OriginalValue is string old && old != slugged.Slug)
            {
                yield return $"{prefix}:{old}";
            }
        }
    }

    private static string OwnerType(int? firearm, int? variant, int? person, int? organization, int? cartridge) =>
        firearm is not null || variant is not null ? "firearms"
        : person is not null ? "people"
        : organization is not null ? "organizations"
        : cartridge is not null ? "cartridges"
        : "all";
}

/// <summary>
/// Asks the frontend to expire cache tags (<c>POST /api/revalidate</c>). Unconfigured means no call; a failure is
/// logged and never fails the write that triggered it.
/// </summary>
public sealed class FrontendRevalidator(HttpClient http, IConfiguration configuration, ILogger<FrontendRevalidator> logger)
{
    public const string SecretHeader = "x-revalidate-secret";

    /// <param name="tags">The cache tags to expire.</param>
    /// <param name="ct">
    /// Host shutdown or the end of a CLI run, not an HTTP request's token: an admin closing the tab after saving must
    /// not leave the pages stale.
    /// </param>
    public async Task SendAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        var url = configuration["Frontend:RevalidateUrl"];
        var secret = configuration["Frontend:RevalidateSecret"];
        if (tags.Count == 0 || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { tags }) };
            request.Headers.Add(SecretHeader, secret);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Revalidating {Tags} answered HTTP {Status}", tags, (int)response.StatusCode);
            }
        }
        catch (Exception e)
        {
            // Anything (frontend down, a malformed URL in configuration, shutdown): the write it follows is committed.
            logger.LogWarning(e, "Revalidating {Tags} failed", tags);
        }
    }
}

public static class RevalidationExtensions
{
    /// <summary>
    /// After each successful admin request, sends the tags its saved changes made stale. A request answering an error
    /// sends nothing, even when it saved before failing: its transaction rolled back.
    /// </summary>
    public static RouteGroupBuilder RevalidatesFrontend(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            var result = await next(context);
            var services = context.HttpContext.RequestServices;
            var pending = services.GetRequiredService<PendingRevalidation>();
            if (pending.Tags.Count > 0 && StatusOf(result) is null or < 400)
            {
                var stopping = services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
                await services.GetRequiredService<FrontendRevalidator>().SendAsync([.. pending.Tags], stopping);
            }

            pending.Tags.Clear();
            return result;
        });

    /// <summary>The status a typed result will answer with, looking inside <c>Results&lt;…&gt;</c> unions.</summary>
    private static int? StatusOf(object? result) => result switch
    {
        INestedHttpResult nested => StatusOf(nested.Result),
        IStatusCodeHttpResult typed => typed.StatusCode,
        _ => null,
    };
}
