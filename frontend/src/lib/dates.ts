import type { Dto } from "./api/types";

export type FuzzyDate = Dto<"FuzzyDateDto">;

const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

/**
 * A historical date as precisely as it is known (GLOSSARY.md: *design date*): "23 January 1855", "March 1942",
 * "1947", "1950s", a range "1942–1944", each with "c. " when approximate.
 */
export function formatFuzzyDate(date: FuzzyDate): string {
  const text = (() => {
    switch (date.precision) {
      case "Day":
        return date.month && date.day ? `${date.day} ${MONTHS[date.month - 1]} ${date.year}` : `${date.year}`;
      case "Month":
        return date.month ? `${MONTHS[date.month - 1]} ${date.year}` : `${date.year}`;
      case "Decade":
        return `${date.year}s`;
      case "Year":
        return date.yearTo && date.yearTo !== date.year ? `${date.year}–${date.yearTo}` : `${date.year}`;
    }
  })();
  return date.isApproximate ? `c. ${text}` : text;
}
