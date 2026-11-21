using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Infrastructure.Seed;

/// <summary>
/// Vocabulary every environment needs: the operating mechanism tree, tags, the Wikidata class → tag map, and the
/// Wikidata source that imported entities cite.
/// Runs after every migration. Every entry is seeded at most once (tracked in <c>reference_seed_log</c>): new
/// entries added here appear on the next run, and entries an admin deleted or renamed stay that way.
/// </summary>
public static class ReferenceDataSeeder
{
    private sealed record MechanismSeed(string Slug, string Name, string? ParentSlug, string Explanation);

    // Parents before children.
    private static readonly MechanismSeed[] Mechanisms =
    [
        new("manual", "Manual", null, "The shooter cycles the action by hand between shots."),
        new("bolt-action", "Bolt action", "manual", "A bolt is lifted, drawn back and pushed forward by hand to extract, eject, chamber and lock."),
        new("lever-action", "Lever action", "manual", "A lever beneath the receiver, usually around the trigger guard, cycles the action."),
        new("pump-action", "Pump action", "manual", "A sliding fore-end is pulled back and pushed forward to cycle the action."),
        new("break-action", "Break action", "manual", "The barrels hinge open for manual loading and unloading."),
        new("blowback", "Blowback", null, "Pressure from the fired cartridge pushes the case, and with it the bolt, rearward to cycle the action."),
        new("simple-blowback", "Simple blowback", "blowback", "The bolt is held closed only by its own mass and a spring; there is no lock or delay."),
        new("advanced-primer-ignition", "Advanced primer ignition", "blowback", "The cartridge fires while the bolt is still moving forward, so part of the recoil impulse is spent stopping the bolt."),
        new("roller-delayed-blowback", "Roller-delayed blowback", "blowback", "Rollers in the bolt head must be cammed inward before the bolt can move rearward, delaying its opening."),
        new("lever-delayed-blowback", "Lever-delayed blowback", "blowback", "A lever at mechanical disadvantage must be moved before the bolt can travel rearward."),
        new("gas-delayed-blowback", "Gas-delayed blowback", "blowback", "Gas bled from the barrel acts against the slide or bolt, slowing its rearward travel."),
        new("recoil-operated", "Recoil-operated", null, "The barrel and bolt recoil together while locked, then unlock; recoil energy cycles the action."),
        new("short-recoil", "Short recoil", "recoil-operated", "Barrel and bolt travel back together a short distance before unlocking; the bolt then continues alone."),
        new("long-recoil", "Long recoil", "recoil-operated", "Barrel and bolt recoil together for at least the length of a cartridge before unlocking."),
        new("gas-operated", "Gas-operated", null, "Propellant gas tapped from the barrel drives the mechanism that unlocks and cycles the action."),
        new("long-stroke-piston", "Long-stroke piston", "gas-operated", "The gas piston is attached to the bolt carrier and travels with it through the full cycle."),
        new("short-stroke-piston", "Short-stroke piston", "gas-operated", "The gas piston moves only a short distance, imparting a push to the bolt carrier, which completes the cycle by momentum."),
        new("direct-impingement", "Direct impingement", "gas-operated", "Gas is routed through a tube directly to the bolt carrier, which acts as the piston; there is no separate piston."),
        new("gas-trap", "Gas trap", "gas-operated", "Gas is captured by a cup or trap at the muzzle rather than through a port in the barrel."),
    ];

    private static readonly (string Slug, string Name, TagGroup Group)[] Tags =
    [
        ("handgun", "Handgun", TagGroup.Type),
        ("revolver", "Revolver", TagGroup.Type),
        ("submachine-gun", "Submachine gun", TagGroup.Type),
        ("rifle", "Rifle", TagGroup.Type),
        ("carbine", "Carbine", TagGroup.Type),
        ("assault-rifle", "Assault rifle", TagGroup.Type),
        ("battle-rifle", "Battle rifle", TagGroup.Type),
        ("shotgun", "Shotgun", TagGroup.Type),
        ("machine-gun", "Machine gun", TagGroup.Type),
        ("designated-marksman", "Designated marksman", TagGroup.Role),
        ("sniper", "Sniper", TagGroup.Role),
        ("personal-defense", "Personal defense", TagGroup.Role),
        ("ww1", "World War I", TagGroup.Era),
        ("ww2", "World War II", TagGroup.Era),
        ("cold-war", "Cold War", TagGroup.Era),
        ("post-cold-war", "Post–Cold War", TagGroup.Era),
    ];

    // Wikidata "instance of" classes, resolved from their English Wikipedia sitelinks.
    private static readonly (string Qid, string TagSlug)[] ClassTags =
    [
        ("Q177456", "assault-rifle"),
        ("Q1194268", "battle-rifle"),
        ("Q183302", "carbine"),
        ("Q178550", "submachine-gun"),
        ("Q1907525", "handgun"), // pistol
        ("Q1574963", "handgun"),
        ("Q170382", "revolver"),
        ("Q12800", "machine-gun"),
        ("Q486396", "shotgun"),
        ("Q190570", "sniper"),
        ("Q124072", "rifle"),
        ("Q2291248", "rifle"), // semi-automatic rifle
    ];

    /// <summary>Identifies the Wikidata <see cref="Source"/> (PLAN.md Phase 4a).</summary>
    public const string WikidataSourceUrl = "https://www.wikidata.org";

    public static async Task SeedAsync(ArmoryWikiDbContext db, CancellationToken ct = default)
    {
        // Loaded entities are reused as navigation targets, so they must be tracked.
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        var log = (await db.ReferenceSeedLog.Select(e => e.Key).ToListAsync(ct)).ToHashSet();

        // Each key is handled once, ever: inserted when missing, or just recorded when it already exists
        // (databases seeded before the log). Keys an admin since deleted or renamed are never re-created.
        bool FirstTime(string key)
        {
            if (!log.Add(key))
            {
                return false;
            }

            db.ReferenceSeedLog.Add(new ReferenceSeedLogEntry { Key = key, SeededAt = DateTime.UtcNow });
            return true;
        }

        var mechanisms = await db.OperatingMechanisms.ToDictionaryAsync(m => m.Slug, ct);
        foreach (var seed in Mechanisms.Where(s => !log.Contains($"mechanism:{s.Slug}")))
        {
            OperatingMechanism? parent = null;
            if (!mechanisms.ContainsKey(seed.Slug) && seed.ParentSlug is not null && !mechanisms.TryGetValue(seed.ParentSlug, out parent))
            {
                continue; // the parent was renamed or removed: leave it unlogged and try again next run
            }

            if (!FirstTime($"mechanism:{seed.Slug}") || mechanisms.ContainsKey(seed.Slug))
            {
                continue;
            }

            var mechanism = new OperatingMechanism
            {
                Slug = seed.Slug,
                Name = seed.Name,
                Parent = parent,
                Path = $"{parent?.Path ?? "/"}{seed.Slug}/",
                Explanation = seed.Explanation,
            };
            db.OperatingMechanisms.Add(mechanism);
            mechanisms[seed.Slug] = mechanism;
        }

        var tags = await db.Tags.ToDictionaryAsync(t => t.Slug, ct);
        foreach (var (slug, name, group) in Tags.Where(t => FirstTime($"tag:{t.Slug}") && !tags.ContainsKey(t.Slug)))
        {
            var tag = new Tag { Slug = slug, Name = name, Group = group };
            db.Tags.Add(tag);
            tags[slug] = tag;
        }

        var mapped = (await db.WikidataClassTagMaps.Select(m => new { m.Qid, m.Tag.Slug }).ToListAsync(ct))
            .Select(m => (m.Qid, m.Slug))
            .ToHashSet();
        foreach (var (qid, tagSlug) in ClassTags.Where(c => FirstTime($"class-tag:{c.Qid}:{c.TagSlug}") && !mapped.Contains(c)))
        {
            if (tags.TryGetValue(tagSlug, out var tag))
            {
                db.WikidataClassTagMaps.Add(new WikidataClassTagMap { Qid = qid, Tag = tag });
            }
        }

        if (FirstTime("source:wikidata") && !await db.Sources.AnyAsync(x => x.Url == WikidataSourceUrl, ct))
        {
            db.Sources.Add(new Source { Type = SourceType.Dataset, Title = "Wikidata", Publisher = "Wikimedia Foundation", Url = WikidataSourceUrl });
        }

        await db.SaveChangesAsync(ct);
    }
}
