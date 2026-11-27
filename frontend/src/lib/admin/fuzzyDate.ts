import type { FuzzyDate } from "@/lib/dates";

export type ParsedFuzzyDate = { ok: true; date: FuzzyDate | null } | { ok: false; error: string };

const HINT = "Write a date like 1942, 1942-03, 1942-03-15, 1940s or 1942–1944, with c. in front when approximate.";
const MAX_YEAR = 2100; // the API's limit

/**
 * Reads the admin's date field (PLAN.md Phase 7): `1942`, `1942-03`, `1942-03-15`, `1940s`, `1942–1944` (`-` or
 * `–`), each optionally after `c.` / `ca.` / `circa`. An empty field is no date. Years have three or four digits,
 * which keeps "1942-03" (a month) apart from "1942-1944" (a range).
 */
export function parseFuzzyDate(text: string): ParsedFuzzyDate {
  let rest = text.trim();
  if (rest === "") {
    return { ok: true, date: null };
  }

  const circa = /^(c\.|ca\.|circa)\s*/i.exec(rest);
  const isApproximate = circa !== null;
  if (circa) {
    rest = rest.slice(circa[0].length);
  }

  const base = { month: null, day: null, yearTo: null, isApproximate };
  const fail = (error = HINT): ParsedFuzzyDate => ({ ok: false, error });
  const yearOk = (year: number) => year >= 1 && year <= MAX_YEAR;
  let m: RegExpExecArray | null;

  if ((m = /^(\d{3,4})$/.exec(rest))) {
    const year = Number(m[1]);
    return yearOk(year) ? { ok: true, date: { ...base, year, precision: "Year" } } : fail(`Years go up to ${MAX_YEAR}.`);
  }

  if ((m = /^(\d{3,4})s$/.exec(rest))) {
    const year = Number(m[1]);
    if (year % 10 !== 0) {
      return fail("A decade ends in 0s, like 1940s.");
    }

    return yearOk(year + 9)
      ? { ok: true, date: { ...base, year, yearTo: year + 9, precision: "Decade" } }
      : fail(`Years go up to ${MAX_YEAR}.`);
  }

  if ((m = /^(\d{3,4})\s*[-–]\s*(\d{3,4})$/.exec(rest))) {
    const [year, yearTo] = [Number(m[1]), Number(m[2])];
    if (!yearOk(year) || !yearOk(yearTo)) {
      return fail(`Years go up to ${MAX_YEAR}.`);
    }

    if (yearTo <= year) {
      return fail("A range ends after it starts.");
    }

    return { ok: true, date: { ...base, year, yearTo, precision: "Year" } };
  }

  if ((m = /^(\d{3,4})-(\d{1,2})(?:-(\d{1,2}))?$/.exec(rest))) {
    const [year, month] = [Number(m[1]), Number(m[2])];
    if (!yearOk(year)) {
      return fail(`Years go up to ${MAX_YEAR}.`);
    }

    if (month < 1 || month > 12) {
      return fail("Months go from 01 to 12.");
    }

    if (m[3] === undefined) {
      return { ok: true, date: { ...base, year, month, precision: "Month" } };
    }

    const day = Number(m[3]);
    const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
    if (day < 1 || day > daysInMonth) {
      return fail(`That month has ${daysInMonth} days.`);
    }

    return { ok: true, date: { ...base, year, month, day, precision: "Day" } };
  }

  return fail();
}

/** The field text for a stored date, which {@link parseFuzzyDate} reads back to the same date. */
export function fuzzyDateText(date: FuzzyDate | null): string {
  if (!date) {
    return "";
  }

  const pad = (n: number | null) => String(n ?? 0).padStart(2, "0");
  const text = (() => {
    switch (date.precision) {
      case "Day":
        return `${date.year}-${pad(date.month)}-${pad(date.day)}`;
      case "Month":
        return `${date.year}-${pad(date.month)}`;
      case "Decade":
        return `${date.year}s`;
      case "Year":
        return date.yearTo && date.yearTo !== date.year ? `${date.year}–${date.yearTo}` : `${date.year}`;
    }
  })();
  return date.isApproximate ? `c. ${text}` : text;
}
