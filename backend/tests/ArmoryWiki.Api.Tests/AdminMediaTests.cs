using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Admin.Media;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Api.Features.People;
using ArmoryWiki.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>Slice 3d through HTTP: image uploads, ordering, file sharing and cleanup, and videos.</summary>
[Collection(nameof(DatabaseCollection))]
public sealed class AdminMediaTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AdminClientAsync();

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        await using var db = fixture.NewDbContext();
        await db.Images.ExecuteDeleteAsync(); // the seed has none, so everything here is test data
        await db.Videos.IgnoreQueryFilters().ExecuteDeleteAsync();
        foreach (var folder in Directory.Exists(fixture.MediaRoot) ? Directory.GetDirectories(fixture.MediaRoot) : [])
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // Helpers ---------------------------------------------------------------------------------------------------

    private async Task<int> FirearmIdAsync(string slug)
    {
        await using var db = fixture.NewDbContext();
        return await db.Firearms.IgnoreQueryFilters().Where(f => f.Slug == slug).Select(f => f.Id).SingleAsync();
    }

    private async Task<int> PersonIdAsync(string slug)
    {
        await using var db = fixture.NewDbContext();
        return await db.People.IgnoreQueryFilters().Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync();
    }

    private Task<HttpResponseMessage> UploadAsync(string owner, int id, byte[] bytes, string fileName = "photo.png",
        string license = "CC-BY-SA-4.0", string? author = "Test Author", string caption = "Test caption")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(caption), "caption");
        if (author is not null)
        {
            form.Add(new StringContent(author), "author");
        }

        form.Add(new StringContent(license), "license");
        form.Add(new StringContent("https://commons.wikimedia.org/wiki/File:Test.png"), "sourceUrl");
        return _admin.PostAsync($"/api/admin/{owner}/{id}/images", form);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, DatabaseFixture.Json)!;
    }

    // Images ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_uploaded_image_is_stored_served_and_shown_with_its_attribution()
    {
        var ak47 = await FirearmIdAsync("ak-47");

        var uploaded = await ReadAsync<AdminImageDto>(await UploadAsync("firearms", ak47, TestImages.Png(1600, 800)), HttpStatusCode.Created);

        Assert.Equal((1600, 800), (uploaded.Image.Width, uploaded.Image.Height));
        Assert.Equal("CC BY-SA 4.0", uploaded.Image.License.Name);
        using var files = fixture.Client();
        foreach (var url in new[] { uploaded.Image.ThumbUrl, uploaded.Image.MediumUrl, uploaded.Image.OriginalUrl })
        {
            Assert.Equal(HttpStatusCode.OK, (await files.GetAsync(url)).StatusCode);
        }

        var page = await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47");
        var shown = Assert.Single(page.Images);
        Assert.Equal(("Test Author", "Test caption", "https://creativecommons.org/licenses/by-sa/4.0/"), (shown.Author, shown.Caption, shown.License.Url));

        var list = await fixture.GetJsonAsync<PagedResult<FirearmListItemDto>>("/api/firearms?q=kalashnikov");
        Assert.Equal(uploaded.Image.ThumbUrl, list.Items.Single(i => i.Slug == "ak-47").ThumbnailUrl);
    }

    [Theory]
    [InlineData("gif", "CC-BY-4.0", "Test Author", "Unusable image")]
    [InlineData("text", "CC-BY-4.0", "Test Author", "Unusable image")]
    [InlineData("png", "All rights reserved", "Test Author", null)]
    [InlineData("png", "CC-BY-4.0", null, null)]
    public async Task Unusable_files_and_missing_attribution_are_rejected(string kind, string license, string? author, string? title)
    {
        var bytes = kind switch
        {
            "gif" => TestImages.Gif(),
            "text" => "not an image"u8.ToArray(),
            _ => TestImages.Png(10, 10),
        };

        var response = await UploadAsync("firearms", await FirearmIdAsync("ak-47"), bytes, fileName: "looks-fine.jpg", license: license, author: author);

        var problem = await ReadAsync<JsonElement>(response, HttpStatusCode.BadRequest);
        if (title is not null)
        {
            Assert.Equal(title, problem.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task A_missing_or_oversized_file_is_a_validation_problem()
    {
        var ak47 = await FirearmIdAsync("ak-47");
        var noFile = new MultipartFormDataContent
        {
            { new StringContent("Test Author"), "author" },
            { new StringContent("CC0-1.0"), "license" },
            { new StringContent("https://example.org/x"), "sourceUrl" },
        };

        var missing = await _admin.PostAsync($"/api/admin/firearms/{ak47}/images", noFile);
        var oversized = await UploadAsync("firearms", ak47, new byte[(20 * 1024 * 1024) + 1]);

        Assert.True((await ReadAsync<JsonElement>(missing, HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("file", out _));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [Fact]
    public async Task Identical_files_share_storage_until_the_last_image_using_them_is_deleted()
    {
        var bytes = TestImages.Png(50, 50);
        var onFirearm = await ReadAsync<AdminImageDto>(await UploadAsync("firearms", await FirearmIdAsync("ak-47"), bytes), HttpStatusCode.Created);
        var onPerson = await ReadAsync<AdminImageDto>(await UploadAsync("people", await PersonIdAsync("mikhail-kalashnikov"), bytes), HttpStatusCode.Created);
        Assert.Equal(onFirearm.ContentHash, onPerson.ContentHash);
        Assert.Single((await fixture.GetJsonAsync<PersonDetailDto>("/api/people/mikhail-kalashnikov")).Images);

        using var files = fixture.Client();
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/admin/images/{onFirearm.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await files.GetAsync(onPerson.Image.ThumbUrl)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/admin/images/{onPerson.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await files.GetAsync(onPerson.Image.ThumbUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/admin/images/{onPerson.Id}")).StatusCode);
    }

    [Fact]
    public async Task Reordering_picks_the_thumbnail_and_details_can_be_edited()
    {
        var ak47 = await FirearmIdAsync("ak-47");
        var first = await ReadAsync<AdminImageDto>(await UploadAsync("firearms", ak47, TestImages.Png(30, 30)), HttpStatusCode.Created);
        var second = await ReadAsync<AdminImageDto>(await UploadAsync("firearms", ak47, TestImages.Png(31, 31)), HttpStatusCode.Created);
        Assert.Equal((0, 1), (first.SortOrder, second.SortOrder));

        var wrong = await _admin.PutAsJsonAsync($"/api/admin/firearms/{ak47}/images/order", new ImageOrderInput([second.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        await ReadAsync<List<AdminImageDto>>(await _admin.PutAsJsonAsync($"/api/admin/firearms/{ak47}/images/order", new ImageOrderInput([second.Id, first.Id])));

        var list = await fixture.GetJsonAsync<PagedResult<FirearmListItemDto>>("/api/firearms?q=kalashnikov");
        Assert.Equal(second.Image.ThumbUrl, list.Items.Single(i => i.Slug == "ak-47").ThumbnailUrl);

        var edited = await ReadAsync<AdminImageDto>(await _admin.PutAsJsonAsync($"/api/admin/images/{first.Id}",
            new ImageDetailsInput("New caption", "Someone Else", "CC0-1.0", "https://example.org/photo")));
        Assert.Equal(("New caption", "CC0 1.0"), (edited.Image.Caption, edited.Image.License.Name));
        var listed = await _admin.GetFromJsonAsync<List<AdminImageDto>>($"/api/admin/firearms/{ak47}/images", DatabaseFixture.Json);
        Assert.Equal([second.Id, first.Id], listed!.Select(i => i.Id));
    }

    [Fact]
    public async Task Deleting_a_person_removes_their_image_files()
    {
        var person = await ReadAsync<JsonElement>(await _admin.PostAsJsonAsync("/api/admin/people", new { fullName = "Test Photographed" }), HttpStatusCode.Created);
        var id = person.GetProperty("id").GetInt32();
        var image = await ReadAsync<AdminImageDto>(await UploadAsync("people", id, TestImages.Png(40, 40)), HttpStatusCode.Created);

        var deleted = await _admin.DeleteAsync($"/api/admin/people/{id}?version={person.GetProperty("version").GetUInt32()}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.Client().GetAsync(image.Image.ThumbUrl)).StatusCode);
    }

    // Videos ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Videos_added_by_link_are_approved_and_public()
    {
        var ak47 = await FirearmIdAsync("ak-47");

        var added = await ReadAsync<AdminVideoDto>(await _admin.PostAsJsonAsync($"/api/admin/firearms/{ak47}/videos",
            new { url = "https://youtu.be/dQw4w9WgXcQ?t=42", title = "Test AK-47 history", channel = "Test Channel", publishedAt = "2020-05-01T12:00:00+02:00" }),
            HttpStatusCode.Created);
        Assert.Equal(new DateTime(2020, 5, 1, 10, 0, 0, DateTimeKind.Utc), added.Video.PublishedAt?.ToUniversalTime());
        var again = await _admin.PostAsJsonAsync($"/api/admin/firearms/{ak47}/videos",
            new { url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", title = "Same video" });
        var notYouTube = await _admin.PostAsJsonAsync($"/api/admin/firearms/{ak47}/videos",
            new { url = "https://vimeo.com/123", title = "Elsewhere" });

        Assert.Equal((VideoStatus.Approved, "dQw4w9WgXcQ"), (added.Status, added.Video.YouTubeVideoId));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notYouTube.StatusCode);
        Assert.Equal("Test AK-47 history", Assert.Single((await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47")).Videos).Title);
    }

    [Fact]
    public async Task Suggested_videos_stay_hidden_until_approved()
    {
        await using var db = fixture.NewDbContext();
        var suggested = new Video
        {
            FirearmId = await FirearmIdAsync("ak-47"),
            YouTubeVideoId = "abcdefghijk",
            Title = "Test suggestion",
            Status = VideoStatus.Suggested,
        };
        db.Videos.Add(suggested);
        await db.SaveChangesAsync();
        Assert.Empty((await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47")).Videos);

        await ReadAsync<AdminVideoDto>(await _admin.PostAsync($"/api/admin/videos/{suggested.Id}/approve", null));
        Assert.Single((await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47")).Videos);

        await ReadAsync<AdminVideoDto>(await _admin.PostAsync($"/api/admin/videos/{suggested.Id}/reject", null));
        Assert.Empty((await fixture.GetJsonAsync<FirearmDetailDto>("/api/firearms/ak-47")).Videos);

        var all = await _admin.GetFromJsonAsync<List<AdminVideoDto>>($"/api/admin/firearms/{suggested.FirearmId}/videos", DatabaseFixture.Json);
        Assert.Equal(VideoStatus.Rejected, Assert.Single(all!).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/admin/videos/{suggested.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=x", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=short", null)]
    [InlineData("https://evil.example/watch?v=dQw4w9WgXcQ", null)]
    [InlineData("javascript:alert(1)", null)]
    public void YouTube_ids_are_read_only_from_youtube_links(string url, string? expected)
    {
        Assert.Equal(expected, YouTube.TryParseId(url, out var id) ? id : null);
    }
}
