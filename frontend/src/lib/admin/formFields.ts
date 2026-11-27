import { z } from "zod";
import { parseFuzzyDate } from "./fuzzyDate";

/** A date field: the text must parse; the API checks the rest. */
export const fuzzyDateField = z.string().superRefine((text, ctx) => {
  const parsed = parseFuzzyDate(text);
  if (!parsed.ok) {
    ctx.addIssue({ code: "custom", message: parsed.error });
  }
});

/** A whole-number field: digits or empty. */
export const wholeNumberField = z.string().trim().regex(/^\d*$/, "A whole number, or empty.");

/** A slug field that must be filled in. */
export const requiredSlugField = (what: string) => z.string().trim().min(1, `Pick ${what}.`);

export const orNull = (text: string) => (text.trim() === "" ? null : text.trim());

export const numberOrNull = (text: string) => (text.trim() === "" ? null : Number(text.trim()));

export const numberText = (value: number | null) => (value === null ? "" : String(value));

/** The date of a field the schema has already checked: unparsable text never gets this far. */
export function dateOrNull(text: string) {
  const parsed = parseFuzzyDate(text);
  return parsed.ok ? parsed.date : null;
}
