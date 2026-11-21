using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Features.Admin.Firearms;

// Inputs. References to other entities are slugs. GET returns the same shapes, so the admin UI can send them back.

/// <summary>A firearm's own fields. Omit <see cref="Slug"/> to generate one from the name.</summary>
public sealed record FirearmInput(
    string? Slug,
    string Name,
    IReadOnlyList<string>? AlternativeNames,
    string? WikidataId,
    string? Country,
    FuzzyDateDto? DesignDate,
    FuzzyDateDto? ProductionStart,
    FuzzyDateDto? ProductionEnd,
    string? Mechanism,
    FeedSystem? FeedSystem,
    string? Summary,
    string? DevelopmentHistory,
    string? ServiceHistory,
    string? WikipediaTitle,
    long? WikipediaRevisionId);

/// <summary>A variant's own fields. Omit <see cref="Slug"/> to generate one from the name.</summary>
public sealed record VariantInput(
    string? Slug,
    string Name,
    FuzzyDateDto? IntroducedDate,
    int? WeightEmptyG,
    int? OverallLengthMm,
    int? OverallLengthFoldedMm,
    int? BarrelLengthMm,
    int? RateOfFireMinRpm,
    int? RateOfFireMaxRpm,
    int? MuzzleVelocityMps,
    int? EffectiveRangeM,
    int? MagazineCapacityStandard,
    IReadOnlyList<FiringModes>? FiringModes,
    IReadOnlyDictionary<string, string>? ExtraSpecs);

public sealed record TagsInput(IReadOnlyList<string> Tags);

/// <summary>Exactly one of <see cref="Person"/> or <see cref="Organization"/>.</summary>
public sealed record DesignerInput(string? Person, string? Organization, DesignerRole Role);

public sealed record DesignersInput(IReadOnlyList<DesignerInput> Designers);

public sealed record ManufacturerInput(string Organization, FuzzyDateDto? From, FuzzyDateDto? To, bool IsLicensed);

public sealed record ManufacturersInput(IReadOnlyList<ManufacturerInput> Manufacturers);

/// <summary>A relation stored on this firearm: "this firearm <see cref="Type"/> <see cref="Firearm"/>".</summary>
public sealed record RelationInput(RelationType Type, string Firearm, string? Note);

public sealed record RelationsInput(IReadOnlyList<RelationInput> Relations);

public sealed record AdoptionInput(string Country, FuzzyDateDto? From, FuzzyDateDto? To, string? Note);

public sealed record AdoptionsInput(IReadOnlyList<AdoptionInput> Adoptions);

public sealed record ChamberingInput(string Cartridge, bool IsPrimary);

public sealed record ChamberingsInput(IReadOnlyList<ChamberingInput> Chamberings);

// Outputs.

public sealed record AdminVariantDto(int Id, bool IsBaseVariant, VariantInput Details, IReadOnlyList<ChamberingInput> Chamberings);

/// <summary>
/// <see cref="Version"/> must be sent back with every change (409 when stale). <see cref="MissingForPublish"/> lists the
/// unmet publish requirements by field name. <see cref="LockedFields"/> are keys imports will not overwrite.
/// </summary>
public sealed record AdminFirearmDto(
    int Id,
    uint Version,
    PublishStatus Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> LockedFields,
    IReadOnlyList<string> MissingForPublish,
    FirearmInput Details,
    IReadOnlyList<string> Tags,
    IReadOnlyList<AdminVariantDto> Variants,
    IReadOnlyList<DesignerInput> Designers,
    IReadOnlyList<ManufacturerInput> Manufacturers,
    IReadOnlyList<RelationInput> Relations,
    IReadOnlyList<AdoptionInput> Adoptions);

public sealed record AdminFirearmListItemDto(
    int Id,
    string Slug,
    string Name,
    PublishStatus Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> MissingForPublish);
