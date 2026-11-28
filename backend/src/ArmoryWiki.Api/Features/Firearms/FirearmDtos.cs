using ArmoryWiki.Api.Common;
using ArmoryWiki.Domain;

namespace ArmoryWiki.Api.Features.Firearms;

public sealed record FirearmListItemDto(
    string Slug,
    string Name,
    IReadOnlyList<TagDto> Tags,
    SlugName? Country,
    FuzzyDateDto? DesignDate,
    SlugName? Mechanism,
    SlugName? PrimaryCartridge,
    string? ThumbnailUrl);

public sealed record SpecsDto(
    int? WeightEmptyG,
    int? OverallLengthMm,
    int? OverallLengthFoldedMm,
    int? BarrelLengthMm,
    int? RateOfFireMinRpm,
    int? RateOfFireMaxRpm,
    int? MuzzleVelocityMps,
    int? EffectiveRangeM,
    int? MagazineCapacityStandard,
    IReadOnlyList<FiringModes> FiringModes)
{
    /// <summary>Runs client-side inside EF projections.</summary>
    public static SpecsDto From(Variant v) => new(
        v.WeightEmptyG, v.OverallLengthMm, v.OverallLengthFoldedMm, v.BarrelLengthMm, v.RateOfFireMinRpm,
        v.RateOfFireMaxRpm, v.MuzzleVelocityMps, v.EffectiveRangeM, v.MagazineCapacityStandard, Modes(v.FiringModes));

    /// <summary>The flags as a list, so the API never emits "SemiAutomatic, Automatic" as one string.</summary>
    private static FiringModes[] Modes(FiringModes flags) =>
        Enum.GetValues<FiringModes>().Where(m => m != Domain.FiringModes.None && flags.HasFlag(m)).ToArray();
}

public sealed record ChamberingDto(SlugName Cartridge, bool IsPrimary);

public sealed record VariantDto(
    string Slug,
    string Name,
    bool IsBaseVariant,
    FuzzyDateDto? IntroducedDate,
    SpecsDto Specs,
    IReadOnlyDictionary<string, string> ExtraSpecs,
    IReadOnlyList<ChamberingDto> Chamberings);

/// <summary>Exactly one of <see cref="Person"/> or <see cref="Organization"/> is set.</summary>
public sealed record DesignerDto(DesignerRole Role, SlugName? Person, SlugName? Organization);

public sealed record ManufacturerDto(SlugName Organization, FuzzyDateDto? From, FuzzyDateDto? To, bool IsLicensed);

public sealed record AdoptionDto(SlugName Country, FuzzyDateDto? From, FuzzyDateDto? To, string? Note);

/// <summary>A relation seen from this firearm: "this <see cref="Type"/> <see cref="Firearm"/>" or the reverse.</summary>
public sealed record RelationDto(RelationType Type, SlugName Firearm, string? Note);

public sealed record WikipediaSourceDto(string Title, long? RevisionId);

public sealed record FirearmDetailDto(
    string Slug,
    string Name,
    IReadOnlyList<string> AlternativeNames,
    string? WikidataId,
    IReadOnlyList<TagDto> Tags,
    SlugName? Country,
    FuzzyDateDto? DesignDate,
    FuzzyDateDto? ProductionStart,
    FuzzyDateDto? ProductionEnd,
    SlugName? Mechanism,
    FeedSystem? FeedSystem,
    string? Summary,
    string? DevelopmentHistory,
    string? ServiceHistory,
    WikipediaSourceDto? Wikipedia,
    IReadOnlyList<VariantDto> Variants,
    IReadOnlyList<DesignerDto> Designers,
    IReadOnlyList<ManufacturerDto> Manufacturers,
    IReadOnlyList<AdoptionDto> Adoptions,
    /// <summary>Relations stored on this firearm, e.g. "AKM DerivedFrom AK-47" on the AKM page.</summary>
    IReadOnlyList<RelationDto> RelationsFrom,
    /// <summary>Relations pointing at this firearm, e.g. the same relation on the AK-47 page.</summary>
    IReadOnlyList<RelationDto> RelationsTo,
    /// <summary>Footnotes: citations of the firearm and of its variants, in the order added.</summary>
    IReadOnlyList<CitationDto> Citations,
    /// <summary>In display order; the first is the thumbnail.</summary>
    IReadOnlyList<ImageDto> Images,
    /// <summary>Approved videos only, newest first.</summary>
    IReadOnlyList<VideoDto> Videos);

public sealed record TagFacetDto(string Slug, string Name, TagGroup Group, int Count);

public sealed record FacetCountDto(string Slug, string Name, int Count);

public sealed record DecadeFacetDto(int Decade, int Count);

/// <summary>Mechanism counts include every firearm using a descendant mechanism.</summary>
public sealed record FirearmFacetsDto(
    IReadOnlyList<TagFacetDto> Tags,
    IReadOnlyList<FacetCountDto> Countries,
    IReadOnlyList<FacetCountDto> Mechanisms,
    IReadOnlyList<DecadeFacetDto> Decades);

public sealed record FamilyTreeNodeDto(string Slug, string Name, FuzzyDateDto? DesignDate, bool IsRoot);

/// <summary><see cref="Target"/> is derived from <see cref="Source"/>, or a copy of it when <see cref="IsCopy"/>.</summary>
public sealed record FamilyTreeEdgeDto(string Source, string Target, bool IsCopy);

/// <summary><see cref="Truncated"/>: the family is larger than the nodes returned (the nearest ones are kept).</summary>
public sealed record FamilyTreeDto(IReadOnlyList<FamilyTreeNodeDto> Nodes, IReadOnlyList<FamilyTreeEdgeDto> Edges, bool Truncated);

public sealed record CompareItemDto(string Slug, string Name, string VariantName, SlugName? PrimaryCartridge, SpecsDto Specs);
