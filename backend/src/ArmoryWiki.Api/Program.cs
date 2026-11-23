using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Api.Features.Admin;
using ArmoryWiki.Api.Features.Admin.Firearms;
using ArmoryWiki.Api.Features.Admin.Media;
using ArmoryWiki.Api.Features.Admin.Resources;
using ArmoryWiki.Api.Features.Admin.Sources;
using ArmoryWiki.Api.Features.Cartridges;
using ArmoryWiki.Api.Features.Firearms;
using ArmoryWiki.Api.Features.Mechanisms;
using ArmoryWiki.Api.Features.Organizations;
using ArmoryWiki.Api.Features.People;
using ArmoryWiki.Api.Features.Reference;
using ArmoryWiki.Api.Import;
using ArmoryWiki.Infrastructure.Media;
using ArmoryWiki.Infrastructure.Persistence;
using ArmoryWiki.Infrastructure.Seed;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ArmoryWiki")
    ?? throw new InvalidOperationException("Connection string 'ArmoryWiki' is not configured. See CLAUDE.md.");
// Reads dominate, and projecting owned FuzzyDates needs no-tracking. Write paths opt in with AsTracking().
builder.Services.AddDbContext<ArmoryWikiDbContext>((services, o) =>
{
    ArmoryWikiDbContext.Configure(o, connectionString);
    o.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    o.AddInterceptors(services.GetRequiredService<RevalidationInterceptor>());
});
// Frontend cache tags made stale by saved changes (PLAN.md §5).
builder.Services.AddScoped<PendingRevalidation>();
builder.Services.AddScoped<RevalidationInterceptor>();
builder.Services.AddHttpClient<FrontendRevalidator>(c => c.Timeout = TimeSpan.FromSeconds(5));
// Image files: a Docker volume in production (Media:Root), relative paths resolved against the API project.
var mediaRoot = Path.GetFullPath(
    builder.Configuration["Media:Root"] ?? throw new InvalidOperationException("Media:Root is not configured."),
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(mediaRoot);
builder.Services.AddSingleton(new FileStore(mediaRoot));
builder.Services.AddProblemDetails();
builder.AddAdminAuth();
// Production keeps the keys behind auth and antiforgery cookies on a volume, so a redeploy does not sign the admin out.
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("ArmoryWiki");
}

// Behind Caddy: trust X-Forwarded-For/-Proto only from the compose network, so the login rate limit and lockout see
// real client addresses and cookies know the request was HTTPS (PLAN.md Phase 6).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    if (builder.Configuration["ForwardedHeaders:KnownNetwork"] is { Length: > 0 } network)
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
ValidationExtensions.ConfigureValidation();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Web defaults also read numbers from strings, which types every integer as "number | string" in OpenAPI.
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
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

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    // Production serves /media from the same volume through Caddy (PLAN.md Phase 6).
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(mediaRoot), RequestPath = "/media" });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (args is ["migrate"])
{
    // Production: `docker compose run --rm api migrate` before `up`; the API itself never migrates there.
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ArmoryWikiDbContext>();
    await db.Database.MigrateAsync();
    await ReferenceDataSeeder.SeedAsync(db);
    Console.WriteLine("Migrations applied and reference data seeded.");
    return;
}

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

if (ImportCommands.IsImportCommand(args))
{
    Environment.ExitCode = await ImportCommands.RunAsync(args, app.Services);
    return;
}

await AdminAuth.EnsureAdminAccountAsync(app.Services);

// Container health check (not routed by Caddy): the process answers and reaches the database.
app.MapGet("/healthz", async (ArmoryWikiDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct) ? Results.Ok("ok") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .ExcludeFromDescription();

// Server-side fetches are cached by Next.js; only endpoints the browser calls directly opt into public caching.
var api = app.MapGroup("/api").WithCacheControl("no-store");
api.MapFirearms();
api.MapPeople();
api.MapOrganizations();
api.MapCartridges();
api.MapMechanisms();
api.MapReference();

api.MapAdminAuth();
var admin = api.MapGroup("/admin").RequireAuthorization(AdminAuth.Policy).RequireAntiforgeryHeader().RevalidatesFrontend();
admin.MapAdminFirearms().MapAdminFirearmParts();
admin.MapAdminResources();
admin.MapAdminSources();
admin.MapAdminMedia();

app.Run();
