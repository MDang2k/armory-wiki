using ArmoryWiki.Api.Import;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

public sealed class WikipediaProseTests
{
    private static readonly ArticleProse Garand =
        WikipediaProse.Convert(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "wikipedia-m1-garand.html")));

    [Fact]
    public void The_lead_becomes_a_short_plain_summary()
    {
        Assert.NotNull(Garand.Summary);
        Assert.StartsWith("The M1 Garand", Garand.Summary);
        Assert.True(Garand.Summary.Length <= WikipediaProse.SummaryMaxLength);
        Assert.EndsWith(".", Garand.Summary);
        Assert.DoesNotContain("[", Garand.Summary); // no links or reference marks
    }

    [Fact]
    public void Sections_are_sorted_into_development_and_service_history()
    {
        Assert.Contains("## History", Garand.DevelopmentHistory);
        Assert.Contains("## Design details", Garand.DevelopmentHistory);
        Assert.Contains("## Users", Garand.ServiceHistory);
        foreach (var dropped in new[] { "## Pronunciation", "## Variants", "## Accessories", "## Civilian use", "## See also" })
        {
            Assert.DoesNotContain(dropped, Garand.DevelopmentHistory);
            Assert.DoesNotContain(dropped, Garand.ServiceHistory);
        }

        Assert.Contains("](https://en.wikipedia.org/wiki/", Garand.DevelopmentHistory); // article links kept
        Assert.DoesNotContain("<", Garand.DevelopmentHistory!.Replace(@"\<", "", StringComparison.Ordinal)); // no raw HTML
        Assert.DoesNotMatch(@"\\\[\d+\\\]", Garand.DevelopmentHistory); // no "[12]" reference marks
    }

    [Fact]
    public void Only_paragraphs_headings_lists_links_bold_and_italic_survive()
    {
        const string html = """
            <html><head><style>p { color: red }</style></head><body>
            <section><div role="note" class="hatnote">For other uses, see X.</div>
            <table class="infobox"><tr><td>Type: rifle</td></tr></table>
            <p class="mw-empty-elt"></p>
            <p>The <b>Test rifle</b> is a <a rel="mw:WikiLink" href="./Rifle" title="Rifle">rifle</a><sup class="mw-ref reference"><a href="#cite_note-1">[1]</a></sup>.</p></section>
            <section><h2 id="Operational_history">Operational history</h2>
            <p>Used <i> widely </i> in 1_000 *wars* <span data-x='a > b'>and</span> more.</p>
            <ul><li>First<ul><li>Nested</li></ul></li><li>Second</li></ul>
            <figure><img src="x.jpg"/><figcaption>A photo</figcaption></figure>
            <section><h3>Korea</h3><p>See <a rel="mw:ExtLink" href="https://example.org/a b">source</a>.</p></section></section>
            <section><h2>Design</h2><p>Gas operated, by <a rel="mw:WikiLink" class="new" href="./Ivan_Petrov?action=edit&amp;redlink=1">Ivan Petrov</a>, not <a rel="mw:WikiLink" href="./Blowback_(arms)">blowback</a>.</p><section><h3>Empty subsection</h3></section></section>
            <section><h2>Users</h2><div class="div-col"><ul><li>Finland</li></ul></div><section><h3>Former users</h3></section></section>
            <section><h2>See also</h2><ul><li>Other rifle</li></ul></section>
            </body></html>
            """;

        var prose = WikipediaProse.Convert(html);

        Assert.Equal("The Test rifle is a rifle.", prose.Summary);
        Assert.Equal(
            "## Operational history\n\nUsed *widely* in 1\\_000 \\*wars\\* and more.\n\n- First\n  - Nested\n- Second\n\n### Korea\n\nSee [source](https://example.org/a%20b).\n\n## Users\n\n- Finland",
            prose.ServiceHistory);
        // A red link keeps its text only; parentheses in a URL are encoded, as CommonMark ends a link at an unbalanced one.
        Assert.Equal("## Design\n\nGas operated, by Ivan Petrov, not [blowback](https://en.wikipedia.org/wiki/Blowback_%28arms%29).", prose.DevelopmentHistory);
    }

    [Fact]
    public void A_long_lead_is_cut_at_a_sentence()
    {
        var sentence = new string('a', 100) + ". ";
        var prose = WikipediaProse.Convert($"<p>{string.Concat(Enumerable.Repeat(sentence, 8))}</p>");

        Assert.Equal(string.Concat(Enumerable.Repeat(sentence, 4)).TrimEnd(), prose.Summary);
    }
}

/// <summary><c>import-wikipedia</c> end to end against the fixture article, on its own seeded database.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class WikipediaImportTests(DatabaseFixture fixture)
{
    private readonly FakeWikidata _wikimedia = new();

    private async Task<ImportReport> ImportAsync(DbContextOptions<ArmoryWikiDbContext> options, bool dryRun = false)
    {
        await using var db = new ArmoryWikiDbContext(options);
        var client = _wikimedia.Client();
        return (await ImportCommands.RunAsync(db, WikipediaImporter.Command, dryRun,
            r => new WikipediaImporter(db, client, r).RunAsync(CancellationToken.None), CancellationToken.None))!;
    }

    /// <summary>Points the seeded (published) M1 Garand and a new draft at the fixture article.</summary>
    private async Task<DbContextOptions<ArmoryWikiDbContext>> DatabaseAsync(Action<Firearm>? draft = null)
    {
        var options = await fixture.CreateSeededDatabaseAsync();
        await using var db = new ArmoryWikiDbContext(options);
        var garand = await db.Firearms.AsTracking().SingleAsync(f => f.Slug == "m1-garand");
        garand.WikipediaTitle = "M1 Garand";
        var copy = new Firearm
        {
            Slug = "garand-draft",
            Name = "Garand draft",
            WikipediaTitle = "M1 Garand",
            Summary = "Old summary.",
            DevelopmentHistory = "Old history.",
            Variants = [new Variant { Slug = "garand-draft", Name = "Garand draft", IsBaseVariant = true }],
        };
        draft?.Invoke(copy);
        db.Firearms.Add(copy);
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<Dictionary<string, Firearm>> FirearmsAsync(DbContextOptions<ArmoryWikiDbContext> options)
    {
        await using var db = new ArmoryWikiDbContext(options);
        return await db.Firearms.IgnoreQueryFilters().Where(f => f.WikipediaTitle != null).ToDictionaryAsync(f => f.Slug);
    }

    [Fact]
    public async Task Published_firearms_get_empty_fields_filled_and_drafts_are_refreshed()
    {
        var options = await DatabaseAsync();
        var seededSummary = (await FirearmsAsync(options))["m1-garand"].Summary;

        var report = await ImportAsync(options);

        var firearms = await FirearmsAsync(options);
        var garand = firearms["m1-garand"];
        Assert.Equal(seededSummary, garand.Summary); // published and already filled: kept
        Assert.Contains("## History", garand.DevelopmentHistory);
        Assert.Contains("## Users", garand.ServiceHistory);
        Assert.Equal((1386668058L, PublishStatus.Published), (garand.WikipediaRevisionId!.Value, garand.Status));
        var draft = firearms["garand-draft"];
        Assert.StartsWith("The M1 Garand", draft.Summary);
        Assert.Contains("## History", draft.DevelopmentHistory);
        Assert.Equal(2, report.CountOf("firearm", "updated"));
    }

    [Fact]
    public async Task Locked_prose_is_kept()
    {
        var options = await DatabaseAsync(d => d.LockedFields = ["developmentHistory"]);

        await ImportAsync(options);

        var draft = (await FirearmsAsync(options))["garand-draft"];
        Assert.Equal("Old history.", draft.DevelopmentHistory);
        Assert.StartsWith("The M1 Garand", draft.Summary);
    }

    [Fact]
    public async Task A_pinned_revision_leaves_the_firearm_alone()
    {
        var options = await DatabaseAsync(d => (d.LockedFields, d.WikipediaRevisionId) = (["wikipediaRevisionId"], 42));

        await ImportAsync(options);

        var draft = (await FirearmsAsync(options))["garand-draft"];
        Assert.Equal(("Old summary.", 42L), (draft.Summary, draft.WikipediaRevisionId!.Value));
    }

    [Fact]
    public async Task A_second_run_downloads_nothing_and_changes_nothing()
    {
        var options = await DatabaseAsync();
        await ImportAsync(options);
        _wikimedia.Requests.Clear();

        var report = await ImportAsync(options);

        Assert.Equal(2, report.CountOf("firearm", "unchanged"));
        Assert.DoesNotContain(_wikimedia.Requests, r => r.Contains("/with_html", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_article_that_cannot_be_read_is_skipped_and_the_run_goes_on()
    {
        var options = await DatabaseAsync(d => d.WikipediaTitle = "Broken article");
        _wikimedia.Articles["Broken article"] = (7, "unused");
        _wikimedia.BrokenArticles.Add("Broken article");

        var report = await ImportAsync(options);

        Assert.Contains(report.Issues, i => i.Message.Contains("\"Broken article\" (garand-draft): not read: HTTP 404"));
        var firearms = await FirearmsAsync(options);
        Assert.Equal("Old summary.", firearms["garand-draft"].Summary);
        Assert.Contains("## History", firearms["m1-garand"].DevelopmentHistory); // the other firearm was still imported
    }

    [Fact]
    public async Task A_title_the_api_does_not_list_counts_as_missing()
    {
        var options = await DatabaseAsync(d => d.WikipediaTitle = "de:MG 42");
        _wikimedia.UnlistedTitles.Add("de:MG 42"); // interwiki titles are reported apart from the pages

        var report = await ImportAsync(options);

        Assert.Contains(report.Issues, i => i.Message.Contains("\"de:MG 42\": no such English Wikipedia article"));
        Assert.Equal(1, report.CountOf("firearm", "updated"));
    }

    [Fact]
    public async Task Missing_articles_are_reported()
    {
        var options = await DatabaseAsync(d => d.WikipediaTitle = "No such article");

        var report = await ImportAsync(options);

        Assert.Contains(report.Issues, i => i.Message.Contains("\"No such article\": no such English Wikipedia article"));
        Assert.Equal("Old summary.", (await FirearmsAsync(options))["garand-draft"].Summary);
    }
}
