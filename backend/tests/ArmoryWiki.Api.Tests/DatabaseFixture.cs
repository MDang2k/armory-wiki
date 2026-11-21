using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// One throwaway Postgres 17 per test run. Starting the app in Development applies migrations and runs the
/// seeders, exactly like local dev. On top of the seed it adds one draft firearm and two slug redirects, so
/// every public endpoint can be checked for leaking drafts.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string DraftSlug = "draft-carbine";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private DbContextOptions<ArmoryWikiDbContext> _options = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>A throwaway folder for uploaded image files, removed after the run.</summary>
    public string MediaRoot { get; } = Path.Combine(Path.GetTempPath(), "armorywiki-tests-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<ArmoryWikiDbContext>();
        ArmoryWikiDbContext.Configure(options, connectionString);
        _options = options.Options;

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:ArmoryWiki", connectionString);
            b.UseSetting("Admin:Email", AdminEmail);
            b.UseSetting("Admin:Password", AdminPassword);
            b.UseSetting("RateLimits:LoginPerMinute", "1000"); // every test logs in from the same address
            b.UseSetting("Media:Root", MediaRoot);
        });
        _ = Factory.Server; // boots the app: migrate + seed

        await AddDraftAndRedirectsAsync();
    }

    /// <summary>A context on the test database, owned by the caller.</summary>
    public ArmoryWikiDbContext NewDbContext() => new(_options);

    /// <summary>
    /// A separate, freshly migrated and seeded database in the same container, for tests (importers) that rewrite
    /// seeded rows other tests rely on.
    /// </summary>
    public async Task<DbContextOptions<ArmoryWikiDbContext>> CreateSeededDatabaseAsync()
    {
        var name = "scratch_" + Guid.NewGuid().ToString("N");
        await using (var main = NewDbContext())
        {
            var createDatabase = "CREATE DATABASE " + name; // a GUID: nothing to escape
            await main.Database.ExecuteSqlRawAsync(createDatabase);
        }

        var builder = new DbContextOptionsBuilder<ArmoryWikiDbContext>();
        ArmoryWikiDbContext.Configure(builder, new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString);
        await using var db = new ArmoryWikiDbContext(builder.Options);
        await db.Database.MigrateAsync();
        await Infrastructure.Seed.ReferenceDataSeeder.SeedAsync(db);
        await Infrastructure.Seed.DevelopmentSeeder.SeedAsync(db);
        return builder.Options;
    }

    public HttpClient Client(bool followRedirects = true) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });

    // Test-only credentials for the throwaway database.
    public const string AdminEmail = "admin@armorywiki.test";
    public const string AdminPassword = "Test-Admin-Password-1";

    /// <summary>HTTPS (so the Secure cookies flow), no auto-redirects, cookies kept between calls.</summary>
    public HttpClient HttpsClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    /// <summary>Logged in as the seeded admin, with the antiforgery header set for writes.</summary>
    public async Task<HttpClient> AdminClientAsync()
    {
        var client = HttpsClient();
        (await client.PostAsJsonAsync("/api/admin/auth/login", new { email = AdminEmail, password = AdminPassword })).EnsureSuccessStatusCode();
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/admin/auth/antiforgery")).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        return client;
    }

    public async Task<T> GetJsonAsync<T>(string path)
    {
        using var client = Client();
        return (await client.GetFromJsonAsync<T>(path, Json))!;
    }

    private async Task AddDraftAndRedirectsAsync()
    {
        await using var db = NewDbContext();
        var ak47 = await db.Firearms.SingleAsync(f => f.Slug == "ak-47");
        var draft = new Firearm
        {
            Slug = DraftSlug,
            Name = "Draft carbine",
            Status = PublishStatus.Draft,
            Summary = "Unreviewed import mentioning Kalashnikov.",
            CountryOfOriginId = ak47.CountryOfOriginId,
            OperatingMechanismId = ak47.OperatingMechanismId,
            DesignDate = FuzzyDate.InYear(1948),
            Tags = [await db.Tags.SingleAsync(t => t.Slug == "carbine")],
            Designers = [new FirearmDesigner { PersonId = await db.People.Where(p => p.Slug == "mikhail-kalashnikov").Select(p => p.Id).SingleAsync() }],
            Manufacturers = [new FirearmManufacturer { OrganizationId = await db.Organizations.Where(o => o.Slug == "colts-manufacturing-company").Select(o => o.Id).SingleAsync() }],
        };
        draft.Variants.Add(new Variant
        {
            Slug = DraftSlug,
            Name = "Draft carbine",
            IsBaseVariant = true,
            Chamberings = [new VariantCartridge { CartridgeId = await db.Cartridges.Where(c => c.Slug == "7-62x39mm").Select(c => c.Id).SingleAsync(), IsPrimary = true }],
        });
        db.Firearms.Add(draft);
        await db.SaveChangesAsync();

        db.FirearmRelations.Add(new FirearmRelation { FromFirearmId = draft.Id, ToFirearmId = ak47.Id, Type = RelationType.DerivedFrom });
        // A published firearm credited to a draft person: the credit must not appear anywhere.
        db.FirearmDesigners.Add(new FirearmDesigner
        {
            FirearmId = ak47.Id,
            Role = DesignerRole.CoDesigner,
            Person = new Person { Slug = "draft-person", FullName = "Draft person", Status = PublishStatus.Draft },
        });
        db.SlugRedirects.AddRange(
            new SlugRedirect { EntityType = SluggedEntity.Firearm, OldSlug = "kalashnikov-rifle", EntityId = ak47.Id },
            new SlugRedirect { EntityType = SluggedEntity.Firearm, OldSlug = "old-draft-carbine", EntityId = draft.Id });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        if (Directory.Exists(MediaRoot))
        {
            Directory.Delete(MediaRoot, recursive: true);
        }
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
