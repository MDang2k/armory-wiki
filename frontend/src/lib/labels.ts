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
