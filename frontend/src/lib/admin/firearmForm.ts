import { z } from "zod";
import type { Dto } from "@/lib/api/types";
import { FEED_SYSTEMS } from "@/lib/labels";
import { fuzzyDateText, parseFuzzyDate } from "./fuzzyDate";

type FirearmInput = Dto<"FirearmInput">;

/** A date field: the text must parse; the API checks the rest. */
const fuzzyDateField = z.string().superRefine((text, ctx) => {
  const parsed = parseFuzzyDate(text);
  if (!parsed.ok) {
    ctx.addIssue({ code: "custom", message: parsed.error });
  }
});

const feedSystems = Object.keys(FEED_SYSTEMS) as [keyof typeof FEED_SYSTEMS, ...(keyof typeof FEED_SYSTEMS)[]];

/**
 * The firearm details form. Shape and required fields only: everything else (references, slug rules, publish
 * requirements) is the API's, whose messages land on these same fields. Field names are the input's, which are
 * also the lock keys.
 */
export const firearmFormSchema = z.object({
  name: z.string().trim().min(1, "A firearm needs a name."),
  slug: z.string().trim(),
  alternativeNames: z.string(), // one per line
  wikidataId: z.string().trim().regex(/^(Q\d+)?$/, "A Wikidata id looks like Q12345."),
  country: z.string().trim(),
  mechanism: z.string().trim(),
  feedSystem: z.enum(feedSystems).or(z.literal("")),
  designDate: fuzzyDateField,
  productionStart: fuzzyDateField,
  productionEnd: fuzzyDateField,
  summary: z.string(),
  developmentHistory: z.string(),
  serviceHistory: z.string(),
  wikipediaTitle: z.string().trim(),
});

export type FirearmFormValues = z.infer<typeof firearmFormSchema>;

const orNull = (text: string) => (text.trim() === "" ? null : text.trim());

function dateOrNull(text: string) {
  const parsed = parseFuzzyDate(text);
  return parsed.ok ? parsed.date : null; // the schema has already rejected what does not parse
}

/** The form for a firearm's stored fields (blank for a new one). */
export function firearmFormValues(input: FirearmInput | null): FirearmFormValues {
  return {
    name: input?.name ?? "",
    slug: input?.slug ?? "",
    alternativeNames: (input?.alternativeNames ?? []).join("\n"),
    wikidataId: input?.wikidataId ?? "",
    country: input?.country ?? "",
    mechanism: input?.mechanism ?? "",
    feedSystem: input?.feedSystem ?? "",
    designDate: fuzzyDateText(input?.designDate ?? null),
    productionStart: fuzzyDateText(input?.productionStart ?? null),
    productionEnd: fuzzyDateText(input?.productionEnd ?? null),
    summary: input?.summary ?? "",
    developmentHistory: input?.developmentHistory ?? "",
    serviceHistory: input?.serviceHistory ?? "",
    wikipediaTitle: input?.wikipediaTitle ?? "",
  };
}

/**
 * The API input for a submitted form. An empty slug means "generate one" on create and "keep it" on update. The
 * Wikipedia revision is not in the form: it comes from the stored firearm and goes back unchanged.
 */
export function toFirearmInput(values: FirearmFormValues, wikipediaRevisionId: number | null): FirearmInput {
  return {
    slug: orNull(values.slug),
    name: values.name.trim(),
    alternativeNames: values.alternativeNames.split("\n").map((n) => n.trim()).filter(Boolean),
    wikidataId: orNull(values.wikidataId),
    country: orNull(values.country),
    mechanism: orNull(values.mechanism),
    feedSystem: values.feedSystem === "" ? null : values.feedSystem,
    designDate: dateOrNull(values.designDate),
    productionStart: dateOrNull(values.productionStart),
    productionEnd: dateOrNull(values.productionEnd),
    summary: orNull(values.summary),
    developmentHistory: orNull(values.developmentHistory),
    serviceHistory: orNull(values.serviceHistory),
    wikipediaTitle: orNull(values.wikipediaTitle),
    wikipediaRevisionId,
  };
}
