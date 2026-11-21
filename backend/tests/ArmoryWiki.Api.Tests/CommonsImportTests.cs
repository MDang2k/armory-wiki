using System.Text.Json;
using ArmoryWiki.Api.Import;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Media;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

public sealed class CommonsMapperTests
{
    [Theory]
    [InlineData("cc-by-sa-3.0", "CC-BY-SA-3.0")]
    [InlineData("cc-by-4.0", "CC-BY-4.0")]
    [InlineData("CC-BY-2.5", "CC-BY-2.5")]
    [InlineData("cc0", "CC0-1.0")]
    [InlineData("pd", "PD")]
    [InlineData("cc-by-sa-3.0-de", null)] // ported: a different license
    [InlineData("cc-by-1.0", null)]
    [InlineData("cc-by-nc-sa-4.0", null)]
    [InlineData("fal", null)]
    [InlineData(null, null)]
    public void Only_accepted_licenses_map(string? code, string? expected) => Assert.Equal(expected, CommonsMapper.License(code));

    [Fact]
    public void Author_html_becomes_plain_text()
    {
        const string html = """<bdi><a href="//commons.wikimedia.org/w/index.php?title=User:Allatur&amp;action=edit" class="new">Allatur</a></bdi> &amp; <span title="x">Co.</span>""";

        Assert.Equal("Allatur & Co.", CommonsMapper.PlainText(html));
    }

    [Fact]
    public void A_missing_author_is_only_acceptable_without_attribution()
    {
        var file = new CommonsFile("x.jpg", "https://upload.wikimedia.org/x.jpg", "https://commons.wikimedia.org/wiki/File:x.jpg", null, null, " <span></span> ");

        Assert.Equal("Unknown author", CommonsMapper.Author(file, "PD"));
        Assert.Null(CommonsMapper.Author(file, "CC-BY-SA-4.0"));
        Assert.Equal("Museum", CommonsMapper.Author(file with { ArtistHtml = "<i>Museum</i>" }, "CC-BY-4.0"));
    }

    [Fact]
    public void Titles_the_api_normalized_or_redirected_are_keyed_as_asked()
    {
        var response = JsonDocument.Parse("""
            {"continue":{"iistart":"x"},"query":{
              "normalized":[{"from":"File:A_b.jpg","to":"File:A b.jpg"},{"from":"File:old_name.jpg","to":"File:Old name.jpg"}],
              "redirects":[{"from":"File:Old name.jpg","to":"File:A b.jpg"}],
              "pages":[
                {"title":"File:A b.jpg","imageinfo":[{"url":"https://u/o.jpg","thumburl":"https://u/t.jpg","descriptionurl":"https://c/File:A_b.jpg",
                  "extmetadata":{"License":{"value":"cc0"}}}]},
                {"title":"File:Gone.jpg","missing":true},
                {"title":"File:Cut off.jpg"}]}}
            """).RootElement;

        var files = CommonsFile.ParseResponse(response);

        Assert.Equal(("A b.jpg", "https://u/t.jpg", "cc0"), (files["A_b.jpg"]!.Name, files["A_b.jpg"]!.DownloadUrl, files["A_b.jpg"]!.LicenseCode));
        Assert.Equal("A b.jpg", files["old_name.jpg"]!.Name); // a renamed file
        Assert.Null(files["Gone.jpg"]);
        Assert.False(files.ContainsKey("Cut off.jpg")); // left out of a truncated answer: asked for again
    }
}

/// <summary><c>import-commons</c> end to end against fixture metadata, on its own seeded database and file store.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class CommonsImportTests(DatabaseFixture fixture) : IDisposable
{
    private const string AkFile = "АК-47.jpg", GarandFile = "M1 Garand rifle - USA - 30-06 - Armémuseum.jpg";

    private readonly FakeWikidata _wikimedia = new();
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), "armorywiki-commons-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_mediaRoot))
        {
            Directory.Delete(_mediaRoot, recursive: true);
        }
    }

    private async Task<ImportReport> ImportAsync(DbContextOptions<ArmoryWikiDbContext> options, bool dryRun = false)
    {
        Directory.CreateDirectory(_mediaRoot);
        await using var db = new ArmoryWikiDbContext(options);
        var client = _wikimedia.Client();
        var store = new FileStore(_mediaRoot);
        return (await ImportCommands.RunAsync(db, CommonsImporter.Command, dryRun,
            r => new CommonsImporter(db, client, store, r, dryRun).RunAsync(CancellationToken.None), CancellationToken.None))!;
    }

    private static async Task<List<Image>> ImagesAsync(DbContextOptions<ArmoryWikiDbContext> options)
    {
        await using var db = new ArmoryWikiDbContext(options);
        return await db.Images.Include(i => i.Firearm).OrderBy(i => i.Id).ToListAsync();
    }

    [Fact]
    public async Task Copies_each_firearms_wikidata_image_with_its_attribution()
    {
        var options = await fixture.CreateSeededDatabaseAsync();

        var report = await ImportAsync(options);

        var images = await ImagesAsync(options);
        Assert.Equal(2, report.CountOf("image", "created")); // AK-47 and M1 Garand are the seeded firearms with fixture images
        var ak = images.Single(i => i.Firearm!.WikidataId == "Q37116");
        Assert.Equal((AkFile, "CC-BY-SA-3.0", "Allatur", 0), (ak.CommonsFileName, ak.License, ak.Author, ak.SortOrder));
        Assert.Equal("https://commons.wikimedia.org/wiki/File:%D0%90%D0%9A-47.jpg", ak.SourceUrl);
        var garand = images.Single(i => i.Firearm!.WikidataId == "Q204046");
        Assert.Equal(("PD", "Armémuseum (The Swedish Army Museum)"), (garand.License, garand.Author));
        Assert.All(images, i => Assert.True(File.Exists(Path.Combine(_mediaRoot, i.ContentHash, "thumb.webp"))));
        Assert.Contains(_wikimedia.Requests, r => r == Uri.UnescapeDataString(_wikimedia.CommonsFile(AkFile)["thumburl"]!.GetValue<string>())); // the 2560 px rendition
    }

    [Fact]
    public async Task A_second_run_adds_nothing_and_firearms_with_images_are_left_alone()
    {
        var options = await fixture.CreateSeededDatabaseAsync();
        await ImportAsync(options);
        var before = await ImagesAsync(options);

        var report = await ImportAsync(options);

        Assert.Equal(0, report.CountOf("image", "created"));
        Assert.Equal(before.Select(i => (i.Id, i.ContentHash)), (await ImagesAsync(options)).Select(i => (i.Id, i.ContentHash)));
    }

    [Fact]
    public async Task Unaccepted_licenses_and_missing_files_are_skipped_and_reported()
    {
        var options = await fixture.CreateSeededDatabaseAsync();
        _wikimedia.CommonsFile(GarandFile)["extmetadata"]!["License"]!["value"] = "cc-by-nc-4.0";
        _wikimedia.CommonsFile(GarandFile)["extmetadata"]!["LicenseShortName"]!["value"] = "CC BY-NC 4.0";
        var ak = _wikimedia.Entity("Q37116");
        ak["claims"]![Wd.Image]![0]!["mainsnak"]!["datavalue"]!["value"] = "Not on Commons.jpg";

        var report = await ImportAsync(options);

        Assert.Empty(await ImagesAsync(options));
        Assert.Contains(report.Issues, i => i.Qid == "Q204046" && i.Message.Contains("license not accepted (CC BY-NC 4.0)"));
        Assert.Contains(report.Issues, i => i.Qid == "Q37116" && i.Message.Contains("missing on Commons"));
    }

    [Fact]
    public async Task Files_that_are_not_images_are_skipped_and_leave_nothing_behind()
    {
        var options = await fixture.CreateSeededDatabaseAsync();
        foreach (var name in new[] { AkFile, GarandFile })
        {
            _wikimedia.Downloads[_wikimedia.CommonsFile(name)["thumburl"]!.GetValue<string>()] = TestImages.Gif();
        }

        var report = await ImportAsync(options);

        Assert.Empty(await ImagesAsync(options));
        Assert.Equal(2, report.CountOf("image", "skipped"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_mediaRoot));
    }

    [Fact]
    public async Task A_failed_download_skips_that_file_and_the_run_goes_on()
    {
        var options = await fixture.CreateSeededDatabaseAsync();
        _wikimedia.BrokenDownloads.Add(_wikimedia.CommonsFile(AkFile)["thumburl"]!.GetValue<string>());

        var report = await ImportAsync(options);

        Assert.Equal(GarandFile, Assert.Single(await ImagesAsync(options)).CommonsFileName);
        Assert.Contains(report.Issues, i => i.Qid == "Q37116" && i.Message.Contains("not usable: HTTP 404"));
    }

    [Fact]
    public async Task A_dry_run_counts_without_downloading_or_writing()
    {
        var options = await fixture.CreateSeededDatabaseAsync();

        var report = await ImportAsync(options, dryRun: true);

        Assert.Equal(2, report.CountOf("image", "created"));
        Assert.Empty(await ImagesAsync(options));
        Assert.DoesNotContain(_wikimedia.Requests, r => r.StartsWith("https://upload.wikimedia.org/", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_mediaRoot));
    }
}
