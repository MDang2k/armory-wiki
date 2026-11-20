using ArmoryWiki.Infrastructure.Persistence;
using ArmoryWiki.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ArmoryWiki")
    ?? throw new InvalidOperationException("Connection string 'ArmoryWiki' is not configured. See CLAUDE.md.");
builder.Services.AddDbContext<ArmoryWikiDbContext>(o => ArmoryWikiDbContext.Configure(o, connectionString));
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Development only: production runs migrations in a separate one-shot container (PLAN.md Phase 6).
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ArmoryWikiDbContext>();
    await db.Database.MigrateAsync();
    await ReferenceDataSeeder.SeedAsync(db); // production runs this in the migrate container too
    await DevelopmentSeeder.SeedAsync(db);
}

app.Run();
