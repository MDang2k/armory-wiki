using ArmoryWiki.Infrastructure.Media;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// CLI subcommands (PLAN.md Phase 4): <c>dotnet run --project src/ArmoryWiki.Api -- import-wikidata [--dry-run]</c>.
/// </summary>
public static class ImportCommands
{
    public static bool IsImportCommand(string[] args) => args is [WikidataImporter.Command or CommonsImporter.Command, ..];

    public static async Task<int> RunAsync(string[] args, IServiceProvider services, CancellationToken ct = default)
    {
        var command = args[0];
        var dryRun = args.Contains("--dry-run");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArmoryWikiDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        using var http = WikimediaClient.CreateHttpClient(configuration["Import:Contact"] ?? WikimediaClient.DefaultContact);
        var client = new WikimediaClient(http);
        var store = scope.ServiceProvider.GetRequiredService<FileStore>();

        var report = await RunAsync(db, command, dryRun, r => command switch
        {
            WikidataImporter.Command => new WikidataImporter(db, client, r).RunAsync(ct),
            _ => new CommonsImporter(db, client, store, r, dryRun).RunAsync(ct),
        }, ct);
        if (report is null)
        {
            await Console.Error.WriteLineAsync($"{command} is already running.");
            return 1;
        }

        Console.Write(report.Summary());
        Console.WriteLine($"  {"report",-16} {await report.WriteAsync(Path.Combine(Directory.GetCurrentDirectory(), "import-reports"), ct)}");
        return 0;
    }

    /// <summary>
    /// Runs one importer under its own advisory lock (null when another run holds it). A dry run does the real
    /// work inside one transaction and rolls it back, so its counts are exact.
    /// </summary>
    public static async Task<ImportReport?> RunAsync(
        ArmoryWikiDbContext db, string command, bool dryRun, Func<ImportReport, Task> import, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct); // the session lock lives as long as this connection
        if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtext({command})) AS \"Value\"").SingleAsync(ct))
        {
            await db.Database.CloseConnectionAsync();
            return null;
        }

        try
        {
            var report = new ImportReport(command, dryRun);
            await using (var transaction = dryRun ? await db.Database.BeginTransactionAsync(ct) : null)
            {
                await import(report);
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(ct);
                }
            }

            return report;
        }
        finally
        {
            // A pooled connection keeps its session locks until it is reused, so a failed run lets go explicitly.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock(hashtext({command}))", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
}
