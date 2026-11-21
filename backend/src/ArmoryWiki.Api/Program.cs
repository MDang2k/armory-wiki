using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Api.Features.Admin;
using ArmoryWiki.Api.Features.Admin.Firearms;
using ArmoryWiki.Api.Features.Admin.Resources;
using ArmoryWiki.Api.Features.Admin.Sources;
using ArmoryWiki.Api.Features.Cartridges;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Api.Features.Mechanisms;
using ArmoryWiki.Api.Features.Organizations;
using ArmoryWiki.Api.Features.People;
using ArmoryWiki.Api.Features.Reference;
using ArmoryWiki.Infrastructure.Persistence;
using ArmoryWiki.Infrastructure.Seed;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ArmoryWiki")
    ?? throw new InvalidOperationException("Connection string 'ArmoryWiki' is not configured. See CLAUDE.md.");
// Reads dominate, and projecting owned FuzzyDates needs no-tracking. Write paths opt in with AsTracking().
builder.Services.AddDbContext<ArmoryWikiDbContext>(o =>
{
    ArmoryWikiDbContext.Configure(o, connectionString);
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});
builder.Services.AddProblemDetails();
builder.AddAdminAuth();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
ValidationExtensions.ConfigureValidation();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(o => o.AddSchemaTransformer((schema, context, _) =>
{
    // [Flags] enums get no value list by default; the API sends FiringModes as a list of single values.
    if (context.JsonTypeInfo.Type == typeof(FiringModes))
    {
        schema.Enum = [.. Enum.GetNames<FiringModes>().Where(n => n != nameof(FiringModes.None)).Select(n => (JsonNode)n)];
    }

    return Task.CompletedTask;
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Development only: production runs migrations in a separate one-shot container (PLAN.md Phase 6).
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ArmoryWikiDbContext>();
    await db.Database.MigrateAsync();
    await ReferenceDataSeeder.SeedAsync(db); // production runs this in the migrate container too
    await DevelopmentSeeder.SeedAsync(db);
}

await AdminAuth.EnsureAdminAccountAsync(app.Services);

// Server-side fetches are cached by Next.js; only endpoints the browser calls directly opt into public caching.
var api = app.MapGroup("/api").WithCacheControl("no-store");
api.MapFirearms();
api.MapPeople();
api.MapOrganizations();
api.MapCartridges();
api.MapMechanisms();
api.MapReference();

api.MapAdminAuth();
var admin = api.MapGroup("/admin").RequireAuthorization(AdminAuth.Policy).RequireAntiforgeryHeader();
admin.MapAdminFirearms().MapAdminFirearmParts();
admin.MapAdminResources();
admin.MapAdminSources();

app.Run();
