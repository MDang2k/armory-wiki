using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Import;

/// <summary>
/// <c>import-fw-videos</c> (PLAN.md Phase 4d): Forgotten Weapons uploads matched to firearms by title and written as
/// <b>suggested</b> videos (GLOSSARY.md: *video*) for an admin to approve or reject. Only the title, channel and
/// publish date are stored; nothing is scraped and no text copied.
/// </summary>
public sealed class ForgottenWeaponsImporter(ArmoryWikiDbContext db, YouTubeClient youtube, ImportReport report)
{
    public const string Command = "import-fw-videos";
    public const string ChannelHandle = "@ForgottenWeapons";

    public async Task RunAsync(CancellationToken ct)
    {
        var uploads = await youtube.GetUploadsAsync(ChannelHandle, ct);
        report.Discovered = uploads.Count;
        // Every (firearm, video) pair ever written, rejected ones included: a rejected suggestion never comes back.
        var known = (await db.Videos.Select(v => new { v.FirearmId, v.YouTubeVideoId }).ToListAsync(ct))
            .Select(v => (v.FirearmId, v.YouTubeVideoId)).ToHashSet();

        foreach (var upload in uploads)
        {
            var matches = VideoMatcher.Pick(await CandidatesAsync(upload.Title, ct));
            if (matches.Count == 0)
            {
                report.Count("video", "unmatched");
                continue;
            }

            foreach (var firearmId in matches)
            {
                if (!known.Add((firearmId, upload.VideoId)))
                {
                    report.Count("suggestion", "unchanged");
                    continue;
                }

                db.Videos.Add(new Video
                {
                    FirearmId = firearmId,
                    YouTubeVideoId = upload.VideoId,
                    Title = upload.Title,
                    Channel = upload.Channel,
                    PublishedAt = upload.PublishedAt,
                    Status = VideoStatus.Suggested,
                });
                try
                {
                    await db.SaveChangesAsync(ct);
                    report.Count("suggestion", "created");
                }
                catch (DbUpdateException e)
                {
                    // The firearm was deleted, or an admin added the same video, meanwhile.
                    report.Add("suggestion", null, $"{upload.VideoId} \"{upload.Title}\": not saved: {e.InnerException?.Message ?? e.Message}");
                    report.Count("suggestion", "skipped");
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Firearms one of whose names occurs in the title, best first: pg_trgm's <c>word_similarity</c> scores how well a
    /// name matches some stretch of the title, case and punctuation aside. Only names that identify one firearm
    /// count: at least three letters or digits ("AK" would match every AK-74 title), not a type word ("Carbine"),
    /// and held by no other firearm ("Kalashnikov").
    /// </summary>
    // ponytail: one query per upload (a few thousand, each scanning every firearm name); fine at this size, use the
    // trigram index with the %> operator if it gets slow.
    private Task<List<VideoMatcher.Candidate>> CandidatesAsync(string title, CancellationToken ct) =>
        db.Database.SqlQuery<VideoMatcher.Candidate>($"""
            WITH names AS (
              SELECT f.id, lower(n) AS name
              FROM firearms f CROSS JOIN LATERAL unnest(array_prepend(f.name, f.alternative_names)) AS n),
            usable AS (
              SELECT DISTINCT id, name FROM names
              WHERE length(regexp_replace(name, '[^[:alnum:]]', '', 'g')) >= 3
                AND name NOT IN (SELECT lower(t.name) FROM tags t)
                AND name IN (SELECT name FROM names GROUP BY name HAVING count(DISTINCT id) = 1))
            SELECT id AS firearm_id, max(word_similarity(name, lower({title}))) AS score
            FROM usable
            WHERE word_similarity(name, lower({title})) >= {VideoMatcher.Threshold}
            GROUP BY id
            ORDER BY 2 DESC, id
            LIMIT 10
            """).ToListAsync(ct);
}

/// <summary>Which matched firearms a video is suggested for. Pure.</summary>
public static class VideoMatcher
{
    public const float Threshold = 0.6f;
    /// <summary>A match this far below the best one is a near-miss name ("AR-15" in an "AR-10" title), not a subject.</summary>
    public const float MaxBehindBest = 0.25f;
    public const int MaxPerVideo = 3;

    public sealed class Candidate
    {
        public int FirearmId { get; set; }
        public float Score { get; set; }
    }

    public static List<int> Pick(IReadOnlyCollection<Candidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var best = candidates.Max(c => c.Score);
        return [.. candidates
            .Where(c => c.Score >= Threshold && c.Score >= best - MaxBehindBest)
            .OrderByDescending(c => c.Score).ThenBy(c => c.FirearmId)
            .Take(MaxPerVideo)
            .Select(c => c.FirearmId)];
    }
}
