import type { Dto } from "./api/types";

/** Display names for the API's enums, in the glossary's words. */

export const DESIGNER_ROLES: Record<Dto<"DesignerRole">, string> = {
  LeadDesigner: "lead designer",
  CoDesigner: "co-designer",
  Developer: "developer",
};

export const ORGANIZATION_TYPES: Record<NonNullable<Dto<"OrganizationType">>, string> = {
  Company: "Company",
  DesignBureau: "Design bureau",
  StateArsenal: "State arsenal",
  Other: "Other",
};

export const CARTRIDGE_TYPES: Record<NonNullable<Dto<"CartridgeType">>, string> = {
  Pistol: "Pistol",
  Intermediate: "Intermediate",
  FullPowerRifle: "Full-power rifle",
  Magnum: "Magnum",
  Shotgun: "Shotgun",
  Rimfire: "Rimfire",
  Other: "Other",
};

export const FEED_SYSTEMS: Record<NonNullable<Dto<"FeedSystem">>, string> = {
  DetachableBoxMagazine: "Detachable box magazine",
  InternalMagazine: "Internal magazine",
  Belt: "Belt",
  Drum: "Drum magazine",
  Tubular: "Tubular magazine",
  Cylinder: "Revolving cylinder",
  StripperClip: "Stripper clip",
  EnBlocClip: "En-bloc clip",
  SingleShot: "Single shot",
  Other: "Other",
};

/** The API's "missing for publish" keys (input field names), as the admin reads them. Unknown keys show as-is. */
export const MISSING_FOR_PUBLISH: Record<string, string> = {
  country: "country of origin",
  designDate: "design date",
  mechanism: "operating mechanism",
  primaryCartridge: "primary cartridge",
  primaryCartridgePublished: "primary cartridge is a draft",
  type: "type",
};

export const FIRING_MODES: Record<Dto<"FiringModes">, string> = {
  SemiAutomatic: "semi-automatic",
  Burst: "burst",
  Automatic: "automatic",
};

/** A relation as the admin enters it on this firearm: "this firearm … <the other one>" (GLOSSARY.md). */
export const RELATION_TYPES: Record<Dto<"RelationType">, string> = {
  DerivedFrom: "is derived from",
  CopyOf: "is a copy of",
  InfluencedBy: "was influenced by",
  CompetedAgainst: "competed against",
  Replaced: "replaced",
};
