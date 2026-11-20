namespace ArmoryWiki.Domain;

public sealed class Person : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string FullName { get; set; }
    public FuzzyDate? BirthDate { get; set; }
    public FuzzyDate? DeathDate { get; set; }
    public int? NationalityId { get; set; }
    public Country? Nationality { get; set; }
    public string? Biography { get; set; }
    public string? WikidataId { get; set; }
    public PublishStatus Status { get; set; }
    public List<string> LockedFields { get; set; } = [];
}

/// <summary>Manufacturers, design bureaus, state arsenals.</summary>
public sealed class Organization : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public int? CountryId { get; set; }
    public Country? Country { get; set; }
    public OrganizationType Type { get; set; }
    public FuzzyDate? Founded { get; set; }
    public string? Description { get; set; }
    public string? WikidataId { get; set; }
    public PublishStatus Status { get; set; }
    public List<string> LockedFields { get; set; } = [];
}

public sealed class Country : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    /// <summary>Informational only; historical states often have none.</summary>
    public string? IsoCode { get; set; }
    public bool IsHistorical { get; set; }
    public string? WikidataId { get; set; }
}

public sealed class Cartridge : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public List<string> AlternativeNames { get; set; } = [];
    public string? WikidataId { get; set; }
    public PublishStatus Status { get; set; }
    public List<string> LockedFields { get; set; } = [];

    public decimal? BulletDiameterMm { get; set; }
    public decimal? CaseLengthMm { get; set; }
    public decimal? OverallLengthMm { get; set; }
    public CartridgeType Type { get; set; }
    public int? ParentCartridgeId { get; set; }
    public Cartridge? ParentCartridge { get; set; }
    public FuzzyDate? DesignDate { get; set; }
    public int? CountryOfOriginId { get; set; }
    public Country? CountryOfOrigin { get; set; }
    public string? Description { get; set; }
}

/// <summary>Tree node, e.g. Gas-operated → Short-stroke piston. <see cref="Path"/> is "/gas-operated/short-stroke-piston/".</summary>
public sealed class OperatingMechanism : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }
    public OperatingMechanism? Parent { get; set; }
    public List<OperatingMechanism> Children { get; set; } = [];
    public required string Path { get; set; }
    public string? Explanation { get; set; }
}

/// <summary>Controlled vocabulary replacing a single category (ADR-0005).</summary>
public sealed class Tag : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public TagGroup Group { get; set; }
}

/// <summary>Maps a Wikidata "instance of" class to a tag during import.</summary>
public sealed class WikidataClassTagMap : Entity
{
    public required string Qid { get; set; }
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

/// <summary>Old slug kept so renamed pages answer with a 301.</summary>
public sealed class SlugRedirect : Entity
{
    public SluggedEntity EntityType { get; set; }
    public required string OldSlug { get; set; }
    public int EntityId { get; set; }
}
