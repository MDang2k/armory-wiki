using ArmoryWiki.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace ArmoryWiki.Api.Tests;

/// <summary>
/// One throwaway Postgres 17 per test run. Starting the app in Development
/// applies migrations and runs the seeder, exactly like local dev.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private DbContextOptions<ArmoryWikiDbContext> _options = null!;

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

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
        });
        _ = Factory.Server; // boots the app: migrate + seed
    }

    /// <summary>A context on the test database, owned by the caller.</summary>
    public ArmoryWikiDbContext NewDbContext() => new(_options);

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
