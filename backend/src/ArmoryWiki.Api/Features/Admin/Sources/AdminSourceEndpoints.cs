using System.Linq.Expressions;
using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Sources;

public sealed record SourceInput(SourceType? Type, string Title, string? Author, string? Publisher, int? Year, string? Url, string? Isbn);

public sealed record AdminSourceDto(int Id, uint Version, int CitationCount, SourceInput Details);

/// <summary>Cites the subject in the route; <see cref="FieldName"/> (an API field name) narrows it to one field.</summary>
public sealed record CitationInput(int SourceId, string? FieldName, string? Locator);

public sealed record AdminCitationDto(int Id, int SourceId, string SourceTitle, string? FieldName, string? Locator);

/// <summary>
/// Sources (GLOSSARY.md: *source*) and the citations that tie them to firearms, variants, people, organizations
/// and cartridges. Citations are plain add/remove entries: they don't change the subject's version.
/// </summary>
public static class AdminSourceEndpoints
{
    private sealed record Subject(
        string Route,
        string Title,
        Func<ArmoryWikiDbContext, int, CancellationToken, Task<bool>> ExistsAsync,
        Func<int, Expression<Func<Citation, bool>>> Of,
        Action<Citation, int> Attach);

    private static readonly Subject[] Subjects =
    [
        new("firearms", "Firearm", (db, id, ct) => db.Firearms.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => c => c.FirearmId == id, (c, id) => c.FirearmId = id),
        new("variants", "Variant", (db, id, ct) => db.Variants.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => c => c.VariantId == id, (c, id) => c.VariantId = id),
        new("people", "Person", (db, id, ct) => db.People.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => c => c.PersonId == id, (c, id) => c.PersonId = id),
        new("organizations", "Organization", (db, id, ct) => db.Organizations.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => c => c.OrganizationId == id, (c, id) => c.OrganizationId = id),
        new("cartridges", "Cartridge", (db, id, ct) => db.Cartridges.IgnoreQueryFilters().AnyAsync(x => x.Id == id, ct), id => c => c.CartridgeId == id, (c, id) => c.CartridgeId = id),
    ];

    public static void MapAdminSources(this RouteGroupBuilder admin)
    {
        var sources = admin.MapGroup("/sources").WithTags("Admin: sources");
        sources.MapGet("/", List);
        sources.MapGet("/{id:int}", Detail).ProducesProblem(StatusCodes.Status404NotFound);
        sources.MapPost("/", Create).Validate<SourceInput>();
        sources.MapPut("/{id:int}", Update).Validate<SourceInput>()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        sources.MapDelete("/{id:int}", Delete)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        var citations = admin.MapGroup("").WithTags("Admin: citations");
        foreach (var subject in Subjects)
        {
            citations.MapGet($"/{subject.Route}/{{id:int}}/citations",
                (int id, ArmoryWikiDbContext db, CancellationToken ct) => ListCitationsAsync(subject, id, db, ct))
                .ProducesProblem(StatusCodes.Status404NotFound);
            citations.MapPost($"/{subject.Route}/{{id:int}}/citations",
                (int id, CitationInput input, ArmoryWikiDbContext db, CancellationToken ct) => AddCitationAsync(subject, id, input, db, ct))
                .Validate<CitationInput>()
                .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        }

        citations.MapDelete("/citations/{citationId:int}", RemoveCitation).ProducesProblem(StatusCodes.Status404NotFound);
    }

    // Sources --------------------------------------------------------------------------------------------------

    private static readonly Expression<Func<Source, AdminSourceDto>> ToAdminSourceDto = s => new AdminSourceDto(
        s.Id,
        EF.Property<uint>(s, ArmoryWikiDbContext.Version),
        0,
        new SourceInput(s.Type, s.Title, s.Author, s.Publisher, s.Year, s.Url, s.Isbn));

    private static async Task<AdminSourceDto?> FindAsync(ArmoryWikiDbContext db, int id, CancellationToken ct)
    {
        var dto = await db.Sources.Where(s => s.Id == id).Select(ToAdminSourceDto).FirstOrDefaultAsync(ct);
        return dto is null ? null : dto with { CitationCount = await db.Citations.CountAsync(c => c.SourceId == id, ct) };
    }

    private static async Task<Ok<PagedResult<AdminSourceDto>>> List(string? q, int? page, int? pageSize, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var query = db.Sources.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Escape LIKE wildcards in the search text; backslash is Postgres's default escape character.
            var pattern = $"%{q.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";
            query = query.Where(s => EF.Functions.ILike(s.Title, pattern) || (s.Author != null && EF.Functions.ILike(s.Author, pattern)));
        }

        var paged = await query.OrderBy(s => s.Title).ThenBy(s => s.Id).ToPagedAsync(ToAdminSourceDto, page, pageSize, ct);
        var ids = paged.Items.Select(i => i.Id).ToList();
        var counts = await db.Citations.Where(c => ids.Contains(c.SourceId))
            .GroupBy(c => c.SourceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return TypedResults.Ok(paged with { Items = [.. paged.Items.Select(i => i with { CitationCount = counts.GetValueOrDefault(i.Id) })] });
    }

    private static async Task<Results<Ok<AdminSourceDto>, ProblemHttpResult>> Detail(int id, ArmoryWikiDbContext db, CancellationToken ct) =>
        await FindAsync(db, id, ct) is { } dto ? TypedResults.Ok(dto) : AdminProblems.NotFound("Source", id);

    private static async Task<Created<AdminSourceDto>> Create(SourceInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var source = new Source { Title = "" };
        Apply(source, input);
        db.Sources.Add(source);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/admin/sources/{source.Id}", (await FindAsync(db, source.Id, ct))!);
    }

    private static async Task<Results<Ok<AdminSourceDto>, ProblemHttpResult>> Update(
        int id, uint version, SourceInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var source = await db.Sources.AsTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null)
        {
            return AdminProblems.NotFound("Source", id);
        }

        if (db.VersionOf(source) is var current && current != version)
        {
            return AdminProblems.VersionConflict(current);
        }

        Apply(source, input);
        if (db.ChangeTracker.HasChanges() && await db.TrySaveAsync<Source>(id, ct) is { } conflict)
        {
            return conflict;
        }

        return TypedResults.Ok((await FindAsync(db, id, ct))!);
    }

    /// <summary>A source that is still cited cannot be deleted; remove its citations first.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(int id, uint version, ArmoryWikiDbContext db, CancellationToken ct)
    {
        var source = await db.Sources.AsTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source is null)
        {
            return AdminProblems.NotFound("Source", id);
        }

        if (db.VersionOf(source) is var current && current != version)
        {
            return AdminProblems.VersionConflict(current);
        }

        var citedBy = await db.Citations.IgnoreQueryFilters().Where(c => c.SourceId == id)
            .Select(c => c.Firearm != null ? "firearm " + c.Firearm.Slug
                : c.Variant != null ? "variant " + c.Variant.Firearm.Slug + "/" + c.Variant.Slug
                : c.Person != null ? "person " + c.Person.Slug
                : c.Organization != null ? "organization " + c.Organization.Slug
                : "cartridge " + c.Cartridge!.Slug)
            .Distinct()
            .ToListAsync(ct);
        if (citedBy.Count > 0)
        {
            return AdminProblems.Conflict("Source is cited", $"Remove its citations first. Cited by: {string.Join(", ", citedBy)}.");
        }

        db.Sources.Remove(source);
        return await db.TrySaveAsync<Source>(id, ct) is { } conflict ? conflict : TypedResults.NoContent();
    }

    private static void Apply(Source s, SourceInput i)
    {
        s.Type = i.Type!.Value;
        s.Title = i.Title;
        s.Author = i.Author;
        s.Publisher = i.Publisher;
        s.Year = i.Year;
        s.Url = i.Url;
        s.Isbn = i.Isbn;
    }

    // Citations ------------------------------------------------------------------------------------------------

    private static readonly Expression<Func<Citation, AdminCitationDto>> ToAdminCitationDto =
        c => new AdminCitationDto(c.Id, c.SourceId, c.Source.Title, c.FieldName, c.Locator);

    private static async Task<Results<Ok<List<AdminCitationDto>>, ProblemHttpResult>> ListCitationsAsync(
        Subject subject, int id, ArmoryWikiDbContext db, CancellationToken ct)
    {
        if (!await subject.ExistsAsync(db, id, ct))
        {
            return AdminProblems.NotFound(subject.Title, id);
        }

        return TypedResults.Ok(await db.Citations.Where(subject.Of(id)).OrderBy(c => c.Id).Select(ToAdminCitationDto).ToListAsync(ct));
    }

    private static async Task<Results<Created<AdminCitationDto>, ProblemHttpResult>> AddCitationAsync(
        Subject subject, int id, CitationInput input, ArmoryWikiDbContext db, CancellationToken ct)
    {
        if (!await subject.ExistsAsync(db, id, ct))
        {
            return AdminProblems.NotFound(subject.Title, id);
        }

        if (!await db.Sources.AnyAsync(s => s.Id == input.SourceId, ct))
        {
            return AdminProblems.BadRequest("Unknown source", $"No source has the id {input.SourceId}.");
        }

        var citation = new Citation { SourceId = input.SourceId, FieldName = input.FieldName, Locator = input.Locator };
        subject.Attach(citation, id);
        db.Citations.Add(citation);
        if (await db.TrySaveAsync<Citation>(null, ct) is { } conflict)
        {
            return conflict; // subject or source removed in the meantime
        }

        return TypedResults.Created(
            $"/api/admin/{subject.Route}/{id}/citations",
            await db.Citations.Where(c => c.Id == citation.Id).Select(ToAdminCitationDto).SingleAsync(ct));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveCitation(int citationId, ArmoryWikiDbContext db, CancellationToken ct) =>
        await db.Citations.Where(c => c.Id == citationId).ExecuteDeleteAsync(ct) > 0
            ? TypedResults.NoContent()
            : AdminProblems.NotFound("Citation", citationId);
}

public sealed class SourceInputValidator : AbstractValidator<SourceInput>
{
    public SourceInputValidator()
    {
        RuleFor(x => x.Type).NotNull().IsInEnum();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Author).MaximumLength(300);
        RuleFor(x => x.Publisher).MaximumLength(300);
        RuleFor(x => x.Year).InclusiveBetween(1, 2100);
        RuleFor(x => x.Url)
            .MaximumLength(2000)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .WithMessage("Url must be an absolute http(s) address.")
            .When(x => x.Url is not null);
        RuleFor(x => x.Isbn).Matches(@"^[0-9][0-9 -]{8,15}[0-9Xx]\z").When(x => x.Isbn is not null);
    }
}

public sealed class CitationInputValidator : AbstractValidator<CitationInput>
{
    public CitationInputValidator()
    {
        RuleFor(x => x.SourceId).GreaterThan(0);
        RuleFor(x => x.FieldName).MaximumLength(100).Matches(@"^[a-zA-Z0-9/]+\z").When(x => x.FieldName is not null);
        RuleFor(x => x.Locator).MaximumLength(500);
    }
}
