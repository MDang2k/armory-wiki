using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using ArmoryWiki.Api.Import;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>Answers the YouTube Data API from <c>Fixtures/youtube-uploads.json</c>. No live network.</summary>
public sealed class FakeYouTube : HttpMessageHandler
{
    public const string Key = "test-key";

    private readonly JsonNode _fixture =
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "youtube-uploads.json")))!;

    public List<Uri> Requests { get; } = [];
    /// <summary>Answers served before the fixture, in order (errors to recover from or fail on).</summary>
    public Queue<HttpResponseMessage> First { get; } = [];

    public YouTubeClient Client() => new(new HttpClient(this), Key, (_, _) => Task.CompletedTask);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add(uri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(Key, query["key"]);
        if (First.TryDequeue(out var first))
        {
            return Task.FromResult(first);
        }

        var body = uri.AbsolutePath.EndsWith("/channels", StringComparison.Ordinal)
            ? query["forHandle"] == ForgottenWeaponsImporter.ChannelHandle ? _fixture["channel"]! : new JsonObject { ["items"] = new JsonArray() }
            : _fixture["pages"]![query["pageToken"] ?? ""]!;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") });
    }
}

public sealed class VideoMatcherTests
{
    private static VideoMatcher.Candidate C(int id, float score) => new() { FirearmId = id, Score = score };

    [Fact]
    public void Near_miss_names_and_weak_matches_are_left_out()
    {
        Assert.Equal([10], VideoMatcher.Pick([C(10, 1f), C(11, 0.67f)])); // "AR-10" title, "AR-15" near miss
        Assert.Equal([1, 2], VideoMatcher.Pick([C(2, 1f), C(1, 1f)])); // "AK-47 vs M16"
        Assert.Empty(VideoMatcher.Pick([C(1, 0.55f)]));
        Assert.Empty(VideoMatcher.Pick([]));
    }

    [Fact]
    public void At_most_three_firearms_per_video() =>
        Assert.Equal([1, 2, 3], VideoMatcher.Pick([C(4, 0.9f), C(3, 0.95f), C(2, 1f), C(1, 1f)]));

    [Fact]
    public void Private_and_deleted_uploads_are_left_out_and_dates_are_utc()
    {
        var page = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "youtube-uploads.json")))
            .RootElement.GetProperty("pages").GetProperty("");

        var uploads = YouTubeClient.ParsePlaylistPage(page).ToList();

        Assert.Equal(["vidAK47aaaa", "vidGarand98"], uploads.Select(u => u.VideoId));
        Assert.Equal(new DateTime(2024, 3, 1, 15, 0, 0, DateTimeKind.Utc), uploads[0].PublishedAt); // published, not added to the playlist
        Assert.Equal(DateTimeKind.Utc, uploads[0].PublishedAt!.Value.Kind);
        Assert.Equal("Forgotten Weapons", uploads[0].Channel);
    }
}

/// <summary><c>import-fw-videos</c> end to end against the fixture uploads, on its own seeded database.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class ForgottenWeaponsImportTests(DatabaseFixture fixture)
{
    private readonly FakeYouTube _youtube = new();

    private async Task<ImportReport> ImportAsync(DbContextOptions<ArmoryWikiDbContext> options, bool dryRun = false)
    {
        await using var db = new ArmoryWikiDbContext(options);
        var client = _youtube.Client();
        return (await ImportCommands.RunAsync(db, ForgottenWeaponsImporter.Command, dryRun,
            r => new ForgottenWeaponsImporter(db, client, r).RunAsync(CancellationToken.None), CancellationToken.None))!;
    }

    /// <summary>A seeded database where some aliases could only mislead: too short, a type word, or shared.</summary>
    private async Task<DbContextOptions<ArmoryWikiDbContext>> DatabaseAsync()
    {
        var options = await fixture.CreateSeededDatabaseAsync();
        await using var db = new ArmoryWikiDbContext(options);
        var firearms = await db.Firearms.AsTracking().Where(f => f.Slug == "ak-47" || f.Slug == "akm" || f.Slug == "m4-carbine").ToDictionaryAsync(f => f.Slug);
        firearms["ak-47"].AlternativeNames = [.. firearms["ak-47"].AlternativeNames, "AK", "Kalashnikov"];
        firearms["akm"].AlternativeNames = [.. firearms["akm"].AlternativeNames, "Kalashnikov"];
        firearms["m4-carbine"].AlternativeNames = [.. firearms["m4-carbine"].AlternativeNames, "Carbine"];
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<List<(string Slug, string VideoId, VideoStatus Status)>> SuggestionsAsync(DbContextOptions<ArmoryWikiDbContext> options)
    {
        await using var db = new ArmoryWikiDbContext(options);
        return [.. (await db.Videos.IgnoreQueryFilters().Select(v => new { v.Firearm.Slug, v.YouTubeVideoId, v.Status }).ToListAsync())
            .Select(v => (v.Slug, v.YouTubeVideoId, v.Status)).OrderBy(v => v.YouTubeVideoId).ThenBy(v => v.Slug)];
    }

    [Fact]
    public async Task Uploads_are_suggested_for_the_firearms_their_titles_name()
    {
        var options = await DatabaseAsync();

        var report = await ImportAsync(options);

        Assert.Equal(
            [
                ("ak-47", "vidAK47aaaa", VideoStatus.Suggested),
                ("ak-74", "vidAK74smal", VideoStatus.Suggested), // not AK-47 ("AK"), AKM ("Kalashnikov") or M4 ("Carbine")
                ("ar-10", "vidAR10dutc", VideoStatus.Suggested),
                ("karabiner-98k", "vidGarand98", VideoStatus.Suggested),
                ("m1-garand", "vidGarand98", VideoStatus.Suggested),
            ],
            await SuggestionsAsync(options));
        Assert.Equal((5, 1), (report.CountOf("suggestion", "created"), report.CountOf("video", "unmatched"))); // the pinfire revolver
        Assert.Equal(5, report.Discovered); // the private video is not counted
        Assert.Equal(3, _youtube.Requests.Count); // channel + two pages

        await using var db = new ArmoryWikiDbContext(options);
        var video = await db.Videos.FirstAsync(v => v.YouTubeVideoId == "vidAK47aaaa");
        Assert.Equal(("The AK-47: Kalashnikov's Rifle in Detail", "Forgotten Weapons"), (video.Title, video.Channel));
        Assert.Equal(new DateTime(2024, 3, 1, 15, 0, 0, DateTimeKind.Utc), video.PublishedAt);
    }

    [Fact]
    public async Task Rejected_and_existing_videos_are_not_suggested_again()
    {
        var options = await DatabaseAsync();
        await ImportAsync(options);
        await using (var db = new ArmoryWikiDbContext(options))
        {
            var ak = await db.Videos.AsTracking().SingleAsync(v => v.YouTubeVideoId == "vidAK47aaaa");
            ak.Status = VideoStatus.Rejected;
            await db.SaveChangesAsync();
        }

        var report = await ImportAsync(options);

        Assert.Equal(0, report.CountOf("suggestion", "created"));
        Assert.Equal(5, report.CountOf("suggestion", "unchanged"));
        Assert.Contains(("ak-47", "vidAK47aaaa", VideoStatus.Rejected), await SuggestionsAsync(options));
    }

    [Fact]
    public async Task A_dry_run_counts_and_writes_nothing()
    {
        var options = await DatabaseAsync();

        var report = await ImportAsync(options, dryRun: true);

        Assert.Equal(5, report.CountOf("suggestion", "created"));
        Assert.Empty(await SuggestionsAsync(options));
    }

    [Fact]
    public async Task Transient_errors_are_retried_and_error_pages_name_their_status()
    {
        _youtube.First.Enqueue(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        _youtube.First.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        Assert.Equal(5, (await _youtube.Client().GetUploadsAsync(ForgottenWeaponsImporter.ChannelHandle, CancellationToken.None)).Count);

        _youtube.First.Enqueue(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>Bad gateway</html>") });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => _youtube.Client().GetUploadsAsync(ForgottenWeaponsImporter.ChannelHandle, CancellationToken.None));
        Assert.Equal("YouTube API: HTTP 502", error.Message);
        Assert.DoesNotContain(FakeYouTube.Key, error.Message);
    }

    [Fact]
    public async Task Without_an_api_key_the_command_stops_before_doing_anything()
    {
        var exit = await ImportCommands.RunAsync([ForgottenWeaponsImporter.Command], fixture.Factory.Services);

        Assert.Equal(1, exit);
    }
}
