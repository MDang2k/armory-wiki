namespace ArmoryWiki.Domain;

public enum PublishStatus { Draft, Published }

public enum DatePrecision { Day, Month, Year, Decade }

public enum FeedSystem
{
    DetachableBoxMagazine,
    InternalMagazine,
    Belt,
    Drum,
    Tubular,
    Cylinder,
    StripperClip,
    EnBlocClip,
    SingleShot,
    Other,
}

[Flags]
public enum FiringModes
{
    None = 0,
    SemiAutomatic = 1,
    Burst = 2,
    Automatic = 4,
}

public enum TagGroup { Type, Role, Era }

public enum OrganizationType { Company, DesignBureau, StateArsenal, Other }

public enum CartridgeType { Pistol, Intermediate, FullPowerRifle, Magnum, Shotgun, Rimfire, Other }

public enum DesignerRole { LeadDesigner, CoDesigner, Developer }

public enum RelationType { DerivedFrom, CopyOf, InfluencedBy, CompetedAgainst, Replaced }

/// <summary>Entity kinds that own a public slug (used by <see cref="SlugRedirect"/>).</summary>
public enum SluggedEntity { Firearm, Variant, Person, Organization, Cartridge, Mechanism, Country, Tag }
