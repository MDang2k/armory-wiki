using ArmoryWiki.Api.Features.Admin;
using ArmoryWiki.Api.Features.Admin.Firearms;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using ArmoryWiki.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// <c>import-wikidata</c> (PLAN.md Phase 4a): small arms and, one level deep, their designers, manufacturers,
/// cartridges and countries. Follows the import rules in GLOSSARY.md: drafts are refreshed, published content only
/// gets empty fields filled, locked fields are never written, and nothing is published, unpublished or deleted.
/// </summary>
public sealed class WikidataImporter(ArmoryWikiDbContext db, WikimediaClient wikidata, ImportReport report)
{
    public const string Command = "import-wikidata";

    // Classes reached through the mapped type classes that are not single firearm designs.
    private const string ArtilleryModel = "Q124056273", WeaponFamily = "Q15142889", FirearmFamily = "Q124054676";
    // An item that is an instance of a model is one individual specimen of it.
    private const string WeaponModel = "Q15142894", FirearmModel = "Q22704163";

    private readonly Dictionary<string, int> _countries = [], _people = [], _organizations = [], _cartridges = [], _firearms = [];
    private readonly Dictionary<string, string> _mergedInto = [];
    private int _sourceId;

    public async Task RunAsync(CancellationToken ct)
    {
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        _sourceId = await db.Sources.Where(s => s.Url == ReferenceDataSeeder.WikidataSourceUrl).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The Wikidata source is missing: run the reference seeder first (start the API once).");
        var classTags = (await db.WikidataClassTagMaps.Select(m => new { m.Qid, m.TagId }).ToListAsync(ct)).ToLookup(m => m.Qid, m => m.TagId);

        var discovered = (await wikidata.SparqlQidsAsync(DiscoveryQuery([.. classTags.Select(g => g.Key)]), ct)).ToHashSet();
        report.Discovered = discovered.Count;
        await ReconcileStoredFirearmsAsync(discovered, ct);

        var firearms = new List<(string Qid, FirearmFacts Facts)>();
        foreach (var (qid, item) in await FetchAsync(discovered, ct))
        {
            if (item?.Label is null)
            {
                report.Add("firearm", qid, item is null ? "missing on Wikidata" : "no English label; add one on Wikidata or create the firearm by hand");
                report.Count("firearm", "skipped");
                continue;
            }

            var facts = WikidataMapper.ToFirearm(item);
            firearms.Add((item.Qid, facts));
            foreach (var skipped in facts.Skipped)
            {
                report.Add("firearm", item.Qid, $"skipped {skipped}");
            }
        }

        // One level deep: what the firearms reference, plus those entities' countries.
        var related = await FetchAsync(
            firearms.SelectMany(f => f.Facts.DesignerQids.Concat(f.Facts.ManufacturerQids).Concat(f.Facts.AmmunitionQids)), ct);
        var relatedItems = related.Values.OfType<WikidataEntity>().DistinctBy(e => e.Qid).ToList();
        var ammunition = firearms.SelectMany(f => f.Facts.AmmunitionQids).Select(Resolve).ToHashSet();
        await ImportCountriesAsync(
            firearms.Select(f => f.Facts.CountryQid)
                .Concat(relatedItems.SelectMany(e => new[] { e.SingleItem(Wd.Citizenship), e.SingleItem(Wd.Country), e.SingleItem(Wd.CountryOfOrigin) }))
                .OfType<string>(),
            ct);

        var cartridgeParents = new List<(int Id, string ParentQid)>();
        foreach (var item in relatedItems)
        {
            if (item.Label is null)
            {
                report.Add("related", item.Qid, "no English label; not imported");
                continue;
            }

            if (ammunition.Contains(item.Qid))
            {
                if (await UpsertCartridgeAsync(item, ct) is { } id && item.SingleItem(Wd.BasedOn) is { } parent)
                {
                    cartridgeParents.Add((id, parent));
                }
            }
            else if (item.Items(Wd.InstanceOf).Contains(Wd.Human))
            {
                await UpsertPersonAsync(item, ct);
            }
            else
            {
                await UpsertOrganizationAsync(item, ct);
            }
        }

        await LinkCartridgeParentsAsync(cartridgeParents, ct);

        foreach (var (qid, facts) in firearms)
        {
            await UpsertFirearmAsync(qid, facts, classTags, ct);
        }

        // Every firearm with a QID can be a relation target, not only this run's.
        foreach (var f in await db.Firearms.IgnoreQueryFilters().Where(f => f.WikidataId != null).Select(f => new { f.Id, f.WikidataId }).ToListAsync(ct))
        {
            _firearms.TryAdd(f.WikidataId!, f.Id);
        }

        foreach (var (qid, facts) in firearms.Where(f => f.Facts.BasedOnQids.Count > 0))
        {
            await AddDerivationsAsync(qid, facts.BasedOnQids, ct);
        }
    }

    /// <summary>Items reaching one of the mapped type classes through "instance of" or "subclass of".</summary>
    public static string DiscoveryQuery(IReadOnlyCollection<string> classQids)
    {
        var classes = string.Join(" ", classQids.Select(q => "wd:" + q));
        return $$"""
            SELECT DISTINCT ?item WHERE {
              hint:Query hint:optimizer "None" .
              VALUES ?class { {{classes}} }
              ?sub wdt:P279* ?class .
              ?item wdt:P31|wdt:P279 ?sub .
              ?item wdt:P176|wdt:P739|wdt:P287 [] .
              FILTER NOT EXISTS { VALUES ?notADesign { wd:{{ArtilleryModel}} wd:{{WeaponFamily}} wd:{{FirearmFamily}} } ?item wdt:P31 ?notADesign }
              FILTER NOT EXISTS { VALUES ?model { wd:{{WeaponModel}} wd:{{FirearmModel}} } ?item wdt:P31/wdt:P31 ?model }
              FILTER(?item NOT IN ({{string.Join(", ", classQids.Select(q => "wd:" + q))}}))
            }
            """;
    }

    /// <summary>
    /// Stored firearms the query no longer finds: merged items get their QID moved to the item they were merged
    /// into (when no other firearm holds it); deleted or reclassified ones are reported, never touched.
    /// </summary>
    // ponytail: only firearms are checked; a merged person, organization or cartridge would come back as a new
    // draft. Check those too if duplicates show up.
    private async Task ReconcileStoredFirearmsAsync(HashSet<string> discovered, CancellationToken ct)
    {
        var stored = await db.Firearms.IgnoreQueryFilters().Where(f => f.WikidataId != null)
            .Select(f => new { f.Id, f.WikidataId }).ToDictionaryAsync(f => f.WikidataId!, f => f.Id, ct);
        var gone = stored.Keys.Where(q => !discovered.Contains(q)).ToList();
        foreach (var (qid, item) in await FetchAsync(gone, ct))
        {
            if (item is null)
            {
                report.Add("firearm", qid, "deleted on Wikidata; review the firearm");
            }
            else if (item.Qid == qid)
            {
                report.Add("firearm", qid, "no longer matches the small-arms query; review the firearm");
            }
            else if (stored.ContainsKey(item.Qid))
            {
                report.Add("firearm", qid, $"merged into {item.Qid}, which another firearm already has; merge them by hand");
            }
            else
            {
                var firearm = await db.Firearms.IgnoreQueryFilters().SingleAsync(f => f.Id == stored[qid], ct);
                firearm.WikidataId = item.Qid;
                report.Add("firearm", qid, $"merged into {item.Qid} on Wikidata; QID updated");
                await SaveAsync("firearm", qid, isNew: false, ct);
                if (!discovered.Contains(item.Qid))
                {
                    report.Add("firearm", item.Qid, "no longer matches the small-arms query; review the firearm");
                }
            }
        }
    }

    // Countries are created, never updated: they have no locks to protect an admin's edits.
    private async Task ImportCountriesAsync(IEnumerable<string> qids, CancellationToken ct)
    {
        var wanted = qids.ToHashSet();
        foreach (var c in await db.Countries.Where(c => c.WikidataId != null && wanted.Contains(c.WikidataId)).Select(c => new { c.Id, c.WikidataId }).ToListAsync(ct))
        {
            _countries[c.WikidataId!] = c.Id;
        }

        foreach (var (qid, item) in await FetchAsync(wanted.Where(q => !_countries.ContainsKey(q)), ct))
        {
            if (item?.Label is null)
            {
                report.Add("country", qid, "no English label; not imported");
                report.Count("country", "skipped");
                continue;
            }

            // The claim may point at an item that was merged into a country we already have.
            if (_countries.ContainsKey(item.Qid))
            {
                continue;
            }

            if (await db.Countries.Where(c => c.WikidataId == item.Qid).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct) is { } existing)
            {
                _countries[item.Qid] = existing;
                continue;
            }

            var country = new Country
            {
                Slug = await db.Countries.UniqueSlugAsync(item.Label, "country", ct),
                Name = item.Label,
                WikidataId = item.Qid,
                IsHistorical = item.Has(Wd.Dissolved),
            };
            db.Countries.Add(country);
            if (await SaveAsync("country", qid, isNew: true, ct))
            {
                _countries[item.Qid] = country.Id;
            }
        }
    }

    private async Task UpsertPersonAsync(WikidataEntity item, CancellationToken ct)
    {
        var person = await db.People.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.WikidataId == item.Qid, ct);
        var isNew = person is null;
        if (person is null)
        {
            person = new Person { Slug = await db.People.UniqueSlugAsync(item.Label!, "person", ct), FullName = item.Label!, WikidataId = item.Qid };
            db.People.Add(person);
            await db.ClaimSlugAsync(SluggedEntity.Person, person.Slug, ct);
        }

        var f = new ImportFields(person, isNew);
        f.Set("fullName", person.FullName, item.Label, v => person.FullName = v);
        f.SetDate("birthDate", person.BirthDate, item.Date(Wd.BirthDate), v => person.BirthDate = v);
        f.SetDate("deathDate", person.DeathDate, item.Date(Wd.DeathDate), v => person.DeathDate = v);
        f.Set("nationality", person.NationalityId, CountryId(item.SingleItem(Wd.Citizenship)), v => person.NationalityId = v);
        Cite(c => c.Person = person, isNew || !await db.Citations.AnyAsync(c => c.PersonId == person.Id && c.SourceId == _sourceId, ct), item.Qid);
        if (await SaveAsync("person", item.Qid, isNew, ct))
        {
            _people[item.Qid] = person.Id;
        }
    }

    private async Task UpsertOrganizationAsync(WikidataEntity item, CancellationToken ct)
    {
        var organization = await db.Organizations.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.WikidataId == item.Qid, ct);
        var isNew = organization is null;
        if (organization is null)
        {
            organization = new Organization { Slug = await db.Organizations.UniqueSlugAsync(item.Label!, "organization", ct), Name = item.Label!, WikidataId = item.Qid };
            db.Organizations.Add(organization);
            await db.ClaimSlugAsync(SluggedEntity.Organization, organization.Slug, ct);
        }

        var f = new ImportFields(organization, isNew);
        f.Set("name", organization.Name, item.Label, v => organization.Name = v);
        f.SetDate("founded", organization.Founded, item.Date(Wd.Inception), v => organization.Founded = v);
        f.Set("country", organization.CountryId, CountryId(item.SingleItem(Wd.Country)), v => organization.CountryId = v);
        Cite(c => c.Organization = organization, isNew || !await db.Citations.AnyAsync(c => c.OrganizationId == organization.Id && c.SourceId == _sourceId, ct), item.Qid);
        if (await SaveAsync("organization", item.Qid, isNew, ct))
        {
            _organizations[item.Qid] = organization.Id;
        }
    }

    private async Task<int?> UpsertCartridgeAsync(WikidataEntity item, CancellationToken ct)
    {
        var cartridge = await db.Cartridges.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.WikidataId == item.Qid, ct);
        var isNew = cartridge is null;
        if (cartridge is null)
        {
            cartridge = new Cartridge { Slug = await db.Cartridges.UniqueSlugAsync(item.Label!, "cartridge", ct), Name = item.Label!, WikidataId = item.Qid };
            db.Cartridges.Add(cartridge);
            await db.ClaimSlugAsync(SluggedEntity.Cartridge, cartridge.Slug, ct);
        }

        var f = new ImportFields(cartridge, isNew);
        f.Set("name", cartridge.Name, item.Label, v => cartridge.Name = v);
        f.AddNames("alternativeNames", cartridge.AlternativeNames, item.Aliases);
        f.SetDate("designDate", cartridge.DesignDate, item.Date(Wd.Inception), v => cartridge.DesignDate = v);
        f.Set("country", cartridge.CountryOfOriginId, CountryId(item.SingleItem(Wd.CountryOfOrigin)), v => cartridge.CountryOfOriginId = v);
        Cite(c => c.Cartridge = cartridge, isNew || !await db.Citations.AnyAsync(c => c.CartridgeId == cartridge.Id && c.SourceId == _sourceId, ct), item.Qid);
        if (!await SaveAsync("cartridge", item.Qid, isNew, ct))
        {
            return null;
        }

        _cartridges[item.Qid] = cartridge.Id;
        return cartridge.Id;
    }

    /// <summary>A cartridge's parent, when the parent was imported too; parent links never loop (GLOSSARY.md).</summary>
    // ponytail: the loop check reads all parent links once; an admin editing parents during the run is not seen.
    private async Task LinkCartridgeParentsAsync(List<(int Id, string ParentQid)> links, CancellationToken ct)
    {
        var parentOf = await db.Cartridges.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id, c => c.ParentCartridgeId, ct);
        foreach (var (id, parentQid) in links)
        {
            if (!_cartridges.TryGetValue(Resolve(parentQid), out var parentId))
            {
                continue;
            }

            for (int? walk = parentId; walk is { } w; walk = parentOf.GetValueOrDefault(w))
            {
                if (w == id)
                {
                    report.Add("cartridge", parentQid, "parent link would loop; skipped");
                    goto next;
                }
            }

            var cartridge = await db.Cartridges.IgnoreQueryFilters().SingleAsync(c => c.Id == id, ct);
            new ImportFields(cartridge, isNew: false).Set("parent", cartridge.ParentCartridgeId, parentId, v => cartridge.ParentCartridgeId = v);
            if (await SaveAsync("cartridge parent", parentQid, isNew: false, ct))
            {
                parentOf[id] = cartridge.ParentCartridgeId;
            }

            next:;
        }
    }

    private async Task UpsertFirearmAsync(string qid, FirearmFacts facts, ILookup<string, int> classTags, CancellationToken ct)
    {
        var firearm = await db.Firearms.IgnoreQueryFilters()
            .Include(f => f.Tags)
            .Include(f => f.Designers)
            .Include(f => f.Manufacturers)
            .Include(f => f.Variants.Where(v => v.IsBaseVariant)).ThenInclude(v => v.Chamberings)
            .AsSplitQuery()
            .FirstOrDefaultAsync(f => f.WikidataId == qid, ct);
        var isNew = firearm is null;
        if (firearm is null)
        {
            firearm = new Firearm { Slug = await db.UniqueFirearmSlugAsync(facts.Name, ct), Name = facts.Name, WikidataId = qid };
            firearm.Variants.Add(new Variant { Slug = firearm.Slug, Name = firearm.Name, IsBaseVariant = true });
            db.Firearms.Add(firearm);
            await db.ClaimSlugAsync(SluggedEntity.Firearm, firearm.Slug, ct);
        }

        var f = new ImportFields(firearm, isNew);
        f.Set("name", firearm.Name, facts.Name, v => firearm.Name = v);
        f.AddNames("alternativeNames", firearm.AlternativeNames, facts.AlternativeNames);
        f.Set("country", firearm.CountryOfOriginId, CountryId(facts.CountryQid), v => firearm.CountryOfOriginId = v);
        f.SetDate("designDate", firearm.DesignDate, facts.DesignDate, v => firearm.DesignDate = v);
        f.Set("wikipediaTitle", firearm.WikipediaTitle, facts.WikipediaTitle, v => firearm.WikipediaTitle = v);

        // ponytail: type tags come from the item's own classes only; a class one level further down the tree
        // (e.g. "bullpup assault rifle") adds no tag until it is mapped too.
        var tagIds = facts.ClassQids.SelectMany(c => classTags[c]).Except(firearm.Tags.Select(t => t.Id)).ToList();
        if (tagIds.Count > 0 && f.CanAddTo("tags", firearm.Tags.Count))
        {
            firearm.Tags.AddRange(await db.Tags.Where(t => tagIds.Contains(t.Id)).ToListAsync(ct));
        }

        // One designer is the lead; with several, an admin picks the lead during review.
        var role = facts.DesignerQids.Count == 1 ? DesignerRole.LeadDesigner : DesignerRole.CoDesigner;
        if (f.CanAddTo("designers", firearm.Designers.Count))
        {
            foreach (var designer in facts.DesignerQids)
            {
                if (_people.TryGetValue(Resolve(designer), out var personId) && !firearm.Designers.Any(d => d.PersonId == personId))
                {
                    firearm.Designers.Add(new FirearmDesigner { PersonId = personId, Role = role });
                }
                else if (_organizations.TryGetValue(Resolve(designer), out var organizationId) && !firearm.Designers.Any(d => d.OrganizationId == organizationId))
                {
                    firearm.Designers.Add(new FirearmDesigner { OrganizationId = organizationId, Role = role });
                }
            }
        }

        if (f.CanAddTo("manufacturers", firearm.Manufacturers.Count))
        {
            foreach (var manufacturer in facts.ManufacturerQids)
            {
                if (_organizations.TryGetValue(Resolve(manufacturer), out var organizationId))
                {
                    if (!firearm.Manufacturers.Any(m => m.OrganizationId == organizationId))
                    {
                        firearm.Manufacturers.Add(new FirearmManufacturer { OrganizationId = organizationId });
                    }
                }
                else if (_people.ContainsKey(Resolve(manufacturer)))
                {
                    report.Add("firearm", qid, $"manufacturer {manufacturer} is a person; not linked");
                }
            }
        }

        if (firearm.Variants.SingleOrDefault(v => v.IsBaseVariant) is { } variant)
        {
            WriteSpecs(f, variant, facts);
        }

        if (!isNew)
        {
            TouchIfChildrenChanged(firearm);
        }

        Cite(c => c.Firearm = firearm, isNew || !await db.Citations.AnyAsync(c => c.FirearmId == firearm.Id && c.SourceId == _sourceId, ct), qid);
        if (await SaveAsync("firearm", qid, isNew, ct))
        {
            _firearms[qid] = firearm.Id;
            if (isNew && report.Sample.Count < 10)
            {
                report.Sample.Add(facts.Name);
            }
        }
    }

    private void WriteSpecs(ImportFields f, Variant v, FirearmFacts facts)
    {
        var key = $"variants/{v.Id}/";
        f.Set(key + "overallLengthMm", v.OverallLengthMm, facts.OverallLengthMm, x => v.OverallLengthMm = x);
        f.Set(key + "barrelLengthMm", v.BarrelLengthMm, facts.BarrelLengthMm, x => v.BarrelLengthMm = x);
        f.Set(key + "muzzleVelocityMps", v.MuzzleVelocityMps, facts.MuzzleVelocityMps, x => v.MuzzleVelocityMps = x);
        f.Set(key + "rateOfFireMinRpm", v.RateOfFireMinRpm, facts.RateOfFireMinRpm, x => v.RateOfFireMinRpm = x);
        f.Set(key + "rateOfFireMaxRpm", v.RateOfFireMaxRpm, facts.RateOfFireMaxRpm, x => v.RateOfFireMaxRpm = x);
        f.Set(key + "effectiveRangeM", v.EffectiveRangeM, facts.EffectiveRangeM, x => v.EffectiveRangeM = x);

        var specs = new Dictionary<string, string>(v.ExtraSpecs);
        foreach (var (name, value) in facts.ExtraSpecs)
        {
            f.Set(key + "extraSpecs", specs.GetValueOrDefault(name), value, x => specs[name] = x);
        }

        if (specs.Count != v.ExtraSpecs.Count || specs.Except(v.ExtraSpecs).Any())
        {
            v.ExtraSpecs = specs;
        }

        // Every cartridge becomes a chambering; it is the primary one only when the item names exactly one.
        var cartridges = facts.AmmunitionQids.Select(Resolve).Where(_cartridges.ContainsKey).Select(q => _cartridges[q]).ToList();
        if (cartridges.Count > 0 && f.CanAddTo($"chamberings/{v.Id}", v.Chamberings.Count))
        {
            var primary = facts.AmmunitionQids.Count == 1 && !v.Chamberings.Any(c => c.IsPrimary);
            foreach (var id in cartridges.Where(id => !v.Chamberings.Any(c => c.CartridgeId == id)))
            {
                v.Chamberings.Add(new VariantCartridge { CartridgeId = id, IsPrimary = primary });
            }
        }
    }

    /// <summary>"based on" becomes *derived from*, unless that would close a derivation loop.</summary>
    private async Task AddDerivationsAsync(string qid, IReadOnlyList<string> basedOn, CancellationToken ct)
    {
        if (!_firearms.TryGetValue(qid, out var id))
        {
            return;
        }

        var parents = basedOn.Select(Resolve).Where(q => q != qid && _firearms.ContainsKey(q)).Select(q => _firearms[q]).Distinct().ToList();
        if (parents.Count == 0)
        {
            return;
        }

        // Same lock as the admin relation writes, so an admin and the import cannot close a loop together.
        var ownTransaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminFirearmPartsEndpoints.DerivationWriteLock})", ct);
            var firearm = await db.Firearms.IgnoreQueryFilters().Include(f => f.RelationsFrom).SingleAsync(f => f.Id == id, ct);
            var f = new ImportFields(firearm, isNew: false);
            var added = parents.Where(p => !firearm.RelationsFrom.Any(r => r.ToFirearmId == p)).ToList();
            if (added.Count == 0 || !f.CanAddTo("relations", firearm.RelationsFrom.Count))
            {
                report.Count("relation", "unchanged");
                return;
            }

            if (await db.FindDerivationLoopAsync(id, added, ct) is { } loop)
            {
                report.Add("relation", qid, $"derived-from link would loop ({loop}); skipped");
                report.Count("relation", "skipped");
                return;
            }

            firearm.RelationsFrom.AddRange(added.Select(p => new FirearmRelation { ToFirearmId = p, Type = RelationType.DerivedFrom }));
            TouchIfChildrenChanged(firearm);
            if (await SaveAsync("relation", qid, isNew: true, ct) && ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(ct);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
            if (ownTransaction is not null)
            {
                await ownTransaction.DisposeAsync();
            }
        }
    }

    private int? CountryId(string? qid) => qid is not null && _countries.TryGetValue(Resolve(qid), out var id) ? id : null;

    /// <summary>The QID an item now lives under: claims can still point at an item that was merged away.</summary>
    private string Resolve(string qid) => _mergedInto.GetValueOrDefault(qid, qid);

    private async Task<Dictionary<string, WikidataEntity?>> FetchAsync(IEnumerable<string> qids, CancellationToken ct)
    {
        var result = await wikidata.GetEntitiesAsync(qids, ct);
        foreach (var (qid, item) in result.Where(r => r.Value is not null && r.Value.Qid != r.Key))
        {
            _mergedInto[qid] = item!.Qid;
        }

        return result;
    }

    /// <summary>
    /// Child rows (designers, tags, specs, chamberings, relations) do not change the firearm row, so its version
    /// would not be checked: touch it, so an admin edit made meanwhile (and the locks it set) wins with a conflict.
    /// Citations are add/remove entries that never bump the subject's version.
    /// </summary>
    private void TouchIfChildrenChanged(Firearm firearm)
    {
        if (db.ChangeTracker.Entries().Any(e => e.Entity != firearm && e.Entity is not Citation
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            db.Entry(firearm).Property(x => x.UpdatedAt).IsModified = true;
        }
    }

    private void Cite(Action<Citation> subject, bool needed, string qid)
    {
        if (needed)
        {
            var citation = new Citation { SourceId = _sourceId, Locator = Wd.ItemUrl(qid) };
            subject(citation);
            db.Citations.Add(citation);
        }
    }

    /// <summary>
    /// Saves one item. A clash with an admin's concurrent edit, or a unique value taken meanwhile, skips the item
    /// (the next run picks it up). Inside the dry-run transaction EF rolls back to a savepoint, so the run goes on.
    /// </summary>
    private async Task<bool> SaveAsync(string kind, string qid, bool isNew, CancellationToken ct)
    {
        try
        {
            var changed = db.ChangeTracker.HasChanges();
            await db.SaveChangesAsync(ct);
            report.Count(kind, !changed ? "unchanged" : isNew ? "created" : "updated");
            return true;
        }
        catch (DbUpdateException e)
        {
            report.Add(kind, qid, e is DbUpdateConcurrencyException
                ? "changed by an admin during the import; picked up by the next run"
                : $"not saved: {e.InnerException?.Message ?? e.Message}");
            report.Count(kind, "skipped");
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
