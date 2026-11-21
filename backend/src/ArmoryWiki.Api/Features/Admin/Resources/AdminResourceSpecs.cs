using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;
using ArmoryWiki.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ArmoryWiki.Api.Features.Admin.Resources;

// Inputs: the same shapes GET returns in `details`. References to other entities are slugs.

public sealed record PersonInput(string? Slug, string FullName, FuzzyDateDto? BirthDate, FuzzyDateDto? DeathDate, string? Nationality, string? Biography, string? WikidataId);

public sealed record OrganizationInput(string? Slug, string Name, OrganizationType? Type, string? Country, FuzzyDateDto? Founded, string? Description, string? WikidataId);

public sealed record CartridgeInput(
    string? Slug,
    string Name,
    IReadOnlyList<string>? AlternativeNames,
    string? WikidataId,
    CartridgeType? Type,
    decimal? BulletDiameterMm,
    decimal? CaseLengthMm,
    decimal? OverallLengthMm,
    string? Parent,
    FuzzyDateDto? DesignDate,
    string? Country,
    string? Description);

public sealed record CountryInput(string? Slug, string Name, string? IsoCode, bool IsHistorical, string? WikidataId);

public sealed record TagInput(string? Slug, string Name, TagGroup Group);

/// <summary>Leave <see cref="Parent"/> empty for a top-level mechanism.</summary>
public sealed record MechanismInput(string? Slug, string Name, string? Parent, string? Explanation);

/// <summary>The admin resources besides firearms, each a <see cref="ResourceSpec{TEntity,TInput,TRefs}"/> on the shared lifecycle.</summary>
internal static class AdminResourceSpecs
{
    public static void MapAdminResources(this RouteGroupBuilder admin)
    {
        admin.MapResource("people", People);
        admin.MapResource("organizations", Organizations);
        admin.MapResource("cartridges", Cartridges);
        admin.MapResource("countries", Countries);
        admin.MapResource("tags", Tags);
        admin.MapResource("mechanisms", Mechanisms);
    }

    private static async Task<Resolved<int?>> CountryAsync(ArmoryWikiDbContext db, string? slug, CancellationToken ct)
    {
        if (slug is null)
        {
            return new(null, null);
        }

        var id = await db.Countries.Where(c => c.Slug == slug).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        return id is null ? new(null, AdminProblems.UnknownReference("country", [slug])) : new(id, null);
    }

    private static void SetNames(Changes c, IReadOnlyList<string>? before, IReadOnlyList<string>? after, Action<List<string>> apply)
    {
        if (!(before ?? []).SequenceEqual(after ?? []))
        {
            apply([.. after ?? []]);
            c.Mark("alternativeNames");
        }
    }

    // People ---------------------------------------------------------------------------------------------------

    private static readonly ResourceSpec<Person, PersonInput, int?> People = new()
    {
        Title = "Person",
        SlugType = SluggedEntity.Person,
        HasPublicPage = true,
        Set = db => db.People,
        Include = q => q.Include(p => p.Nationality),
        NameOf = p => p.FullName,
        ToInput = p => new(p.Slug, p.FullName, FuzzyDateDto.From(p.BirthDate), FuzzyDateDto.From(p.DeathDate), p.Nationality?.Slug, p.Biography, p.WikidataId),
        Blank = new(null, "", null, null, null, null, null),
        SlugOf = i => i.Slug,
        InputName = i => i.FullName,
        WithSlug = (i, slug) => i with { Slug = slug },
        WikidataIdOf = i => i.WikidataId,
        New = slug => new Person { Slug = slug, FullName = "", Status = PublishStatus.Draft },
        ResolveAsync = (db, i, _, ct) => CountryAsync(db, i.Nationality, ct),
        Apply = (p, b, i, nationalityId, c) =>
        {
            c.Set("slug", b.Slug, i.Slug, () => p.Slug = i.Slug!);
            c.Set("fullName", b.FullName, i.FullName, () => p.FullName = i.FullName);
            c.Set("birthDate", b.BirthDate, i.BirthDate, () => p.BirthDate = i.BirthDate.ToEntity());
            c.Set("deathDate", b.DeathDate, i.DeathDate, () => p.DeathDate = i.DeathDate.ToEntity());
            c.Set("nationality", b.Nationality, i.Nationality, () => p.NationalityId = nationalityId);
            c.Set("biography", b.Biography, i.Biography, () => p.Biography = i.Biography);
            c.Set("wikidataId", b.WikidataId, i.WikidataId, () => p.WikidataId = i.WikidataId);
        },
        ReferencedByAsync = (db, id, ct) => db.FirearmDesigners.IgnoreQueryFilters()
            .Where(d => d.PersonId == id).Select(d => "firearm " + d.Firearm.Slug).Distinct().ToListAsync(ct),
    };

    // Organizations --------------------------------------------------------------------------------------------

    private static readonly ResourceSpec<Organization, OrganizationInput, int?> Organizations = new()
    {
        Title = "Organization",
        SlugType = SluggedEntity.Organization,
        HasPublicPage = true,
        Set = db => db.Organizations,
        Include = q => q.Include(o => o.Country),
        NameOf = o => o.Name,
        ToInput = o => new(o.Slug, o.Name, o.Type, o.Country?.Slug, FuzzyDateDto.From(o.Founded), o.Description, o.WikidataId),
        Blank = new(null, "", null, null, null, null, null),
        SlugOf = i => i.Slug,
        InputName = i => i.Name,
        WithSlug = (i, slug) => i with { Slug = slug },
        WikidataIdOf = i => i.WikidataId,
        New = slug => new Organization { Slug = slug, Name = "", Status = PublishStatus.Draft },
        ResolveAsync = (db, i, _, ct) => CountryAsync(db, i.Country, ct),
        Apply = (o, b, i, countryId, c) =>
        {
            c.Set("slug", b.Slug, i.Slug, () => o.Slug = i.Slug!);
            c.Set("name", b.Name, i.Name, () => o.Name = i.Name);
            c.Set("type", b.Type, i.Type, () => o.Type = i.Type);
            c.Set("country", b.Country, i.Country, () => o.CountryId = countryId);
            c.Set("founded", b.Founded, i.Founded, () => o.Founded = i.Founded.ToEntity());
            c.Set("description", b.Description, i.Description, () => o.Description = i.Description);
            c.Set("wikidataId", b.WikidataId, i.WikidataId, () => o.WikidataId = i.WikidataId);
        },
        ReferencedByAsync = async (db, id, ct) =>
        [
            .. await db.FirearmDesigners.IgnoreQueryFilters().Where(d => d.OrganizationId == id)
                .Select(d => "firearm " + d.Firearm.Slug + " (designer)").Distinct().ToListAsync(ct),
            .. await db.FirearmManufacturers.IgnoreQueryFilters().Where(m => m.OrganizationId == id)
                .Select(m => "firearm " + m.Firearm.Slug + " (manufacturer)").Distinct().ToListAsync(ct),
        ],
    };

    // Cartridges -----------------------------------------------------------------------------------------------

    private sealed record CartridgeRefs(int? ParentId, int? CountryId);

    private static readonly ResourceSpec<Cartridge, CartridgeInput, CartridgeRefs> Cartridges = new()
    {
        Title = "Cartridge",
        SlugType = SluggedEntity.Cartridge,
        HasPublicPage = true,
        Set = db => db.Cartridges,
        Include = q => q.Include(k => k.ParentCartridge).Include(k => k.CountryOfOrigin),
        NameOf = k => k.Name,
        ToInput = k => new(k.Slug, k.Name, k.AlternativeNames, k.WikidataId, k.Type, k.BulletDiameterMm, k.CaseLengthMm,
            k.OverallLengthMm, k.ParentCartridge?.Slug, FuzzyDateDto.From(k.DesignDate), k.CountryOfOrigin?.Slug, k.Description),
        Blank = new(null, "", [], null, null, null, null, null, null, null, null, null),
        SlugOf = i => i.Slug,
        InputName = i => i.Name,
        WithSlug = (i, slug) => i with { Slug = slug },
        WikidataIdOf = i => i.WikidataId,
        New = slug => new Cartridge { Slug = slug, Name = "", Status = PublishStatus.Draft },
        ResolveAsync = async (db, i, current, ct) =>
        {
            var country = await CountryAsync(db, i.Country, ct);
            if (country.Problem is not null)
            {
                return new(new(null, null), country.Problem);
            }

            if (i.Parent is null)
            {
                return new(new(null, country.Refs), null);
            }

            // Parent links never loop: walk up from the new parent and refuse if this cartridge appears.
            var parentOf = await db.Cartridges.IgnoreQueryFilters().ToDictionaryAsync(k => k.Id, k => k.ParentCartridgeId, ct);
            var parentId = await db.Cartridges.IgnoreQueryFilters().Where(k => k.Slug == i.Parent).Select(k => (int?)k.Id).FirstOrDefaultAsync(ct);
            if (parentId is null)
            {
                return new(new(null, null), AdminProblems.UnknownReference("cartridge", [i.Parent]));
            }

            var seen = new HashSet<int>(); // guards against a loop already in the data
            for (int? step = parentId; step is { } s && seen.Add(s); step = parentOf.GetValueOrDefault(s))
            {
                if (s == current?.Id)
                {
                    return new(new(null, null), AdminProblems.BadRequest("Parent loop", "A cartridge cannot descend from itself."));
                }
            }

            return new(new(parentId, country.Refs), null);
        },
        Apply = (k, b, i, refs, c) =>
        {
            c.Set("slug", b.Slug, i.Slug, () => k.Slug = i.Slug!);
            c.Set("name", b.Name, i.Name, () => k.Name = i.Name);
            SetNames(c, b.AlternativeNames, i.AlternativeNames, names => k.AlternativeNames = names);
            c.Set("wikidataId", b.WikidataId, i.WikidataId, () => k.WikidataId = i.WikidataId);
            c.Set("type", b.Type, i.Type, () => k.Type = i.Type);
            c.Set("bulletDiameterMm", b.BulletDiameterMm, i.BulletDiameterMm, () => k.BulletDiameterMm = i.BulletDiameterMm);
            c.Set("caseLengthMm", b.CaseLengthMm, i.CaseLengthMm, () => k.CaseLengthMm = i.CaseLengthMm);
            c.Set("overallLengthMm", b.OverallLengthMm, i.OverallLengthMm, () => k.OverallLengthMm = i.OverallLengthMm);
            c.Set("parent", b.Parent, i.Parent, () => k.ParentCartridgeId = refs.ParentId);
            c.Set("designDate", b.DesignDate, i.DesignDate, () => k.DesignDate = i.DesignDate.ToEntity());
            c.Set("country", b.Country, i.Country, () => k.CountryOfOriginId = refs.CountryId);
            c.Set("description", b.Description, i.Description, () => k.Description = i.Description);
        },
        MissingForPublish = k => [.. k.Type is null ? ["type"] : Array.Empty<string>(), .. k.CountryOfOriginId is null ? ["country"] : Array.Empty<string>()],
        ReferencedByAsync = async (db, id, ct) =>
        [
            .. await db.VariantCartridges.IgnoreQueryFilters().Where(v => v.CartridgeId == id)
                .Select(v => "firearm " + v.Variant.Firearm.Slug).Distinct().ToListAsync(ct),
            .. await db.Cartridges.IgnoreQueryFilters().Where(k => k.ParentCartridgeId == id)
                .Select(k => "cartridge " + k.Slug).ToListAsync(ct),
        ],
    };

    // Countries ------------------------------------------------------------------------------------------------

    private static readonly ResourceSpec<Country, CountryInput, object?> Countries = new()
    {
        Title = "Country",
        SlugType = SluggedEntity.Country,
        HasPublicPage = false,
        Set = db => db.Countries,
        NameOf = c => c.Name,
        ToInput = c => new(c.Slug, c.Name, c.IsoCode, c.IsHistorical, c.WikidataId),
        Blank = new(null, "", null, false, null),
        SlugOf = i => i.Slug,
        InputName = i => i.Name,
        WithSlug = (i, slug) => i with { Slug = slug },
        WikidataIdOf = i => i.WikidataId,
        New = slug => new Country { Slug = slug, Name = "" },
        ResolveAsync = (_, _, _, _) => Task.FromResult(new Resolved<object?>(null, null)),
        Apply = (country, b, i, _, c) =>
        {
            c.Set("slug", b.Slug, i.Slug, () => country.Slug = i.Slug!);
            c.Set("name", b.Name, i.Name, () => country.Name = i.Name);
            c.Set("isoCode", b.IsoCode, i.IsoCode, () => country.IsoCode = i.IsoCode);
            c.Set("isHistorical", b.IsHistorical, i.IsHistorical, () => country.IsHistorical = i.IsHistorical);
            c.Set("wikidataId", b.WikidataId, i.WikidataId, () => country.WikidataId = i.WikidataId);
        },
        ReferencedByAsync = async (db, id, ct) =>
        [
            .. await db.Firearms.IgnoreQueryFilters().Where(f => f.CountryOfOriginId == id).Select(f => "firearm " + f.Slug).ToListAsync(ct),
            .. await db.People.IgnoreQueryFilters().Where(p => p.NationalityId == id).Select(p => "person " + p.Slug).ToListAsync(ct),
            .. await db.Organizations.IgnoreQueryFilters().Where(o => o.CountryId == id).Select(o => "organization " + o.Slug).ToListAsync(ct),
            .. await db.Cartridges.IgnoreQueryFilters().Where(k => k.CountryOfOriginId == id).Select(k => "cartridge " + k.Slug).ToListAsync(ct),
            .. await db.Adoptions.IgnoreQueryFilters().Where(a => a.CountryId == id).Select(a => "adoption of " + a.Firearm.Slug).Distinct().ToListAsync(ct),
        ],
    };

    // Tags -----------------------------------------------------------------------------------------------------

    private static readonly ResourceSpec<Tag, TagInput, object?> Tags = new()
    {
        Title = "Tag",
        SlugType = SluggedEntity.Tag,
        HasPublicPage = false,
        Set = db => db.Tags,
        NameOf = t => t.Name,
        ToInput = t => new(t.Slug, t.Name, t.Group),
        Blank = new(null, "", default),
        SlugOf = i => i.Slug,
        InputName = i => i.Name,
        WithSlug = (i, slug) => i with { Slug = slug },
        New = slug => new Tag { Slug = slug, Name = "" },
        ResolveAsync = (_, _, _, _) => Task.FromResult(new Resolved<object?>(null, null)),
        Apply = (t, b, i, _, c) =>
        {
            c.Set("slug", b.Slug, i.Slug, () => t.Slug = i.Slug!);
            c.Set("name", b.Name, i.Name, () => t.Name = i.Name);
            c.Set("group", b.Group, i.Group, () => t.Group = i.Group);
        },
        ReferencedByAsync = async (db, id, ct) =>
        [
            .. await db.FirearmTags.Where(x => x.TagId == id)
                .Join(db.Firearms.IgnoreQueryFilters(), x => x.FirearmId, f => f.Id, (_, f) => "firearm " + f.Slug).ToListAsync(ct),
            .. await db.WikidataClassTagMaps.Where(m => m.TagId == id).Select(m => "Wikidata class " + m.Qid).ToListAsync(ct),
        ],
    };

    // Mechanisms -----------------------------------------------------------------------------------------------

    private static readonly ResourceSpec<OperatingMechanism, MechanismInput, OperatingMechanism?> Mechanisms = new()
    {
        Title = "Mechanism",
        SlugType = SluggedEntity.Mechanism,
        HasPublicPage = true,
        Set = db => db.OperatingMechanisms,
        Include = q => q.Include(m => m.Parent),
        NameOf = m => m.Name,
        ToInput = m => new(m.Slug, m.Name, m.Parent?.Slug, m.Explanation),
        Blank = new(null, "", null, null),
        SlugOf = i => i.Slug,
        InputName = i => i.Name,
        WithSlug = (i, slug) => i with { Slug = slug },
        New = slug => new OperatingMechanism { Slug = slug, Name = "", Path = "" },
        ResolveAsync = async (db, i, current, ct) =>
        {
            if (i.Parent is null)
            {
                return new(null, null);
            }

            var parent = await db.OperatingMechanisms.AsTracking().FirstOrDefaultAsync(m => m.Slug == i.Parent, ct);
            if (parent is null)
            {
                return new(null, AdminProblems.UnknownReference("mechanism", [i.Parent]));
            }

            return current is not null && parent.Path.StartsWith(current.Path, StringComparison.Ordinal)
                ? new(null, AdminProblems.BadRequest("Mechanism loop", "A mechanism cannot move beneath itself."))
                : new(parent, null);
        },
        Apply = (m, b, i, parent, c) =>
        {
            c.Set("slug", b.Slug, i.Slug, () => m.Slug = i.Slug!);
            c.Set("name", b.Name, i.Name, () => m.Name = i.Name);
            c.Set("parent", b.Parent, i.Parent, () => m.Parent = parent);
            c.Set("explanation", b.Explanation, i.Explanation, () => m.Explanation = i.Explanation);
            // The path follows the parent and slug; descendants follow in BeforeSaveAsync.
            m.Path = $"{parent?.Path ?? "/"}{m.Slug}/";
        },
        BeforeSaveAsync = async (db, m, ct) =>
        {
            var oldPath = (string)db.Entry(m).Property(x => x.Path).OriginalValue!;
            if (m.Id == 0 || oldPath == m.Path)
            {
                return;
            }

            var descendants = await db.OperatingMechanisms.AsTracking()
                .Where(d => d.Path.StartsWith(oldPath) && d.Id != m.Id)
                .ToListAsync(ct);
            foreach (var d in descendants)
            {
                d.Path = m.Path + d.Path[oldPath.Length..];
            }
        },
        ReferencedByAsync = async (db, id, ct) =>
        [
            .. await db.Firearms.IgnoreQueryFilters().Where(f => f.OperatingMechanismId == id).Select(f => "firearm " + f.Slug).ToListAsync(ct),
            .. await db.OperatingMechanisms.Where(m => m.ParentId == id).Select(m => "mechanism " + m.Slug).ToListAsync(ct),
        ],
    };
}

// Validators ---------------------------------------------------------------------------------------------------

internal static class SharedRules
{
    public static void SlugAndName<T>(AbstractValidator<T> v, System.Linq.Expressions.Expression<Func<T, string?>> slug, System.Linq.Expressions.Expression<Func<T, string>> name)
    {
        var getSlug = slug.Compile();
        v.RuleFor(slug).Must(Slug.IsValid).WithMessage("Slugs are lowercase letters and digits separated by single hyphens.")
            .When(x => getSlug(x) is not null);
        v.RuleFor(name).NotEmpty().MaximumLength(200);
    }
}

public sealed class PersonInputValidator : AbstractValidator<PersonInput>
{
    public PersonInputValidator()
    {
        SharedRules.SlugAndName(this, x => x.Slug, x => x.FullName);
        RuleFor(x => x.BirthDate!).SetValidator(new FuzzyDateValidator()).When(x => x.BirthDate is not null);
        RuleFor(x => x.DeathDate!).SetValidator(new FuzzyDateValidator()).When(x => x.DeathDate is not null);
        RuleFor(x => x.WikidataId).Matches(@"^Q[1-9][0-9]*\z").When(x => x.WikidataId is not null);
    }
}

public sealed class OrganizationInputValidator : AbstractValidator<OrganizationInput>
{
    public OrganizationInputValidator()
    {
        SharedRules.SlugAndName(this, x => x.Slug, x => x.Name);
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type is not null);
        RuleFor(x => x.Founded!).SetValidator(new FuzzyDateValidator()).When(x => x.Founded is not null);
        RuleFor(x => x.WikidataId).Matches(@"^Q[1-9][0-9]*\z").When(x => x.WikidataId is not null);
    }
}

public sealed class CartridgeInputValidator : AbstractValidator<CartridgeInput>
{
    public CartridgeInputValidator()
    {
        SharedRules.SlugAndName(this, x => x.Slug, x => x.Name);
        RuleForEach(x => x.AlternativeNames).NotEmpty().MaximumLength(200);
        RuleFor(x => x.WikidataId).Matches(@"^Q[1-9][0-9]*\z").When(x => x.WikidataId is not null);
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type is not null);
        RuleFor(x => x.BulletDiameterMm).GreaterThan(0);
        RuleFor(x => x.CaseLengthMm).GreaterThan(0);
        RuleFor(x => x.OverallLengthMm).GreaterThan(0);
        RuleFor(x => x.DesignDate!).SetValidator(new FuzzyDateValidator()).When(x => x.DesignDate is not null);
    }
}

public sealed class CountryInputValidator : AbstractValidator<CountryInput>
{
    public CountryInputValidator()
    {
        SharedRules.SlugAndName(this, x => x.Slug, x => x.Name);
        RuleFor(x => x.IsoCode).Matches(@"^[A-Z]{2}\z").When(x => x.IsoCode is not null);
        RuleFor(x => x.WikidataId).Matches(@"^Q[1-9][0-9]*\z").When(x => x.WikidataId is not null);
    }
}

public sealed class TagInputValidator : AbstractValidator<TagInput>
{
    public TagInputValidator()
    {
        SharedRules.SlugAndName(this, x => x.Slug, x => x.Name);
        RuleFor(x => x.Group).IsInEnum();
    }
}

public sealed class MechanismInputValidator : AbstractValidator<MechanismInput>
{
    public MechanismInputValidator()
    {
        SharedRules.SlugAndName(this, x => x.Slug, x => x.Name);
        RuleFor(x => x.Parent!).Must(Slug.IsValid).When(x => x.Parent is not null);
    }
}
