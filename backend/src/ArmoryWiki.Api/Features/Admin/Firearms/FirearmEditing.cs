using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Firearms;

/// <summary>Shared mechanics of firearm admin writes: loading, versions, locks, slugs and derivation loops.</summary>
internal static class FirearmEditing
{
    /// <summary>The whole firearm, drafts and draft references included, tracked so its version (xmin) is readable.</summary>
    public static IQueryable<Firearm> AdminGraph(this ArmoryWikiDbContext db) =>
        db.Firearms.IgnoreQueryFilters().AsTracking().AsSplitQuery()
            .Include(f => f.CountryOfOrigin)
            .Include(f => f.OperatingMechanism)
            .Include(f => f.Tags)
            .Include(f => f.Variants).ThenInclude(v => v.Chamberings).ThenInclude(c => c.Cartridge)
            .Include(f => f.Designers).ThenInclude(d => d.Person)
            .Include(f => f.Designers).ThenInclude(d => d.Organization)
            .Include(f => f.Manufacturers).ThenInclude(m => m.Organization)
            .Include(f => f.RelationsFrom).ThenInclude(r => r.ToFirearm)
            .Include(f => f.Adoptions).ThenInclude(a => a.Country);

    // Loading and saving -----------------------------------------------------------------------------------------

    /// <summary>Loads the firearm for a change, or the 404/409 that stops it.</summary>
    public static async Task<(Firearm Firearm, ProblemHttpResult? Problem)> LoadForWriteAsync(
        this ArmoryWikiDbContext db, int id, uint version, CancellationToken ct)
    {
        var firearm = await db.AdminGraph().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (firearm is null)
        {
            return (null!, AdminProblems.NotFound("Firearm", id)); // callers return the problem before touching the firearm
        }

        var current = db.VersionOf(firearm);
        return current == version ? (firearm, null) : (null!, AdminProblems.VersionConflict(current));
    }

    /// <summary>
    /// Saves, touching the firearm row so its version changes even when only child rows did. The UPDATE carries
    /// the loaded version, so a concurrent change in between still ends in a 409.
    /// </summary>
    public static Task<ProblemHttpResult?> SaveFirearmAsync(this ArmoryWikiDbContext db, Firearm firearm, CancellationToken ct)
    {
        db.Entry(firearm).Property(f => f.UpdatedAt).IsModified = true;
        return db.TrySaveAsync<Firearm>(firearm.Id, ct);
    }

    /// <summary>Saves the change, then answers with the freshly loaded firearm (new version included).</summary>
    public static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> SaveAndReloadAsync(
        this ArmoryWikiDbContext db, Firearm firearm, CancellationToken ct) =>
        await db.SaveFirearmAsync(firearm, ct) is { } problem
            ? problem
            : TypedResults.Ok(await db.ReloadDtoAsync(firearm.Id, ct));

    public static Task<bool> WikidataIdTakenAsync(this ArmoryWikiDbContext db, string? qid, int? exceptId, CancellationToken ct) =>
        qid is null
            ? Task.FromResult(false)
            : db.Firearms.IgnoreQueryFilters().AnyAsync(f => f.WikidataId == qid && f.Id != exceptId, ct);

    public static async Task<AdminFirearmDto> ReloadDtoAsync(this ArmoryWikiDbContext db, int id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var firearm = await db.AdminGraph().SingleAsync(f => f.Id == id, ct);
        return db.ToAdminDto(firearm);
    }

    public static void Lock(this Firearm firearm, string key)
    {
        if (!firearm.LockedFields.Contains(key))
        {
            firearm.LockedFields.Add(key);
        }
    }

    // Slugs ----------------------------------------------------------------------------------------------------

    public static Task<bool> FirearmSlugTakenAsync(this ArmoryWikiDbContext db, string slug, int? exceptId, CancellationToken ct) =>
        db.Firearms.IgnoreQueryFilters().AnyAsync(f => f.Slug == slug && f.Id != exceptId, ct);

    public static Task<string> UniqueFirearmSlugAsync(this ArmoryWikiDbContext db, string name, CancellationToken ct) =>
        db.Firearms.UniqueSlugAsync(name, "firearm", ct, Slug.ReservedFirearmSlugs);

    // Derivation loops -------------------------------------------------------------------------------------------

    /// <summary>
    /// Derivation never loops (GLOSSARY.md). Returns "A → B → A" when making <paramref name="firearmId"/> derived from
    /// (or a copy of) <paramref name="newParents"/> would close a loop, else null.
    /// </summary>
    // ponytail: loads every derivation edge; fine for thousands of relations, use a recursive CTE beyond that.
    public static async Task<string?> FindDerivationLoopAsync(
        this ArmoryWikiDbContext db, int firearmId, IReadOnlyCollection<int> newParents, CancellationToken ct)
    {
        if (newParents.Count == 0)
        {
            return null;
        }

        var edges = await db.FirearmRelations.IgnoreQueryFilters()
            .Where(r => (r.Type == RelationType.DerivedFrom || r.Type == RelationType.CopyOf) && r.FromFirearmId != firearmId)
            .Select(r => new { Child = r.FromFirearmId, Parent = r.ToFirearmId })
            .ToListAsync(ct);
        var parentsOf = edges.ToLookup(e => e.Child, e => e.Parent);

        foreach (var start in newParents)
        {
            // Walk up from the new parent. Reaching this firearm again means: firearm → start → … → firearm.
            var previous = new Dictionary<int, int> { [start] = firearmId };
            var queue = new Queue<int>([start]);
            while (queue.TryDequeue(out var current))
            {
                foreach (var parent in parentsOf[current])
                {
                    if (parent == firearmId)
                    {
                        var chain = new List<int> { current };
                        while (chain[^1] != start)
                        {
                            chain.Add(previous[chain[^1]]);
                        }

                        chain.Reverse();
                        List<int> path = [firearmId, .. chain, firearmId];
                        var names = await db.Firearms.IgnoreQueryFilters()
                            .Where(f => path.Contains(f.Id))
                            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);
                        return string.Join(" → ", path.Select(id => names[id]));
                    }

                    if (previous.TryAdd(parent, current))
                    {
                        queue.Enqueue(parent);
                    }
                }
            }
        }

        return null;
    }

    // Mapping ----------------------------------------------------------------------------------------------------

    public static FirearmInput ToInput(this Firearm f) => new(
        f.Slug,
        f.Name,
        f.AlternativeNames,
        f.WikidataId,
        f.CountryOfOrigin?.Slug,
        FuzzyDateDto.From(f.DesignDate),
        FuzzyDateDto.From(f.ProductionStart),
        FuzzyDateDto.From(f.ProductionEnd),
        f.OperatingMechanism?.Slug,
        f.FeedSystem,
        f.Summary,
        f.DevelopmentHistory,
        f.ServiceHistory,
        f.WikipediaTitle,
        f.WikipediaRevisionId);

    public static VariantInput ToInput(this Variant v) => new(
        v.Slug,
        v.Name,
        FuzzyDateDto.From(v.IntroducedDate),
        v.WeightEmptyG,
        v.OverallLengthMm,
        v.OverallLengthFoldedMm,
        v.BarrelLengthMm,
        v.RateOfFireMinRpm,
        v.RateOfFireMaxRpm,
        v.MuzzleVelocityMps,
        v.EffectiveRangeM,
        v.MagazineCapacityStandard,
        Enum.GetValues<FiringModes>().Where(m => m != FiringModes.None && v.FiringModes.HasFlag(m)).ToList(),
        v.ExtraSpecs);

    /// <summary>Unmet publish requirements (GLOSSARY.md), named after the input fields an admin must fill.</summary>
    public static List<string> MissingForPublish(this Firearm f)
    {
        var missing = new List<string>();
        if (f.CountryOfOriginId is null)
        {
            missing.Add("country");
        }

        if (f.DesignDate is null)
        {
            missing.Add("designDate");
        }

        if (f.OperatingMechanismId is null)
        {
            missing.Add("mechanism");
        }

        var primary = f.Variants.FirstOrDefault(v => v.IsBaseVariant)?.Chamberings.FirstOrDefault(c => c.IsPrimary);
        if (primary is null)
        {
            missing.Add("primaryCartridge");
        }
        else if (primary.Cartridge.Status != PublishStatus.Published)
        {
            missing.Add("primaryCartridgePublished");
        }

        return missing;
    }

    public static AdminFirearmDto ToAdminDto(this ArmoryWikiDbContext db, Firearm f) => new(
        f.Id,
        db.VersionOf(f),
        f.Status,
        f.UpdatedAt,
        f.LockedFields,
        f.MissingForPublish(),
        f.ToInput(),
        [.. f.Tags.Select(t => t.Slug).Order()],
        [.. f.Variants.OrderByDescending(v => v.IsBaseVariant).ThenBy(v => v.Name).Select(v => new AdminVariantDto(
            v.Id,
            v.IsBaseVariant,
            v.ToInput(),
            [.. v.Chamberings.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Cartridge.Slug).Select(c => new ChamberingInput(c.Cartridge.Slug, c.IsPrimary))]))],
        [.. f.Designers.OrderBy(d => d.Role).Select(d => new DesignerInput(d.Person?.Slug, d.Organization?.Slug, d.Role))],
        [.. f.Manufacturers.OrderBy(m => m.Organization.Slug).Select(m => new ManufacturerInput(m.Organization.Slug, FuzzyDateDto.From(m.From), FuzzyDateDto.From(m.To), m.IsLicensed))],
        [.. f.RelationsFrom.OrderBy(r => r.Type).ThenBy(r => r.ToFirearm.Slug).Select(r => new RelationInput(r.Type, r.ToFirearm.Slug, r.Note))],
        [.. f.Adoptions.OrderBy(a => a.Country.Slug).Select(a => new AdoptionInput(a.Country.Slug, FuzzyDateDto.From(a.From), FuzzyDateDto.From(a.To), a.Note))]);
}
