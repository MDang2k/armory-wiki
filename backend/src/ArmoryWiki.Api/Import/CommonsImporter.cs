using ArmoryWiki.Api.Features.Admin.Media;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Media;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// <c>import-commons</c> (PLAN.md Phase 4b): each firearm's Wikidata image(s), copied from Wikimedia Commons into
/// the file store with author, license and file page. Only firearms without any image get one, so an image an
/// admin removed or replaced is never brought back over theirs.
/// </summary>
// ponytail: "no images yet" stands in for an images lock; an admin who deletes a draft's only image gets the
// Commons one back on the next run. Add an "images" lock if that turns out to annoy.
public sealed class CommonsImporter(ArmoryWikiDbContext db, WikimediaClient wikimedia, FileStore store, ImportReport report, bool dryRun)
{
    public const string Command = "import-commons";

    public async Task RunAsync(CancellationToken ct)
    {
        var firearms = await db.Firearms.IgnoreQueryFilters().Where(f => f.WikidataId != null)
            .Select(f => new { f.Id, f.WikidataId, HasImages = f.Images.Any() })
            .ToListAsync(ct);
        foreach (var _ in firearms.Where(f => f.HasImages))
        {
            report.Count("firearm", "unchanged");
        }

        var wanted = firearms.Where(f => !f.HasImages).ToList();
        var items = await wikimedia.GetEntitiesAsync(wanted.Select(f => f.WikidataId!), ct);
        var imagesOf = wanted.ToDictionary(f => f.Id, f => items.GetValueOrDefault(f.WikidataId!)?.Strings(Wd.Image) ?? []);
        var files = await wikimedia.GetCommonsFilesAsync(imagesOf.Values.SelectMany(n => n), ct);
        var used = (await db.Images.Where(i => i.CommonsFileName != null).Select(i => i.CommonsFileName!).ToListAsync(ct)).ToHashSet();

        foreach (var firearm in wanted)
        {
            var added = 0;
            foreach (var name in imagesOf[firearm.Id])
            {
                if (await ImportAsync(firearm.Id, firearm.WikidataId!, name, files.GetValueOrDefault(name), added, used, ct))
                {
                    added++;
                }
            }

            report.Count("firearm", added > 0 ? "updated" : "unchanged");
        }
    }

    private async Task<bool> ImportAsync(
        int firearmId, string qid, string name, CommonsFile? file, int sortOrder, HashSet<string> used, CancellationToken ct)
    {
        bool Skip(string why)
        {
            report.Add("image", qid, $"{name}: {why}");
            report.Count("image", "skipped");
            return false;
        }

        if (file is null)
        {
            return Skip("missing on Commons");
        }

        if (CommonsMapper.License(file.LicenseCode) is not { } license)
        {
            return Skip($"license not accepted ({file.LicenseName ?? file.LicenseCode ?? "none"})");
        }

        if (CommonsMapper.Author(file, license) is not { } author)
        {
            return Skip("no author named, but the license requires attribution");
        }

        // CommonsFileName is unique: one Commons file illustrates one subject.
        if (!used.Add(file.Name))
        {
            return Skip("already used by another image");
        }

        if (dryRun)
        {
            report.Count("image", "created"); // ponytail: a dry run neither downloads nor decodes the files
            return true;
        }

        ProcessedImage processed;
        try
        {
            processed = ImageProcessor.Process(await wikimedia.DownloadAsync(file.DownloadUrl, ct));
        }
        catch (Exception e) when (e is InvalidImageException or HttpRequestException)
        {
            used.Remove(file.Name);
            return Skip($"not usable: {e.Message}");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdminMediaEndpoints.LockHashAsync(db, processed.Hash, ct);
        if (!store.Has(processed.Hash))
        {
            await store.WriteAsync(processed.Hash, processed.Files, ct);
        }

        db.Images.Add(new Image
        {
            FirearmId = firearmId,
            SortOrder = sortOrder,
            ContentHash = processed.Hash,
            OriginalExtension = processed.OriginalExtension,
            Width = processed.Width,
            Height = processed.Height,
            Author = author,
            License = license,
            SourceUrl = file.PageUrl,
            CommonsFileName = file.Name,
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            report.Count("image", "created");
            return true;
        }
        catch (Exception e)
        {
            // Never leave files no image uses: the firearm was deleted or the file attached elsewhere meanwhile
            // (skipped), or the database went away (rethrown).
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            await db.DeleteUnusedImageFilesAsync(store, [processed.Hash], CancellationToken.None);
            if (e is not DbUpdateException)
            {
                throw;
            }

            return Skip($"not saved: {e.InnerException?.Message ?? e.Message}");
        }
    }
}
