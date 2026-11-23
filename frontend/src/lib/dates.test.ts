import { describe, expect, it } from "vitest";
import { formatFuzzyDate, type FuzzyDate } from "./dates";

const date = (fields: Partial<FuzzyDate>): FuzzyDate => ({
  year: 1947,
  month: null,
  day: null,
  yearTo: null,
  precision: "Year",
  isApproximate: false,
  ...fields,
});

describe("formatFuzzyDate", () => {
  it("shows a date only as precisely as it is known", () => {
    expect(formatFuzzyDate(date({ year: 1855, month: 1, day: 23, precision: "Day" }))).toBe("23 January 1855");
    expect(formatFuzzyDate(date({ year: 1942, month: 3, precision: "Month" }))).toBe("March 1942");
    expect(formatFuzzyDate(date({}))).toBe("1947");
    expect(formatFuzzyDate(date({ year: 1950, yearTo: 1959, precision: "Decade" }))).toBe("1950s");
  });

  it("shows ranges and approximate dates", () => {
    expect(formatFuzzyDate(date({ year: 1942, yearTo: 1944 }))).toBe("1942–1944");
    expect(formatFuzzyDate(date({ isApproximate: true }))).toBe("c. 1947");
    expect(formatFuzzyDate(date({ year: 1950, yearTo: 1959, precision: "Decade", isApproximate: true }))).toBe("c. 1950s");
  });
});
