import { z } from "zod";
import type { Dto } from "@/lib/api/types";
import { FIRING_MODES } from "@/lib/labels";
import { dateOrNull, fuzzyDateField, numberOrNull, numberText, orNull, requiredSlugField, wholeNumberField } from "./formFields";
import { fuzzyDateText } from "./fuzzyDate";

/*
 * The forms for a firearm's parts (PLAN.md Phase 7b): each list is replaced whole by its own PUT, so each form
 * holds the list as rows and turns it back into the API's input. Shape and required fields only; the API checks
 * references and rules, and its messages show on the section.
 */

const enumOf = <T extends string>(labels: Record<T, string>) => z.enum(Object.keys(labels) as [T, ...T[]]);

// Tags ---------------------------------------------------------------------------------------------------------

export const tagsSchema = z.object({ rows: z.array(z.object({ tag: requiredSlugField("a tag") })) });
export type TagsForm = z.infer<typeof tagsSchema>;

export const tagsForm = (tags: string[]): TagsForm => ({ rows: tags.map((tag) => ({ tag })) });
export const toTagsInput = (form: TagsForm): Dto<"TagsInput"> => ({ tags: form.rows.map((r) => r.tag.trim()) });

// Designers ----------------------------------------------------------------------------------------------------

const DESIGNER_ROLES = { LeadDesigner: "", CoDesigner: "", Developer: "" } satisfies Record<Dto<"DesignerRole">, string>;

export const designersSchema = z.object({
  rows: z.array(
    z.object({
      kind: z.enum(["person", "organization"]),
      slug: requiredSlugField("a person or organization"),
      role: enumOf(DESIGNER_ROLES),
    }),
  ),
});
export type DesignersForm = z.infer<typeof designersSchema>;

export const designersForm = (designers: Dto<"DesignerInput">[]): DesignersForm => ({
  rows: designers.map((d) => ({
    kind: d.person ? "person" : "organization",
    slug: d.person ?? d.organization ?? "",
    role: d.role,
  })),
});

export const toDesignersInput = (form: DesignersForm): Dto<"DesignersInput"> => ({
  designers: form.rows.map((r) => ({
    person: r.kind === "person" ? r.slug.trim() : null,
    organization: r.kind === "organization" ? r.slug.trim() : null,
    role: r.role,
  })),
});

// Manufacturers ------------------------------------------------------------------------------------------------

export const manufacturersSchema = z.object({
  rows: z.array(
    z.object({ organization: requiredSlugField("an organization"), from: fuzzyDateField, to: fuzzyDateField, isLicensed: z.boolean() }),
  ),
});
export type ManufacturersForm = z.infer<typeof manufacturersSchema>;

export const manufacturersForm = (list: Dto<"ManufacturerInput">[]): ManufacturersForm => ({
  rows: list.map((m) => ({ organization: m.organization, from: fuzzyDateText(m.from), to: fuzzyDateText(m.to), isLicensed: m.isLicensed })),
});

export const toManufacturersInput = (form: ManufacturersForm): Dto<"ManufacturersInput"> => ({
  manufacturers: form.rows.map((r) => ({
    organization: r.organization.trim(),
    from: dateOrNull(r.from),
    to: dateOrNull(r.to),
    isLicensed: r.isLicensed,
  })),
});

// Adoptions ----------------------------------------------------------------------------------------------------

export const adoptionsSchema = z.object({
  rows: z.array(z.object({ country: requiredSlugField("a country"), from: fuzzyDateField, to: fuzzyDateField, note: z.string() })),
});
export type AdoptionsForm = z.infer<typeof adoptionsSchema>;

export const adoptionsForm = (list: Dto<"AdoptionInput">[]): AdoptionsForm => ({
  rows: list.map((a) => ({ country: a.country, from: fuzzyDateText(a.from), to: fuzzyDateText(a.to), note: a.note ?? "" })),
});

export const toAdoptionsInput = (form: AdoptionsForm): Dto<"AdoptionsInput"> => ({
  adoptions: form.rows.map((r) => ({ country: r.country.trim(), from: dateOrNull(r.from), to: dateOrNull(r.to), note: orNull(r.note) })),
});

// Relations ----------------------------------------------------------------------------------------------------

const RELATION_TYPES = {
  DerivedFrom: "",
  CopyOf: "",
  InfluencedBy: "",
  CompetedAgainst: "",
  Replaced: "",
} satisfies Record<Dto<"RelationType">, string>;

export const relationsSchema = z.object({
  rows: z.array(z.object({ type: enumOf(RELATION_TYPES), firearm: requiredSlugField("a firearm"), note: z.string() })),
});
export type RelationsForm = z.infer<typeof relationsSchema>;

export const relationsForm = (list: Dto<"RelationInput">[]): RelationsForm => ({
  rows: list.map((r) => ({ type: r.type, firearm: r.firearm, note: r.note ?? "" })),
});

export const toRelationsInput = (form: RelationsForm): Dto<"RelationsInput"> => ({
  relations: form.rows.map((r) => ({ type: r.type, firearm: r.firearm.trim(), note: orNull(r.note) })),
});

// Chamberings --------------------------------------------------------------------------------------------------

/** One primary at most, picked by index (a radio group across the rows); -1 is none. */
export const chamberingsSchema = z.object({
  rows: z.array(z.object({ cartridge: requiredSlugField("a cartridge") })),
  primary: z.string(),
});
export type ChamberingsForm = z.infer<typeof chamberingsSchema>;

export const chamberingsForm = (list: Dto<"ChamberingInput">[]): ChamberingsForm => ({
  rows: list.map((c) => ({ cartridge: c.cartridge })),
  primary: String(list.findIndex((c) => c.isPrimary)),
});

export const toChamberingsInput = (form: ChamberingsForm): Dto<"ChamberingsInput"> => ({
  chamberings: form.rows.map((r, i) => ({ cartridge: r.cartridge.trim(), isPrimary: String(i) === form.primary })),
});

// Variant details ----------------------------------------------------------------------------------------------

/** The variant number fields, in form order, with their labels (metric, as stored: CLAUDE.md). */
export const VARIANT_NUMBERS = [
  ["weightEmptyG", "Empty weight (g)"],
  ["overallLengthMm", "Overall length (mm)"],
  ["overallLengthFoldedMm", "Folded length (mm)"],
  ["barrelLengthMm", "Barrel length (mm)"],
  ["rateOfFireMinRpm", "Rate of fire, from (rounds/min)"],
  ["rateOfFireMaxRpm", "Rate of fire, to (rounds/min)"],
  ["muzzleVelocityMps", "Muzzle velocity (m/s)"],
  ["effectiveRangeM", "Effective range (m)"],
  ["magazineCapacityStandard", "Standard magazine (rounds)"],
] as const;

type NumberKey = (typeof VARIANT_NUMBERS)[number][0];

const extraSpecsField = z.string().superRefine((text, ctx) => {
  const bad = text.split("\n").find((line) => line.trim() !== "" && !/^[^:]+:/.test(line));
  if (bad !== undefined) {
    ctx.addIssue({ code: "custom", message: `Write each line as "Name: value" (not "${bad.trim()}").` });
  }
});

export const variantSchema = z.object({
  name: z.string().trim().min(1, "A variant needs a name."),
  slug: z.string().trim(),
  introducedDate: fuzzyDateField,
  ...(Object.fromEntries(VARIANT_NUMBERS.map(([key]) => [key, wholeNumberField])) as Record<NumberKey, typeof wholeNumberField>),
  firingModes: z.array(enumOf(FIRING_MODES)),
  extraSpecs: extraSpecsField,
});
export type VariantForm = z.infer<typeof variantSchema>;

export function variantForm(v: Dto<"VariantInput"> | null): VariantForm {
  return {
    name: v?.name ?? "",
    slug: v?.slug ?? "",
    introducedDate: fuzzyDateText(v?.introducedDate ?? null),
    ...(Object.fromEntries(VARIANT_NUMBERS.map(([key]) => [key, numberText(v?.[key] ?? null)])) as Record<NumberKey, string>),
    firingModes: v?.firingModes ?? [],
    extraSpecs: Object.entries(v?.extraSpecs ?? {})
      .map(([key, value]) => `${key}: ${value}`)
      .join("\n"),
  };
}

export function toVariantInput(form: VariantForm): Dto<"VariantInput"> {
  const extraSpecs = Object.fromEntries(
    form.extraSpecs
      .split("\n")
      .filter((line) => line.trim() !== "")
      .map((line) => {
        const colon = line.indexOf(":");
        return [line.slice(0, colon).trim(), line.slice(colon + 1).trim()];
      }),
  );
  return {
    name: form.name.trim(),
    slug: orNull(form.slug),
    introducedDate: dateOrNull(form.introducedDate),
    ...(Object.fromEntries(VARIANT_NUMBERS.map(([key]) => [key, numberOrNull(form[key])])) as Record<NumberKey, number | null>),
    firingModes: form.firingModes,
    extraSpecs: Object.keys(extraSpecs).length > 0 ? extraSpecs : null,
  };
}
