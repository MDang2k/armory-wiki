using ArmoryWiki.Api.Import;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// <c>import-wikidata</c> end to end against fixture items, each test on its own freshly seeded database
/// (imports rewrite seeded rows that other tests rely on).
/// </summary>
[Collection(nameof(DatabaseCollection))]
public sealed class WikidataImportTests(DatabaseFixture fixture)
{
    private readonly FakeWikidata _wikidata = new();

    private async Task<DbContextOptions<ArmoryWikiDbContext>> DatabaseAsync() => await fixture.CreateSeededDatabaseAsync();

    private async Task<ImportReport> ImportAsync(DbContextOptions<ArmoryWikiDbContext> options, bool dryRun = false)
    {
        await using var db = new ArmoryWikiDbContext(options);
        var client = _wikidata.Client();
        return (await ImportCommands.RunAsync(db, WikidataImporter.Command, dryRun, r => new WikidataImporter(db, client, r).RunAsync(CancellationToken.None), CancellationToken.None))!;
    }

    [Fact]
    public async Task Imports_new_firearms_as_drafts_and_matches_seeded_ones_by_qid()
    {
        var options = await DatabaseAsync();
        await using (var setup = new ArmoryWikiDbContext(options))
        {
            // Mannlicher M1895 is a subclass of "bolt-action rifle" (Q751162), which the seed does not map.
            setup.WikidataClassTagMaps.Add(new WikidataClassTagMap { Qid = "Q751162", TagId = await setup.Tags.Where(t => t.Slug == "rifle").Select(t => t.Id).SingleAsync() });
            await setup.SaveChangesAsync();
        }

        var report = await ImportAsync(options);

        await using var db = new ArmoryWikiDbContext(options);
        var firearms = await db.Firearms.IgnoreQueryFilters().Where(f => FakeWikidata.FixtureFirearms.Contains(f.WikidataId!))
            .Include(f => f.Tags)
            .Include(f => f.Variants).ThenInclude(v => v.Chamberings).ThenInclude(c => c.Cartridge)
            .Include(f => f.Designers).ThenInclude(d => d.Person)
            .Include(f => f.Manufacturers).ThenInclude(m => m.Organization)
            .Include(f => f.CountryOfOrigin)
            .AsSplitQuery().ToListAsync();
        Assert.Equal(3, firearms.Count); // AK-47 and M1 Garand were seeded: matched, not duplicated
        Assert.Equal(1, report.CountOf("firearm", "created"));
        Assert.Equal(["Mannlicher M1895"], report.Sample);

        var mannlicher = firearms.Single(f => f.WikidataId == "Q983967");
        Assert.Equal(PublishStatus.Draft, mannlicher.Status);
        Assert.Equal("mannlicher-m1895", mannlicher.Slug);
        Assert.Equal(["rifle"], mannlicher.Tags.Select(t => t.Slug));
        Assert.True(mannlicher.CountryOfOrigin!.IsHistorical); // Austria-Hungary, auto-created
        Assert.Equal("Q28513", mannlicher.CountryOfOrigin.WikidataId);
        var designer = Assert.Single(mannlicher.Designers);
        Assert.Equal(("Ferdinand Mannlicher", DesignerRole.LeadDesigner, PublishStatus.Draft), (designer.Person!.FullName, designer.Role, designer.Person.Status));
        Assert.NotEmpty(mannlicher.Manufacturers);
        // Two cartridges (8×50mmR, 8×56mmR): both chambered, neither primary until an admin picks one.
        var chamberings = Assert.Single(mannlicher.Variants).Chamberings;
        Assert.Equal(2, chamberings.Count);
        Assert.All(chamberings, c => Assert.False(c.IsPrimary));
        Assert.All(chamberings, c => Assert.Equal(PublishStatus.Draft, c.Cartridge.Status));
        Assert.True(await db.Citations.AnyAsync(c => c.FirearmId == mannlicher.Id && c.Locator == "https://www.wikidata.org/wiki/Q983967" && c.Source.Title == "Wikidata"));

        // Published and hand-checked: only empty fields were filled, status untouched.
        var ak47 = firearms.Single(f => f.WikidataId == "Q37116");
        Assert.Equal((PublishStatus.Published, "ak-47", "AK-47"), (ak47.Status, ak47.Slug, ak47.WikipediaTitle));
        var akBase = ak47.Variants.Single(v => v.IsBaseVariant);
        Assert.Equal("4.3 kg", akBase.ExtraSpecs[WikidataMapper.UnspecifiedWeight]);
        Assert.Single(akBase.Chamberings); // already had one: the published list is left alone

        var garand = firearms.Single(f => f.WikidataId == "Q204046");
        Assert.Single(garand.Designers); // John Garand, seeded, not duplicated
        Assert.Equal(1, await db.People.IgnoreQueryFilters().CountAsync(p => p.WikidataId == "Q1317174"));
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var options = await DatabaseAsync();
        await ImportAsync(options);
        var before = await SnapshotAsync(options);

        var report = await ImportAsync(options);

        foreach (var (kind, outcomes) in report.Counts)
        {
            Assert.True(outcomes.Keys.All(o => o is "unchanged"), $"{kind}: {string.Join(", ", outcomes)}");
        }

        Assert.Equal(before, await SnapshotAsync(options));
    }

    [Fact]
    public async Task Drafts_are_refreshed_published_content_is_only_filled_and_locks_hold()
    {
        var options = await DatabaseAsync();
        await ImportAsync(options);
        await using (var db = new ArmoryWikiDbContext(options))
        {
            var mannlicher = await db.Firearms.IgnoreQueryFilters().Include(f => f.Variants).SingleAsync(f => f.WikidataId == "Q983967");
            mannlicher.Name = "Edited draft name"; // not locked: refreshed
            mannlicher.DesignDate = FuzzyDate.InYear(1901);
            mannlicher.LockedFields.Add("designDate"); // locked: kept
            var ak47 = await db.Firearms.Include(f => f.Variants).SingleAsync(f => f.WikidataId == "Q37116");
            ak47.DesignDate = FuzzyDate.InYear(1946); // published, not locked: still kept
            ak47.Variants.Single(v => v.IsBaseVariant).MuzzleVelocityMps = 700;
            await db.SaveChangesAsync();
        }

        await ImportAsync(options);

        await using var check = new ArmoryWikiDbContext(options);
        var after = await check.Firearms.IgnoreQueryFilters().Include(f => f.Variants)
            .Where(f => f.WikidataId == "Q983967" || f.WikidataId == "Q37116").ToDictionaryAsync(f => f.WikidataId!);
        Assert.Equal("Mannlicher M1895", after["Q983967"].Name);
        Assert.Equal(1901, after["Q983967"].DesignDate!.Year);
        Assert.Equal(1946, after["Q37116"].DesignDate!.Year);
        Assert.Equal(700, after["Q37116"].Variants.Single(v => v.IsBaseVariant).MuzzleVelocityMps);
        Assert.Equal(PublishStatus.Published, after["Q37116"].Status);
    }

    [Fact]
    public async Task A_dry_run_reports_exact_counts_and_writes_nothing()
    {
        var options = await DatabaseAsync();
        var before = await SnapshotAsync(options);

        var report = await ImportAsync(options, dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(1, report.CountOf("firearm", "created"));
        Assert.True(report.CountOf("person", "created") > 0);
        Assert.Equal(before, await SnapshotAsync(options));
    }

    [Fact]
    public async Task Merged_items_move_their_qid_and_deleted_ones_are_only_reported()
    {
        var options = await DatabaseAsync();
        await using (var db = new ArmoryWikiDbContext(options))
        {
            db.Firearms.AddRange(
                new Firearm { Slug = "old-mannlicher", Name = "Old Mannlicher", WikidataId = "Q1000001", Variants = [new Variant { Slug = "old-mannlicher", Name = "Old Mannlicher", IsBaseVariant = true }] },
                new Firearm { Slug = "deleted-item", Name = "Deleted item", WikidataId = "Q1000002", Variants = [new Variant { Slug = "deleted-item", Name = "Deleted item", IsBaseVariant = true }] });
            await db.SaveChangesAsync();
        }

        _wikidata.Redirects["Q1000001"] = "Q983967";
        var report = await ImportAsync(options);

        await using var check = new ArmoryWikiDbContext(options);
        var merged = await check.Firearms.IgnoreQueryFilters().SingleAsync(f => f.Slug == "old-mannlicher");
        Assert.Equal("Q983967", merged.WikidataId);
        Assert.Equal(1, await check.Firearms.IgnoreQueryFilters().CountAsync(f => f.WikidataId == "Q983967"));
        Assert.Equal("Q1000002", (await check.Firearms.IgnoreQueryFilters().SingleAsync(f => f.Slug == "deleted-item")).WikidataId);
        Assert.Contains(report.Issues, i => i.Qid == "Q1000002" && i.Message.StartsWith("deleted on Wikidata"));
        Assert.Contains(report.Issues, i => i.Qid == "Q1000001" && i.Message.Contains("QID updated"));
    }

    [Fact]
    public async Task Claims_pointing_at_merged_items_still_link_and_stay_idempotent()
    {
        var options = await DatabaseAsync();
        var mannlicher = _wikidata.Entity("Q983967");
        SetItemClaim(mannlicher, Wd.DesignedBy, "Q9000001");
        SetItemClaim(mannlicher, Wd.CountryOfOrigin, "Q9000002");
        _wikidata.Redirects["Q9000001"] = "Q113057"; // Ferdinand Mannlicher
        _wikidata.Redirects["Q9000002"] = "Q28513"; // Austria-Hungary

        await ImportAsync(options);
        var second = await ImportAsync(options);

        Assert.All(second.Counts, c => Assert.True(c.Value.Keys.All(o => o is "unchanged"), $"{c.Key}: {string.Join(", ", c.Value)}"));
        await using var db = new ArmoryWikiDbContext(options);
        var firearm = await db.Firearms.IgnoreQueryFilters().Include(f => f.Designers).ThenInclude(d => d.Person).Include(f => f.CountryOfOrigin)
            .SingleAsync(f => f.WikidataId == "Q983967");
        Assert.Equal("Q113057", Assert.Single(firearm.Designers).Person!.WikidataId);
        Assert.Equal("Q28513", firearm.CountryOfOrigin!.WikidataId);
        Assert.Equal(1, await db.Countries.CountAsync(c => c.WikidataId == "Q28513"));
    }

    private static void SetItemClaim(System.Text.Json.Nodes.JsonObject entity, string property, string qid)
    {
        var statement = entity["claims"]![property]![0]!;
        entity["claims"]![property] = new System.Text.Json.Nodes.JsonArray(statement.DeepClone());
        entity["claims"]![property]![0]!["mainsnak"]!["datavalue"]!["value"]!["id"] = qid;
    }

    [Fact]
    public async Task Items_without_an_english_label_are_skipped_and_reported()
    {
        var options = await DatabaseAsync();
        var mannlicher = _wikidata.Entity("Q983967");
        mannlicher["labels"] = new System.Text.Json.Nodes.JsonObject();

        var report = await ImportAsync(options);

        Assert.Contains(report.Issues, i => i.Qid == "Q983967" && i.Message.StartsWith("no English label"));
        await using var db = new ArmoryWikiDbContext(options);
        Assert.False(await db.Firearms.IgnoreQueryFilters().AnyAsync(f => f.WikidataId == "Q983967"));
    }

    [Fact]
    public async Task A_second_concurrent_run_is_refused()
    {
        var options = await DatabaseAsync();
        await using var holder = new ArmoryWikiDbContext(options);
        await holder.Database.OpenConnectionAsync();
        await holder.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock(hashtext({WikidataImporter.Command}))");

        await using var db = new ArmoryWikiDbContext(options);
        Assert.Null(await ImportCommands.RunAsync(db, WikidataImporter.Command, dryRun: false, _ => throw new InvalidOperationException("must not run"), CancellationToken.None));
        Assert.Empty(_wikidata.Requests);
    }

    /// <summary>Row counts and row versions of everything an import writes.</summary>
    private static async Task<string> SnapshotAsync(DbContextOptions<ArmoryWikiDbContext> options)
    {
        await using var db = new ArmoryWikiDbContext(options);
        var rows = await db.Database.SqlQueryRaw<string>("""
            SELECT string_agg(t || ':' || n, ' ') AS "Value" FROM (
              SELECT 'firearms' t, count(*) || '/' || sum(xmin::text::bigint) n FROM firearms
              UNION ALL SELECT 'variants', count(*) || '/' || sum(xmin::text::bigint) FROM variants
              UNION ALL SELECT 'people', count(*) || '/' || sum(xmin::text::bigint) FROM people
              UNION ALL SELECT 'organizations', count(*) || '/' || sum(xmin::text::bigint) FROM organizations
              UNION ALL SELECT 'cartridges', count(*) || '/' || sum(xmin::text::bigint) FROM cartridges
              UNION ALL SELECT 'countries', count(*)::text FROM countries
              UNION ALL SELECT 'citations', count(*)::text FROM citations
              UNION ALL SELECT 'designers', count(*)::text FROM firearm_designers
              UNION ALL SELECT 'manufacturers', count(*)::text FROM firearm_manufacturers
              UNION ALL SELECT 'chamberings', count(*)::text FROM variant_cartridges
              UNION ALL SELECT 'tags', count(*)::text FROM firearm_tags
              UNION ALL SELECT 'relations', count(*)::text FROM firearm_relations) s
            """).SingleAsync();
        return rows;
    }
}
