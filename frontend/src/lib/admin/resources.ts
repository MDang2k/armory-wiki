import { z } from "zod";
import type { Dto } from "@/lib/api/types";
import { CARTRIDGE_TYPES, ORGANIZATION_TYPES } from "@/lib/labels";
import { dateOrNull, fuzzyDateField, orNull, wholeNumberField } from "./formFields";
import { fuzzyDateText } from "./fuzzyDate";

/** The admin lists a slug picker can search; each answers pages of items with an id, slug, name and status. */
export type PickerResource = "countries" | "mechanisms" | "people" | "organizations" | "cartridges" | "tags" | "firearms";

/** How one input field is edited. The kind decides the form control, the check and the conversion both ways. */
export type FieldSpec = { label: string; hint?: string } & (
  | { kind: "text"; required?: boolean } // string (required) or string | null
  | { kind: "slug" | "wikidata" | "lines" | "date" | "markdown" | "decimal" | "whole" | "checkbox" }
  | { kind: "pick"; resource: PickerResource }
  | { kind: "select"; options: Record<string, string>; required?: boolean }
);

/**
 * One admin resource edited by the generic list and editor (PLAN.md Phase 7c). `fields` names every field of the
 * API's input, so an input field added or removed in the API fails `tsc` here until the form follows.
 */
export type ResourceSpec<I> = {
  title: string;
  plural: string;
  /** The input field holding the display name. */
  nameField: keyof I & string;
  publishable: boolean;
  /** Takes citations (`/api/admin/<route>/{id}/citations`). */
  cited: boolean;
  /** Its public page, when it has one: `/<path>/<slug>`. */
  publicPath?: string;
  fields: { [K in keyof I]-?: FieldSpec };
};

/** Any resource spec, as the generic list, editor and form handle them. */
export type AnyResourceSpec = Omit<ResourceSpec<never>, "nameField" | "fields"> & { nameField: string; fields: Record<string, FieldSpec> };

export type ResourceFormValues = Record<string, string | boolean>;

const SOURCE_TYPES: Record<Dto<"SourceType">, string> = {
  Book: "Book",
  Article: "Article",
  Website: "Website",
  Patent: "Patent",
  Manual: "Manual",
  Video: "Video",
  Dataset: "Dataset",
};

const TAG_GROUPS: Record<Dto<"TagGroup">, string> = { Type: "Type", Role: "Role", Era: "Era" };

const slug: FieldSpec = { kind: "slug", label: "Slug", hint: "Leave empty to make one from the name. The old address keeps working after a change." };
const wikidataId: FieldSpec = { kind: "wikidata", label: "Wikidata id", hint: "Like Q12345. Imports match by it." };

const people: ResourceSpec<Dto<"PersonInput">> = {
  title: "Person",
  plural: "People",
  nameField: "fullName",
  publishable: true,
  cited: true,
  publicPath: "people",
  fields: {
    fullName: { kind: "text", label: "Full name", required: true },
    slug,
    birthDate: { kind: "date", label: "Born" },
    deathDate: { kind: "date", label: "Died" },
    nationality: { kind: "pick", resource: "countries", label: "Nationality", hint: "A country slug." },
    wikidataId,
    biography: { kind: "markdown", label: "Biography" },
  },
};

const organizations: ResourceSpec<Dto<"OrganizationInput">> = {
  title: "Organization",
  plural: "Organizations",
  nameField: "name",
  publishable: true,
  cited: true,
  publicPath: "organizations",
  fields: {
    name: { kind: "text", label: "Name", required: true },
    slug,
    type: { kind: "select", label: "Type", options: ORGANIZATION_TYPES },
    country: { kind: "pick", resource: "countries", label: "Country", hint: "A country slug." },
    founded: { kind: "date", label: "Founded" },
    wikidataId,
    description: { kind: "markdown", label: "Description" },
  },
};

const cartridges: ResourceSpec<Dto<"CartridgeInput">> = {
  title: "Cartridge",
  plural: "Cartridges",
  nameField: "name",
  publishable: true,
  cited: true,
  publicPath: "cartridges",
  fields: {
    name: { kind: "text", label: "Name", required: true },
    slug,
    alternativeNames: { kind: "lines", label: "Alternative names", hint: "One per line." },
    type: { kind: "select", label: "Type", options: CARTRIDGE_TYPES },
    country: { kind: "pick", resource: "countries", label: "Country of origin", hint: "A country slug." },
    designDate: { kind: "date", label: "Design date" },
    parent: { kind: "pick", resource: "cartridges", label: "Parent cartridge", hint: "The case it was developed from." },
    bulletDiameterMm: { kind: "decimal", label: "Bullet diameter (mm)" },
    caseLengthMm: { kind: "decimal", label: "Case length (mm)" },
    overallLengthMm: { kind: "decimal", label: "Overall length (mm)" },
    wikidataId,
    description: { kind: "markdown", label: "Description" },
  },
};

const countries: ResourceSpec<Dto<"CountryInput">> = {
  title: "Country",
  plural: "Countries",
  nameField: "name",
  publishable: false,
  cited: false,
  fields: {
    name: { kind: "text", label: "Name", required: true },
    slug,
    isoCode: { kind: "text", label: "ISO code", hint: "Two letters; empty for a historical state without one." },
    isHistorical: { kind: "checkbox", label: "Historical state (no longer exists)" },
    wikidataId,
  },
};

const tags: ResourceSpec<Dto<"TagInput">> = {
  title: "Tag",
  plural: "Tags",
  nameField: "name",
  publishable: false,
  cited: false,
  fields: {
    name: { kind: "text", label: "Name", required: true },
    slug,
    group: { kind: "select", label: "Group", options: TAG_GROUPS, required: true },
  },
};

const mechanisms: ResourceSpec<Dto<"MechanismInput">> = {
  title: "Mechanism",
  plural: "Mechanisms",
  nameField: "name",
  publishable: false,
  cited: false,
  publicPath: "mechanisms",
  fields: {
    name: { kind: "text", label: "Name", required: true },
    slug,
    parent: { kind: "pick", resource: "mechanisms", label: "Parent mechanism", hint: "The broader mechanism it is a kind of." },
    explanation: { kind: "markdown", label: "Explanation" },
  },
};

/** Sources share the editor; they have no slug, status or locks, and list on their own page. */
export const sourceSpec: ResourceSpec<Dto<"SourceInput">> = {
  title: "Source",
  plural: "Sources",
  nameField: "title",
  publishable: false,
  cited: false,
  fields: {
    title: { kind: "text", label: "Title", required: true },
    type: { kind: "select", label: "Type", options: SOURCE_TYPES },
    author: { kind: "text", label: "Author" },
    publisher: { kind: "text", label: "Publisher" },
    year: { kind: "whole", label: "Year" },
    url: { kind: "text", label: "Web address", hint: "For a website or an online copy." },
    isbn: { kind: "text", label: "ISBN" },
  },
};

/** The resources under `/admin/<route>` (firearms and sources have pages of their own). */
export const RESOURCES = { people, organizations, cartridges, countries, tags, mechanisms } as const;
export type ResourceRoute = keyof typeof RESOURCES;

export function isResourceRoute(route: string): route is ResourceRoute {
  return Object.hasOwn(RESOURCES, route);
}

// Forms --------------------------------------------------------------------------------------------------------

/** The zod schema for a resource's form: shape and required fields only; the API checks the rest. */
export function resourceSchema(spec: AnyResourceSpec) {
  const shape: Record<string, z.ZodType> = {};
  for (const [key, f] of Object.entries(spec.fields) as [string, FieldSpec][]) {
    shape[key] =
      f.kind === "checkbox"
        ? z.boolean()
        : f.kind === "date"
          ? fuzzyDateField
          : f.kind === "whole"
            ? wholeNumberField
            : f.kind === "decimal"
              ? z.string().trim().regex(/^(\d+([.,]\d+)?)?$/, "A number like 7.92, or empty.")
              : f.kind === "wikidata"
                ? z.string().trim().regex(/^(Q\d+)?$/, "A Wikidata id looks like Q12345.")
                : (f.kind === "text" || f.kind === "select") && f.required
                  ? z.string().trim().min(1, `${f.label} is required.`)
                  : z.string();
  }

  return z.object(shape) as unknown as z.ZodType<ResourceFormValues, ResourceFormValues>;
}

/** The form for a stored input (blank for a new entry). */
export function resourceFormValues(spec: AnyResourceSpec, input: unknown): ResourceFormValues {
  const values: ResourceFormValues = {};
  for (const [key, f] of Object.entries(spec.fields) as [string, FieldSpec][]) {
    const value = input ? (input as Record<string, unknown>)[key] : null;
    values[key] =
      f.kind === "checkbox"
        ? value === true
        : f.kind === "date"
          ? fuzzyDateText((value as Dto<"FuzzyDateDto"> | null) ?? null)
          : f.kind === "lines"
            ? ((value as string[] | null) ?? []).join("\n")
            : value === null || value === undefined
              ? ""
              : String(value);
  }

  return values;
}

/** The API input for a submitted form: empty fields become null, numbers numbers, dates dates. */
export function toResourceInput<I = unknown>(spec: AnyResourceSpec, values: ResourceFormValues): I {
  const input: Record<string, unknown> = {};
  for (const [key, f] of Object.entries(spec.fields) as [string, FieldSpec][]) {
    const value = values[key];
    const text = typeof value === "string" ? value : "";
    input[key] =
      f.kind === "checkbox"
        ? value === true
        : f.kind === "date"
          ? dateOrNull(text)
          : f.kind === "lines"
            ? text.split("\n").map((l) => l.trim()).filter(Boolean)
            : f.kind === "whole" || f.kind === "decimal"
              ? text.trim() === "" ? null : Number(text.trim().replace(",", "."))
              : f.kind === "text" && f.required
                ? text.trim()
                : f.kind === "markdown"
                  ? text.trim() === "" ? null : text
                  : orNull(text);
  }

  return input as I;
}
