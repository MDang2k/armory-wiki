using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Api.Features.Admin.Resources;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Tests;

/// <summary>Records what the API asks the frontend to revalidate instead of sending it anywhere.</summary>
public sealed class RecordingRevalidations
{
    public sealed record Call(string? Secret, string[] Tags);

    public ConcurrentQueue<Call> Calls { get; } = new();

    /// <summary>When set, the frontend is "down": every call fails.</summary>
    public bool Down { get; set; }

    public HttpMessageHandler Handler() => new Recorder(this);

    /// <summary>Every tag sent in calls that mention <paramref name="marker"/> (tests run concurrently).</summary>
    public string[] TagsWith(string marker) =>
        [.. Calls.Where(c => c.Tags.Any(t => t.Contains(marker, StringComparison.Ordinal))).SelectMany(c => c.Tags).Distinct().Order()];

    private sealed class Recorder(RecordingRevalidations owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (owner.Down)
            {
                throw new HttpRequestException("Connection refused");
            }

            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var secret = request.Headers.TryGetValues(FrontendRevalidator.SecretHeader, out var values) ? values.Single() : null;
            owner.Calls.Enqueue(new Call(secret, [.. body.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!)]));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

[Collection(nameof(DatabaseCollection))]
public sealed class RevalidationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private HttpClient _admin = null!;

    public async Task InitializeAsync() => _admin = await fixture.AdminClientAsync();

    public async Task DisposeAsync()
    {
        fixture.Revalidations.Down = false;
        _admin.Dispose();
        await using var db = fixture.NewDbContext();
        await db.SlugRedirects.Where(r => r.OldSlug.StartsWith("test-reval")).ExecuteDeleteAsync();
        await db.People.IgnoreQueryFilters().Where(x => x.Slug.StartsWith("test-reval")).ExecuteDeleteAsync();
    }

    private async Task<AdminEntityDto<PersonInput>> CreatePersonAsync(string name)
    {
        var response = await _admin.PostAsJsonAsync("/api/admin/people", new { fullName = name }, DatabaseFixture.Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminEntityDto<PersonInput>>(DatabaseFixture.Json))!;
    }

    [Fact]
    public async Task An_admin_write_expires_the_entity_and_its_type()
    {
        await CreatePersonAsync("Test Reval Created");

        Assert.Equal(["people", "person:test-reval-created"], fixture.Revalidations.TagsWith("test-reval-created"));
        Assert.All(fixture.Revalidations.Calls, c => Assert.Equal(DatabaseFixture.RevalidateSecret, c.Secret));
    }

    [Fact]
    public async Task A_rename_also_expires_the_old_slug()
    {
        var person = await CreatePersonAsync("Test Reval Old");

        var renamed = await _admin.PutAsJsonAsync(
            $"/api/admin/people/{person.Id}?version={person.Version}", person.Details with { Slug = "test-reval-new" }, DatabaseFixture.Json);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(["people", "person:test-reval-new", "person:test-reval-old"], fixture.Revalidations.TagsWith("test-reval-new"));
    }

    [Fact]
    public async Task A_failed_write_expires_nothing()
    {
        var person = await CreatePersonAsync("Test Reval Stale");

        var stale = await _admin.PutAsJsonAsync(
            $"/api/admin/people/{person.Id}?version={person.Version + 1}", person.Details with { Slug = "test-reval-never" }, DatabaseFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Empty(fixture.Revalidations.TagsWith("test-reval-never"));
    }

    [Fact]
    public async Task The_frontend_being_down_never_fails_the_write()
    {
        fixture.Revalidations.Down = true;

        var person = await CreatePersonAsync("Test Reval Down");

        Assert.Equal("test-reval-down", person.Details.Slug);
    }

    [Fact]
    public async Task Public_reads_expire_nothing()
    {
        var before = fixture.Revalidations.Calls.Count;

        (await fixture.Client().GetAsync("/api/firearms/ak-47")).EnsureSuccessStatusCode();

        Assert.Equal(before, fixture.Revalidations.Calls.Count);
    }
}
