import { describe, expect, it } from "vitest";
import type { FuzzyDate } from "@/lib/dates";
import { fuzzyDateText, parseFuzzyDate } from "./fuzzyDate";

const date = (d: Partial<FuzzyDate> & Pick<FuzzyDate, "year" | "precision">): FuzzyDate => ({
  month: null,
  day: null,
  yearTo: null,
  isApproximate: false,
  ...d,
});

describe("parseFuzzyDate", () => {
  it.each([
    ["1942", date({ year: 1942, precision: "Year" })],
    ["1942-03", date({ year: 1942, month: 3, precision: "Month" })],
    ["1942-3-15", date({ year: 1942, month: 3, day: 15, precision: "Day" })],
    ["1940s", date({ year: 1940, yearTo: 1949, precision: "Decade" })],
    ["1942–1944", date({ year: 1942, yearTo: 1944, precision: "Year" })],
    ["1942 - 1944", date({ year: 1942, yearTo: 1944, precision: "Year" })],
    ["c. 1940s", date({ year: 1940, yearTo: 1949, precision: "Decade", isApproximate: true })],
    ["circa 1855", date({ year: 1855, precision: "Year", isApproximate: true })],
    ["  ", null],
  ])("reads %j", (text, expected) => {
    expect(parseFuzzyDate(text)).toEqual({ ok: true, date: expected });
  });

  it.each(["1945s", "1942-13", "1942-02-30", "1944–1942", "2101", "March 1942", "42", "c."])("rejects %j", (text) => {
    expect(parseFuzzyDate(text).ok).toBe(false);
  });

  it("knows leap years", () => {
    expect(parseFuzzyDate("1944-02-29").ok).toBe(true);
    expect(parseFuzzyDate("1943-02-29").ok).toBe(false);
  });
});

describe("fuzzyDateText", () => {
  it.each(["1942", "1942-03", "1942-03-15", "1940s", "1942–1944", "c. 1855", "c. 1940s"])("round-trips %j", (text) => {
    const parsed = parseFuzzyDate(text);
    expect(parsed.ok && fuzzyDateText(parsed.date)).toBe(text);
  });

  it("is empty for no date", () => {
    expect(fuzzyDateText(null)).toBe("");
  });
});
