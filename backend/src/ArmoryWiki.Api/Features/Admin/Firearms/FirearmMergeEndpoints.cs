using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Firearms;

/// <summary>
/// What a merge would do, in words for the admin: <see cref="Blockers"/> stop it; <see cref="Moves"/> carry over to
/// the target; <see cref="Drops"/> are lost with the merged firearm.
/// </summary>
public sealed record MergePreviewDto(string Source, string Target, IReadOnlyList<string> Blockers, IReadOnlyList<string> Moves, IReadOnlyList<string> Drops);

/// <summary>
/// Merge into variant (GLOSSARY.md, ADR-0007): a draft firearm that turns out to be a version of another design
/// becomes a variant of it. The preview and the merge come from one <see cref="Plan"/>, so what the admin confirms
/// is what happens.
/// </summary>
public static class FirearmMergeEndpoints
{
    private const string Route = "/{id:int}/merge-into/{targetId:int}";

    public static void MapFirearmMerge(this RouteGroupBuilder firearms)
    {
        firearms.MapGet(Route, Preview).ProducesProblem(StatusCodes.Status404NotFound);
        firearms.MapPost(Route, Merge).ProducesWriteProblems();
    }

    private sealed class Plan
    {
        public required Firearm Source { get; init; }
        public required Firearm Target { get; init; }
        public List<string> Blockers { get; } = [];
        public List<string> Moves { get; } = [];
        public List<string> Drops { get; } = [];
        public List<FirearmDesigner> Designers { get; } = [];
        public List<FirearmManufacturer> Manufacturers { get; } = [];
        public List<Adoption> Adoptions { get; } = [];
        public List<Tag> Tags { get; } = [];
        /// <summary>Relations other firearms hold on the source: re-pointed at the target, or dropped.</summary>
        public List<(FirearmRelation Relation, bool Repoint)> RelationsTo { get; } = [];
        public List<string> DuplicateVideos { get; } = [];

        public MergePreviewDto ToDto() => new(Source.Name, Target.Name, Blockers, Moves, Drops);
    }

    private static async Task<(Plan? Plan, ProblemHttpResult? Problem)> PlanAsync(ArmoryWikiDbContext db, int id, int targetId, CancellationToken ct)
    {
        var source = await db.AdminGraph().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (source is null)
        {
            return (null, AdminProblems.NotFound("Firearm", id));
        }

        var target = targetId == id ? source : await db.AdminGraph().FirstOrDefaultAsync(f => f.Id == targetId, ct);
        if (target is null)
        {
            return (null, AdminProblems.NotFound("Firearm", targetId));
        }

        var plan = new Plan { Source = source, Target = target };
        if (source.Id == target.Id)
        {
            plan.Blockers.Add("A firearm can't be merged into itself.");
        }

        if (source.Status == PublishStatus.Published)
        {
            plan.Blockers.Add($"{source.Name} is published: unpublish it first. Only drafts are merged, so nothing published disappears.");
        }

        if (plan.Blockers.Count > 0)
        {
            return (plan, null);
        }

        // Variants and what hangs off them (specifications, ammunition, citations) move as they are.
        var baseVariant = source.Variants.Single(v => v.IsBaseVariant);
        var qid = source.WikidataId is { } q ? $", which keeps its Wikidata id {q} so imports recognise it" : "";
        plan.Moves.Add($"Its base variant becomes the variant “{source.Name}” of {target.Name}, with its specifications and ammunition{qid}.");
        foreach (var v in source.Variants.Where(v => !v.IsBaseVariant))
        {
            plan.Moves.Add($"Its variant “{v.Name}” becomes a variant of {target.Name}.");
        }

        var images = await db.Images.CountAsync(i => i.FirearmId == source.Id, ct);
        var citations = await db.Citations.CountAsync(c => c.FirearmId == source.Id, ct);
        var videos = await db.Videos.IgnoreQueryFilters().Where(v => v.FirearmId == source.Id).Select(v => v.YouTubeVideoId).ToListAsync(ct);
        var targetVideos = await db.Videos.IgnoreQueryFilters().Where(v => v.FirearmId == target.Id).Select(v => v.YouTubeVideoId).ToListAsync(ct);
        plan.DuplicateVideos.AddRange(videos.Intersect(targetVideos));
        Count(plan.Moves, images, "image", "images");
        Count(plan.Moves, citations, "source citation", "source citations");
        Count(plan.Moves, videos.Count - plan.DuplicateVideos.Count, "video", "videos");
        Count(plan.Drops, plan.DuplicateVideos.Count, $"video {target.Name} already has", $"videos {target.Name} already has");

        foreach (var d in source.Designers)
        {
            var name = d.Person?.FullName ?? d.Organization!.Name;
            if (target.Designers.Any(t => t.PersonId == d.PersonId && t.OrganizationId == d.OrganizationId))
            {
                plan.Drops.Add($"Designer {name}: already credited on {target.Name}.");
            }
            else
            {
                plan.Moves.Add($"Designer {name}.");
                plan.Designers.Add(new FirearmDesigner { FirearmId = target.Id, PersonId = d.PersonId, OrganizationId = d.OrganizationId, Role = d.Role });
            }
        }

        foreach (var m in source.Manufacturers)
        {
            if (target.Manufacturers.Any(t => t.OrganizationId == m.OrganizationId))
            {
                plan.Drops.Add($"Manufacturer {m.Organization.Name}: already listed on {target.Name}.");
            }
            else
            {
                plan.Moves.Add($"Manufacturer {m.Organization.Name}.");
                plan.Manufacturers.Add(new FirearmManufacturer { FirearmId = target.Id, OrganizationId = m.OrganizationId, From = m.From, To = m.To, IsLicensed = m.IsLicensed });
            }
        }

        foreach (var a in source.Adoptions)
        {
            if (target.Adoptions.Any(t => t.CountryId == a.CountryId))
            {
                plan.Drops.Add($"Adoption by {a.Country.Name}: already listed on {target.Name}.");
            }
            else
            {
                plan.Moves.Add($"Adoption by {a.Country.Name}.");
                plan.Adoptions.Add(new Adoption { FirearmId = target.Id, CountryId = a.CountryId, From = a.From, To = a.To, Note = a.Note });
            }
        }

        plan.Tags.AddRange(source.Tags.Where(t => target.Tags.All(x => x.Id != t.Id)));
        if (plan.Tags.Count > 0)
        {
            plan.Moves.Add($"Tags: {string.Join(", ", plan.Tags.Select(t => t.Name))}.");
        }

        // Relations other firearms hold on the source now point at the target, unless that would be a duplicate,
        // a relation to itself, or a derivation loop.
        var relationsTo = await db.FirearmRelations.IgnoreQueryFilters().AsTracking().Include(r => r.FromFirearm)
            .Where(r => r.ToFirearmId == source.Id && r.FromFirearmId != source.Id).ToListAsync(ct);
        foreach (var r in relationsTo)
        {
            var from = r.FromFirearm.Name;
            string? reason = r.FromFirearmId == target.Id
                ? $"{target.Name}'s own relation to {source.Name}"
                : await db.FirearmRelations.IgnoreQueryFilters().AnyAsync(x => x.FromFirearmId == r.FromFirearmId && x.ToFirearmId == target.Id && x.Type == r.Type, ct)
                    ? $"{from}'s relation to {source.Name}: it already has the same one with {target.Name}"
                    : r.Type is RelationType.DerivedFrom or RelationType.CopyOf && await db.FindDerivationLoopAsync(r.FromFirearmId, [target.Id], ct) is { } loop
                        ? $"{from}'s relation to {source.Name}: pointing it at {target.Name} would loop ({loop})"
                        : null;
            plan.RelationsTo.Add((r, reason is null));
            if (reason is null)
            {
                plan.Moves.Add($"{from}'s relation now points at {target.Name}.");
            }
            else
            {
                plan.Drops.Add($"{reason}.");
            }
        }

        foreach (var r in source.RelationsFrom)
        {
            plan.Drops.Add($"Its own relation to {r.ToFirearm.Name} (relations stored on a merged firearm are not kept).");
        }

        var details = new (string Name, bool Filled)[]
        {
            ("summary", source.Summary is not null),
            ("development history", source.DevelopmentHistory is not null),
            ("service history", source.ServiceHistory is not null),
            ("alternative names", source.AlternativeNames.Count > 0),
            ("design date", source.DesignDate is not null),
            ("production dates", source.ProductionStart is not null || source.ProductionEnd is not null),
            ("country of origin", source.CountryOfOriginId is not null),
            ("operating mechanism", source.OperatingMechanismId is not null),
            ("feed system", source.FeedSystem is not null),
            ("Wikipedia article", source.WikipediaTitle is not null),
        }.Where(d => d.Filled).Select(d => d.Name).ToList();
        if (details.Count > 0)
        {
            plan.Drops.Add($"Its own details: {string.Join(", ", details)}.");
        }

        plan.Moves.Add($"Its address /firearms/{source.Slug} leads to {target.Name} from now on.");
        return (plan, null);
    }

    private static void Count(List<string> into, int count, string one, string many)
    {
        if (count > 0)
        {
            into.Add(count == 1 ? $"1 {one}." : $"{count} {many}.");
        }
    }

    private static async Task<Results<Ok<MergePreviewDto>, ProblemHttpResult>> Preview(int id, int targetId, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var (plan, problem) = await PlanAsync(db, id, targetId, ct);
        if (problem is not null)
        {
            return problem;
        }

        return TypedResults.Ok(plan!.ToDto());
    }

    /// <summary>
    /// Merges, in one transaction, and answers with the target. Both versions are checked, so the merge never
    /// acts on what the admin did not preview.
    /// </summary>
    private static async Task<Results<Ok<AdminFirearmDto>, ProblemHttpResult>> Merge(
        int id, int targetId, uint version, uint targetVersion, ArmoryWikiDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (plan, problem) = await PlanAsync(db, id, targetId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var (source, target) = (plan!.Source, plan.Target);
        if (db.VersionOf(source) != version || db.VersionOf(target) != targetVersion)
        {
            return AdminProblems.VersionConflict(null);
        }

        if (plan.Blockers.Count > 0)
        {
            return AdminProblems.Conflict("Can't merge", string.Join(" ", plan.Blockers));
        }

        // Variants move first, out of the source's collection, so deleting the source can't cascade to them.
        var taken = target.Variants.Select(v => v.Slug).ToHashSet();
        foreach (var v in source.Variants.ToList())
        {
            // The base variant stands for the merged firearm: its name, address and QID.
            var wasBase = v.IsBaseVariant;
            if (wasBase)
            {
                v.IsBaseVariant = false;
                v.Name = source.Name;
                v.WikidataId = source.WikidataId;
            }

            v.Slug = Unique(wasBase ? source.Slug : v.Slug, taken);
            taken.Add(v.Slug);
            source.Variants.Remove(v);
            target.Variants.Add(v);
            foreach (var key in source.LockedFields.Where(k => k.StartsWith($"variants/{v.Id}/", StringComparison.Ordinal) || k == $"chamberings/{v.Id}"))
            {
                target.Lock(key);
            }
        }

        source.WikidataId = null; // the variant holds it now
        target.Designers.AddRange(plan.Designers);
        target.Manufacturers.AddRange(plan.Manufacturers);
        target.Adoptions.AddRange(plan.Adoptions);
        target.Tags.AddRange(plan.Tags);
        foreach (var (key, changed) in new[] { ("designers", plan.Designers.Count), ("manufacturers", plan.Manufacturers.Count), ("adoptions", plan.Adoptions.Count), ("tags", plan.Tags.Count) })
        {
            if (changed > 0)
            {
                target.Lock(key); // an admin chose them: imports leave the list alone
            }
        }

        foreach (var (relation, repoint) in plan.RelationsTo)
        {
            db.FirearmRelations.Remove(relation); // the key includes the target firearm, so a re-point is remove and add
            if (repoint)
            {
                db.FirearmRelations.Add(new FirearmRelation { FromFirearmId = relation.FromFirearmId, ToFirearmId = target.Id, Type = relation.Type, Note = relation.Note });
            }
        }

        if (await db.SaveFirearmAsync(target, ct) is { } conflict)
        {
            return conflict;
        }

        // Rows that only point at the firearm move in place.
        var nextOrder = await db.Images.Where(i => i.FirearmId == target.Id).Select(i => (int?)i.SortOrder).MaxAsync(ct) ?? -1;
        await db.Images.Where(i => i.FirearmId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.FirearmId, target.Id).SetProperty(i => i.SortOrder, i => i.SortOrder + nextOrder + 1), ct);
        await db.Citations.Where(c => c.FirearmId == source.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.FirearmId, target.Id), ct);
        await db.Videos.IgnoreQueryFilters().Where(v => v.FirearmId == source.Id && !plan.DuplicateVideos.Contains(v.YouTubeVideoId))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.FirearmId, target.Id), ct);

        // The source goes (its own details, relations and duplicate videos with it); its addresses lead to the target.
        var oldSlug = source.Slug;
        await db.SlugRedirects.Where(r => r.EntityType == SluggedEntity.Firearm && r.EntityId == source.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.EntityId, target.Id), ct);
        db.Firearms.Remove(source);
        await db.RecordRenameAsync(SluggedEntity.Firearm, oldSlug, target.Id, ct);
        if (await db.TrySaveAsync<Firearm>(source.Id, ct) is { } failed)
        {
            return failed;
        }

        await tx.CommitAsync(ct);
        return TypedResults.Ok(await db.ReloadDtoAsync(target.Id, ct));
    }

    private static string Unique(string slug, HashSet<string> taken)
    {
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }
}
