using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// <c>import-wikipedia</c> (PLAN.md Phase 4c): summary, development history and service history for firearms with a
/// <c>WikipediaTitle</c>, converted from the article to Markdown (CC BY-SA, ADR-0001). <c>WikipediaRevisionId</c>
/// records the revision the prose came from, so it can be attributed. Firearms only for now.
/// </summary>
public sealed class WikipediaImporter(ArmoryWikiDbContext db, WikimediaClient wikimedia, ImportReport report)
{
    public const string Command = "import-wikipedia";

    public async Task RunAsync(CancellationToken ct)
    {
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        var firearms = await db.Firearms.IgnoreQueryFilters().Where(f => f.WikipediaTitle != null)
            .Select(f => new { f.Id, f.WikipediaTitle, f.WikipediaRevisionId }).ToListAsync(ct);
        var latest = await wikimedia.GetLatestRevisionsAsync(firearms.Select(f => f.WikipediaTitle!), ct);

        foreach (var f in firearms)
        {
            if (latest.GetValueOrDefault(f.WikipediaTitle!) is not { } revision)
            {
                report.Add("firearm", null, $"\"{f.WikipediaTitle}\": no such English Wikipedia article");
                report.Count("firearm", "skipped");
            }
            else if (revision.RevisionId == f.WikipediaRevisionId)
            {
                report.Count("firearm", "unchanged"); // already read this revision: nothing to download
            }
            else
            {
                await ImportAsync(f.Id, revision, ct);
            }
        }
    }

    private async Task ImportAsync(int firearmId, ArticleRevision revision, CancellationToken ct)
    {
        var firearm = await db.Firearms.IgnoreQueryFilters().SingleAsync(x => x.Id == firearmId, ct);
        var fields = new ImportFields(firearm, isNew: false);
        string Label() => $"\"{firearm.WikipediaTitle}\" ({firearm.Slug})";

        // The revision id attributes the prose: if an admin pinned it, the prose is theirs to manage.
        var anyWritable = fields.CanWrite("summary", firearm.Summary is null)
            || fields.CanWrite("developmentHistory", firearm.DevelopmentHistory is null)
            || fields.CanWrite("serviceHistory", firearm.ServiceHistory is null);
        if (firearm.LockedFields.Contains("wikipediaRevisionId") || !anyWritable)
        {
            db.ChangeTracker.Clear();
            report.Count("firearm", "unchanged"); // nothing the import may write: skip the download
            return;
        }

        ArticleProse prose;
        long revisionId;
        try
        {
            (revisionId, var html) = await wikimedia.GetArticleHtmlAsync(revision.Title, ct);
            prose = WikipediaProse.Convert(html);
        }
        catch (HttpRequestException e)
        {
            db.ChangeTracker.Clear();
            report.Add("firearm", null, $"{Label()}: not read: {e.Message}");
            report.Count("firearm", "skipped");
            return;
        }

        var wrote = fields.Set("summary", firearm.Summary, prose.Summary, v => firearm.Summary = v);
        wrote |= fields.Set("developmentHistory", firearm.DevelopmentHistory, prose.DevelopmentHistory, v => firearm.DevelopmentHistory = v);
        wrote |= fields.Set("serviceHistory", firearm.ServiceHistory, prose.ServiceHistory, v => firearm.ServiceHistory = v);
        // Record the revision read once any prose here came from Wikipedia (always on a draft, whose prose is the
        // import's), even when nothing changed, so the next run does not download the same article again.
        if (wrote || firearm.WikipediaRevisionId is not null || firearm.Status == Domain.PublishStatus.Draft)
        {
            firearm.WikipediaRevisionId = revisionId;
        }

        if (prose is { Summary: null, DevelopmentHistory: null, ServiceHistory: null })
        {
            report.Add("firearm", null, $"{Label()}: no lead paragraph and no history or service section");
        }

        try
        {
            var changed = db.ChangeTracker.HasChanges();
            await db.SaveChangesAsync(ct);
            report.Count("firearm", changed ? "updated" : "unchanged");
        }
        catch (DbUpdateException e)
        {
            report.Add("firearm", null, $"{Label()}: " + (e is DbUpdateConcurrencyException
                ? "changed by an admin during the import; picked up by the next run"
                : $"not saved: {e.InnerException?.Message ?? e.Message}"));
            report.Count("firearm", "skipped");
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
