namespace ArmoryWiki.Domain;

/// <summary>A base model, e.g. "AK-47". Specifications live on its variants.</summary>
public sealed class Firearm : Entity
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public List<string> AlternativeNames { get; set; } = [];
    public string? WikidataId { get; set; }
    public PublishStatus Status { get; set; }
    /// <summary>Fields edited by an admin; importers never overwrite these.</summary>
    public List<string> LockedFields { get; set; } = [];

    public int? CountryOfOriginId { get; set; }
    public Country? CountryOfOrigin { get; set; }
    public FuzzyDate? DesignDate { get; set; }
    public FuzzyDate? ProductionStart { get; set; }
    public FuzzyDate? ProductionEnd { get; set; }
    public int? OperatingMechanismId { get; set; }
    public OperatingMechanism? OperatingMechanism { get; set; }
    public FeedSystem? FeedSystem { get; set; }

    /// <summary>Short plain text, at most 500 characters.</summary>
    public string? Summary { get; set; }
    public string? DevelopmentHistory { get; set; }
    public string? ServiceHistory { get; set; }
    /// <summary>Set when prose was adapted from Wikipedia; drives the attribution footer.</summary>
    public string? WikipediaTitle { get; set; }
    public long? WikipediaRevisionId { get; set; }

    public List<Tag> Tags { get; set; } = [];
    public List<Variant> Variants { get; set; } = [];
    public List<FirearmDesigner> Designers { get; set; } = [];
    public List<FirearmManufacturer> Manufacturers { get; set; } = [];
    public List<FirearmRelation> RelationsFrom { get; set; } = [];
    public List<FirearmRelation> RelationsTo { get; set; } = [];
    public List<Adoption> Adoptions { get; set; } = [];
}

/// <summary>A concrete version of a firearm; holds the specifications. Exactly one per firearm is the base variant.</summary>
public sealed class Variant : Entity
{
    public int Id { get; set; }
    public int FirearmId { get; set; }
    public Firearm Firearm { get; set; } = null!;
    /// <summary>Unique per firearm.</summary>
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public bool IsBaseVariant { get; set; }
    public FuzzyDate? IntroducedDate { get; set; }

    public int? WeightEmptyG { get; set; }
    public int? OverallLengthMm { get; set; }
    public int? OverallLengthFoldedMm { get; set; }
    public int? BarrelLengthMm { get; set; }
    public int? RateOfFireMinRpm { get; set; }
    public int? RateOfFireMaxRpm { get; set; }
    public int? MuzzleVelocityMps { get; set; }
    public int? EffectiveRangeM { get; set; }
    public int? MagazineCapacityStandard { get; set; }
    public FiringModes FiringModes { get; set; }
    /// <summary>Rare or odd specs, stored as jsonb.</summary>
    public Dictionary<string, string> ExtraSpecs { get; set; } = [];

    public List<VariantCartridge> Chamberings { get; set; } = [];
}

public sealed class VariantCartridge : Entity
{
    public int VariantId { get; set; }
    public Variant Variant { get; set; } = null!;
    public int CartridgeId { get; set; }
    public Cartridge Cartridge { get; set; } = null!;
    /// <summary>At most one per variant.</summary>
    public bool IsPrimary { get; set; }
}

public sealed class FirearmTag : Entity
{
    public int FirearmId { get; set; }
    public int TagId { get; set; }
}

/// <summary>Designer credit: exactly one of <see cref="PersonId"/> or <see cref="OrganizationId"/> is set.</summary>
public sealed class FirearmDesigner : Entity
{
    public int Id { get; set; }
    public int FirearmId { get; set; }
    public Firearm Firearm { get; set; } = null!;
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public DesignerRole Role { get; set; }
}

public sealed class FirearmManufacturer : Entity
{
    public int Id { get; set; }
    public int FirearmId { get; set; }
    public Firearm Firearm { get; set; } = null!;
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public FuzzyDate? From { get; set; }
    public FuzzyDate? To { get; set; }
    public bool IsLicensed { get; set; }
}

/// <summary>"AKM DerivedFrom AK-47" is stored as From = AKM, To = AK-47.</summary>
public sealed class FirearmRelation : Entity
{
    public int FromFirearmId { get; set; }
    public Firearm FromFirearm { get; set; } = null!;
    public int ToFirearmId { get; set; }
    public Firearm ToFirearm { get; set; } = null!;
    public RelationType Type { get; set; }
    public string? Note { get; set; }
}

public sealed class Adoption : Entity
{
    public int Id { get; set; }
    public int FirearmId { get; set; }
    public Firearm Firearm { get; set; } = null!;
    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;
    public FuzzyDate? From { get; set; }
    public FuzzyDate? To { get; set; }
    public string? Note { get; set; }
}
