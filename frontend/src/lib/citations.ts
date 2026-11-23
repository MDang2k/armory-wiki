import type { Dto } from "./api/types";

export type Citation = Dto<"CitationDto">;

/**
 * Footnotes (PLAN.md §5): every citation gets a number in the order added and is listed under "Sources"; one tied
 * to a field also puts its number next to that field. A variant's field is keyed "<variant slug>/<field>".
 */
export function footnotes(citations: Citation[]) {
  const byField = new Map<string, number[]>();
  citations.forEach((c, i) => {
    if (c.fieldName) {
      const key = c.variant ? `${c.variant}/${c.fieldName}` : c.fieldName;
      byField.set(key, [...(byField.get(key) ?? []), i + 1]);
    }
  });

  return {
    /** The footnote numbers for a firearm field ("designDate") or a variant's ("ak-47/weightEmptyG"). */
    of: (field: string, variant?: string) => byField.get(variant ? `${variant}/${field}` : field) ?? [],
  };
}

export type Footnotes = ReturnType<typeof footnotes>;

/** Anchor id of footnote n in the Sources list. */
export const footnoteId = (n: number) => `cite-${n}`;
