using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Firearms;

/// <summary>
/// Variants, and the lists hanging off a firearm. Lists are replaced whole: send the list as it should be.
/// A change locks the list (or the variant field) so imports leave it alone.
/// </summary>
public static class AdminFirearmPartsEndpoints
{
    public static void MapAdminFirearmParts(this RouteGroupBuilder firearms)
    {
        firearms.MapPost("/{id:int}/variants", AddVariant).Validate<VariantInput>().ProducesWriteProblems();
        firearms.MapPut("/{id:int}/variants/{variantId:int}", UpdateVariant).Validate<VariantInput>().ProducesWriteProblems();
        firearms.MapDelete("/{id:int}/variants/{variantId:int}", DeleteVariant).ProducesWriteProblems();
        firearms.MapPost("/{id:int}/variants/{variantId:int}/make-base", MakeBase).ProducesWriteProblems();
        firearms.MapPut("/{id:int}/variants/{variantId:int}/chamberings", ReplaceChamberings).Validate<ChamberingsInput>().ProducesWriteProblems();
        firearms.MapPut("/{id:int}/tags", ReplaceTags).Validate<TagsInput>().ProducesWriteProblems();
        firearms.MapPut("/{id:int}/designers", ReplaceDesigners).Validate<DesignersInput>().ProducesWriteProblems();
        firearms.MapPut("/{id:int}/manufacturers", ReplaceManufacturers).Validate<ManufacturersInput>().ProducesWriteProblems();
        firearms.MapPut("/{id:int}/relations", ReplaceRelations).Validate<RelationsInput>().ProducesWriteProblems();
        firearms.MapPut("/{id:int}/adoptions", ReplaceAdoptions).Validate<AdoptionsInput>().ProducesWriteProblems();
    }

    private static ProblemHttpResult VariantNotFound(int variantId) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Variant not found", detail: $"This firearm has no variant {variantId}.");

    // Variants -------------------------------------------------------------------------------------------------

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> AddVariant(
        int id, uint version, VariantInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var slug = input.Slug ?? UniqueVariantSlug(firearm!, input.Name);
        if (firearm!.Variants.Any(v => v.Slug == slug))
        {
            return FirearmEditing.Conflict("Slug taken", $"This firearm already has a variant '{slug}'.");
        }

        // Two saves (lock keys need the variant id, which exists only after the first) in one transaction.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var variant = new Variant { Slug = slug, Name = "" };
        firearm.Variants.Add(variant);
        if (await db.SaveFirearmAsync(firearm, ct) is { } conflict)
        {
            return conflict;
        }

        Apply(firearm, variant, BlankVariant, input with { Slug = slug });
        if (await db.SaveFirearmAsync(firearm, ct) is { } again)
        {
            return again;
        }

        await tx.CommitAsync(ct);
        return TypedResults.Ok(await db.ReloadDtoAsync(firearm.Id, ct));
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> UpdateVariant(
        int id, int variantId, uint version, VariantInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (firearm!.Variants.FirstOrDefault(v => v.Id == variantId) is not { } variant)
        {
            return VariantNotFound(variantId);
        }

        var slug = input.Slug ?? variant.Slug;
        if (firearm.Variants.Any(v => v.Slug == slug && v.Id != variantId))
        {
            return FirearmEditing.Conflict("Slug taken", $"This firearm already has a variant '{slug}'.");
        }

        if (!Apply(firearm, variant, variant.ToInput(), input with { Slug = slug }))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm)); // unchanged: no new version, no locks
        }

        return await db.SaveAndReloadAsync(firearm, ct);
    }

    /// <summary>The base variant cannot be deleted; promote another variant first.</summary>
    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> DeleteVariant(
        int id, int variantId, uint version, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (firearm!.Variants.FirstOrDefault(v => v.Id == variantId) is not { } variant)
        {
            return VariantNotFound(variantId);
        }

        if (variant.IsBaseVariant)
        {
            return FirearmEditing.Conflict("Base variant", "The base variant cannot be deleted. Make another variant the base first.");
        }

        db.Variants.Remove(variant);
        firearm.LockedFields.RemoveAll(k => k.StartsWith($"variants/{variantId}/", StringComparison.Ordinal) || k == $"chamberings/{variantId}");
        return await db.SaveAndReloadAsync(firearm, ct);
    }

    /// <summary>Moves the base flag in two steps inside one transaction, so the one-base index never sees two.</summary>
    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> MakeBase(
        int id, int variantId, uint version, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (firearm!.Variants.FirstOrDefault(v => v.Id == variantId) is not { } target)
        {
            return VariantNotFound(variantId);
        }

        if (target.IsBaseVariant)
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        firearm.Variants.Single(v => v.IsBaseVariant).IsBaseVariant = false;
        if (await db.SaveFirearmAsync(firearm, ct) is { } conflict)
        {
            return conflict;
        }

        target.IsBaseVariant = true;
        firearm.Lock("baseVariant");
        if (await db.SaveFirearmAsync(firearm, ct) is { } again)
        {
            return again;
        }

        await tx.CommitAsync(ct);
        return TypedResults.Ok(await db.ReloadDtoAsync(id, ct));
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceChamberings(
        int id, int variantId, uint version, ChamberingsInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (firearm!.Variants.FirstOrDefault(v => v.Id == variantId) is not { } variant)
        {
            return VariantNotFound(variantId);
        }

        var slugs = input.Chamberings.Select(c => c.Cartridge).ToList();
        var cartridges = await db.Cartridges.IgnoreQueryFilters().AsTracking().Where(c => slugs.Contains(c.Slug)).ToDictionaryAsync(c => c.Slug, ct);
        if (slugs.Except(cartridges.Keys).ToList() is { Count: > 0 } unknown)
        {
            return FirearmEditing.UnknownReference("cartridge", unknown);
        }

        var before = variant.Chamberings.Select(c => (c.Cartridge.Slug, c.IsPrimary)).ToHashSet();
        if (before.SetEquals(input.Chamberings.Select(c => (c.Cartridge, c.IsPrimary))))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        return await ReplaceAsync(db, firearm, variant.Chamberings,
            [.. input.Chamberings.Select(c => new VariantCartridge { Variant = variant, Cartridge = cartridges[c.Cartridge], IsPrimary = c.IsPrimary })],
            $"chamberings/{variantId}", ct);
    }

    // Lists ----------------------------------------------------------------------------------------------------

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceTags(
        int id, uint version, TagsInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var tags = await db.Tags.AsTracking().Where(t => input.Tags.Contains(t.Slug)).ToListAsync(ct);
        if (input.Tags.Except(tags.Select(t => t.Slug)).ToList() is { Count: > 0 } unknown)
        {
            return FirearmEditing.UnknownReference("tag", unknown);
        }

        if (firearm!.Tags.Select(t => t.Slug).ToHashSet().SetEquals(input.Tags))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        firearm.Tags.Clear();
        firearm.Tags.AddRange(tags);
        firearm.Lock("tags");
        return await db.SaveAndReloadAsync(firearm, ct);
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceDesigners(
        int id, uint version, DesignersInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var personSlugs = input.Designers.Where(d => d.Person is not null).Select(d => d.Person!).ToList();
        var orgSlugs = input.Designers.Where(d => d.Organization is not null).Select(d => d.Organization!).ToList();
        var people = await db.People.IgnoreQueryFilters().AsTracking().Where(p => personSlugs.Contains(p.Slug)).ToDictionaryAsync(p => p.Slug, ct);
        var orgs = await db.Organizations.IgnoreQueryFilters().AsTracking().Where(o => orgSlugs.Contains(o.Slug)).ToDictionaryAsync(o => o.Slug, ct);
        if (personSlugs.Except(people.Keys).ToList() is { Count: > 0 } unknownPeople)
        {
            return FirearmEditing.UnknownReference("person", unknownPeople);
        }

        if (orgSlugs.Except(orgs.Keys).ToList() is { Count: > 0 } unknownOrgs)
        {
            return FirearmEditing.UnknownReference("organization", unknownOrgs);
        }

        var before = firearm!.Designers.Select(d => (d.Person?.Slug, d.Organization?.Slug, d.Role)).ToHashSet();
        var after = input.Designers.Select(d => (d.Person, d.Organization, d.Role)).ToHashSet();
        if (after.Count != input.Designers.Count)
        {
            return FirearmEditing.BadRequest("Duplicate designer", "Each designer and role may appear once.");
        }

        if (before.SetEquals(after))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        return await ReplaceAsync(db, firearm, firearm.Designers,
            [.. input.Designers.Select(d => new FirearmDesigner
            {
                Firearm = firearm,
                Person = d.Person is null ? null : people[d.Person],
                Organization = d.Organization is null ? null : orgs[d.Organization],
                Role = d.Role,
            })],
            "designers", ct);
    }

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceManufacturers(
        int id, uint version, ManufacturersInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var slugs = input.Manufacturers.Select(m => m.Organization).Distinct().ToList();
        var orgs = await db.Organizations.IgnoreQueryFilters().AsTracking().Where(o => slugs.Contains(o.Slug)).ToDictionaryAsync(o => o.Slug, ct);
        if (slugs.Except(orgs.Keys).ToList() is { Count: > 0 } unknown)
        {
            return FirearmEditing.UnknownReference("organization", unknown);
        }

        var before = firearm!.Manufacturers.Select(m => new ManufacturerInput(m.Organization.Slug, FuzzyDateDto.From(m.From), FuzzyDateDto.From(m.To), m.IsLicensed));
        if (before.ToHashSet().SetEquals(input.Manufacturers))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        return await ReplaceAsync(db, firearm, firearm.Manufacturers,
            [.. input.Manufacturers.Select(m => new FirearmManufacturer
            {
                Firearm = firearm,
                Organization = orgs[m.Organization],
                From = m.From.ToEntity(),
                To = m.To.ToEntity(),
                IsLicensed = m.IsLicensed,
            })],
            "manufacturers", ct);
    }

    /// <summary>Relations stored on this firearm. A derived-from or copy-of link that would close a loop is refused.</summary>
    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceRelations(
        int id, uint version, RelationsInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var slugs = input.Relations.Select(r => r.Firearm).Distinct().ToList();
        if (slugs.Contains(firearm!.Slug))
        {
            return FirearmEditing.BadRequest("Self relation", "A firearm cannot be related to itself.");
        }

        var targets = await db.Firearms.IgnoreQueryFilters().AsTracking().Where(f => slugs.Contains(f.Slug)).ToDictionaryAsync(f => f.Slug, ct);
        if (slugs.Except(targets.Keys).ToList() is { Count: > 0 } unknown)
        {
            return FirearmEditing.UnknownReference("firearm", unknown);
        }

        var before = firearm.RelationsFrom.Select(r => new RelationInput(r.Type, r.ToFirearm.Slug, r.Note));
        if (before.ToHashSet().SetEquals(input.Relations))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        var parents = input.Relations
            .Where(r => r.Type is RelationType.DerivedFrom or RelationType.CopyOf)
            .Select(r => targets[r.Firearm].Id)
            .Distinct()
            .ToList();

        return await ReplaceAsync(db, firearm, firearm.RelationsFrom,
            [.. input.Relations.Select(r => new FirearmRelation { FromFirearm = firearm, ToFirearm = targets[r.Firearm], Type = r.Type, Note = r.Note })],
            "relations", ct,
            beforeWriting: async () =>
            {
                // Two concurrent edits on different firearms (A from B, B from A) would each pass a check made
                // outside the transaction. Serialize derivation writes, then check against committed edges.
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({DerivationWriteLock})", ct);
                return await db.FindDerivationLoopAsync(firearm.Id, parents, ct) is { } loop
                    ? FirearmEditing.BadRequest("Derivation loop", $"This would make a firearm derive from itself: {loop}.")
                    : null;
            });
    }

    /// <summary>Advisory lock key held while a firearm's relations are replaced.</summary>
    private const long DerivationWriteLock = 0x41524D5F52454CL; // "ARM_REL"

    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceAdoptions(
        int id, uint version, AdoptionsInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (firearm, problem) = await db.LoadForWriteAsync(id, version, ct);
        if (problem is not null)
        {
            return problem;
        }

        var slugs = input.Adoptions.Select(a => a.Country).Distinct().ToList();
        var countries = await db.Countries.AsTracking().Where(c => slugs.Contains(c.Slug)).ToDictionaryAsync(c => c.Slug, ct);
        if (slugs.Except(countries.Keys).ToList() is { Count: > 0 } unknown)
        {
            return FirearmEditing.UnknownReference("country", unknown);
        }

        var before = firearm!.Adoptions.Select(a => new AdoptionInput(a.Country.Slug, FuzzyDateDto.From(a.From), FuzzyDateDto.From(a.To), a.Note));
        if (before.ToHashSet().SetEquals(input.Adoptions))
        {
            return TypedResults.Ok(db.ToAdminDto(firearm));
        }

        return await ReplaceAsync(db, firearm, firearm.Adoptions,
            [.. input.Adoptions.Select(a => new Adoption { Firearm = firearm, Country = countries[a.Country], From = a.From.ToEntity(), To = a.To.ToEntity(), Note = a.Note })],
            "adoptions", ct);
    }

    /// <summary>
    /// Deletes the old rows and inserts the new ones in two saves inside one transaction, so unique indexes
    /// (one primary cartridge, relation keys) never see old and new rows at once.
    /// </summary>
    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> ReplaceAsync<T>(
        ArmoryWikiDbContext db, Firearm firearm, List<T> current, List<T> replacement, string lockKey, CancellationToken ct,
        Func<Task<ProblemHttpResult?>>? beforeWriting = null)
        where T : class
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (beforeWriting is not null && await beforeWriting() is { } refused)
        {
            return refused;
        }

        db.RemoveRange(current.ToList());
        if (await db.SaveFirearmAsync(firearm, ct) is { } conflict)
        {
            return conflict;
        }

        db.AddRange(replacement);
        firearm.Lock(lockKey);
        if (await db.SaveFirearmAsync(firearm, ct) is { } again)
        {
            return again;
        }

        await tx.CommitAsync(ct);
        return TypedResults.Ok(await db.ReloadDtoAsync(firearm.Id, ct));
    }

    // Variant fields --------------------------------------------------------------------------------------------

    private static readonly VariantInput BlankVariant = new(null, "", null, null, null, null, null, null, null, null, null, null, [], null);

    private static string UniqueVariantSlug(Firearm firearm, string name)
    {
        var root = Slug.FromName(name) is { Length: > 0 } s ? s : "variant";
        var candidate = root;
        for (var n = 2; firearm.Variants.Any(v => v.Slug == candidate); n++)
        {
            candidate = $"{root}-{n}";
        }

        return candidate;
    }

    /// <summary>
    /// Copies every field that differs from <paramref name="before"/> and locks it as variants/{id}/{field}.
    /// Returns whether anything changed.
    /// </summary>
    private static bool Apply(Firearm firearm, Variant v, VariantInput before, VariantInput input)
    {
        var changed = false;
        void Set<T>(string field, T old, T value, Action apply)
        {
            if (!EqualityComparer<T>.Default.Equals(old, value))
            {
                apply();
                firearm.Lock($"variants/{v.Id}/{field}");
                changed = true;
            }
        }

        var modes = input.FiringModes ?? [];
        var extra = input.ExtraSpecs ?? new Dictionary<string, string>();
        Set("slug", before.Slug, input.Slug, () => v.Slug = input.Slug!);
        Set("name", before.Name, input.Name, () => v.Name = input.Name);
        Set("introducedDate", before.IntroducedDate, input.IntroducedDate, () => v.IntroducedDate = input.IntroducedDate.ToEntity());
        Set("weightEmptyG", before.WeightEmptyG, input.WeightEmptyG, () => v.WeightEmptyG = input.WeightEmptyG);
        Set("overallLengthMm", before.OverallLengthMm, input.OverallLengthMm, () => v.OverallLengthMm = input.OverallLengthMm);
        Set("overallLengthFoldedMm", before.OverallLengthFoldedMm, input.OverallLengthFoldedMm, () => v.OverallLengthFoldedMm = input.OverallLengthFoldedMm);
        Set("barrelLengthMm", before.BarrelLengthMm, input.BarrelLengthMm, () => v.BarrelLengthMm = input.BarrelLengthMm);
        Set("rateOfFireMinRpm", before.RateOfFireMinRpm, input.RateOfFireMinRpm, () => v.RateOfFireMinRpm = input.RateOfFireMinRpm);
        Set("rateOfFireMaxRpm", before.RateOfFireMaxRpm, input.RateOfFireMaxRpm, () => v.RateOfFireMaxRpm = input.RateOfFireMaxRpm);
        Set("muzzleVelocityMps", before.MuzzleVelocityMps, input.MuzzleVelocityMps, () => v.MuzzleVelocityMps = input.MuzzleVelocityMps);
        Set("effectiveRangeM", before.EffectiveRangeM, input.EffectiveRangeM, () => v.EffectiveRangeM = input.EffectiveRangeM);
        Set("magazineCapacityStandard", before.MagazineCapacityStandard, input.MagazineCapacityStandard, () => v.MagazineCapacityStandard = input.MagazineCapacityStandard);
        if (!(before.FiringModes ?? []).ToHashSet().SetEquals(modes))
        {
            v.FiringModes = modes.Aggregate(FiringModes.None, (all, m) => all | m);
            firearm.Lock($"variants/{v.Id}/firingModes");
            changed = true;
        }

        var beforeExtra = before.ExtraSpecs ?? new Dictionary<string, string>();
        if (beforeExtra.Count != extra.Count || beforeExtra.Except(extra).Any())
        {
            v.ExtraSpecs = new Dictionary<string, string>(extra);
            firearm.Lock($"variants/{v.Id}/extraSpecs");
            changed = true;
        }

        return changed;
    }
}
