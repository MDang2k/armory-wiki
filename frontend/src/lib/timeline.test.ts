import { describe, expect, it } from "vitest";
import { packLanes, parseTimelineFilters, timelineQuery, yearTicks } from "./timeline";

describe("parseTimelineFilters", () => {
  it("defaults to 1900–1950 with no filters", () => {
    expect(parseTimelineFilters({})).toEqual({ from: 1900, to: 1950, country: "", tag: "" });
  });

  it("reads years and slugs, turning a reversed range round", () => {
    expect(parseTimelineFilters({ from: "1960", to: "1940", country: "germany", tag: ["assault-rifle"] })).toEqual({
      from: 1940,
      to: 1960,
      country: "germany",
      tag: "assault-rifle",
    });
  });

  it("ignores years out of range and anything that is not a slug", () => {
    expect(parseTimelineFilters({ from: "12", to: "9999", country: "Germany!", tag: "a b" })).toEqual({
      from: 1900,
      to: 1950,
      country: "",
      tag: "",
    });
  });
});

describe("timelineQuery", () => {
  it("leaves out defaults and empty filters", () => {
    expect(timelineQuery({ from: 1900, to: 1950, country: "", tag: "" })).toBe("");
    expect(timelineQuery({ from: 1930, to: 1950, country: "germany", tag: "" })).toBe("from=1930&country=germany");
  });
});

describe("packLanes", () => {
  const lanes = (spans: [number, number][]) => packLanes(spans, (s) => s).map(({ item, lane }) => [item, lane]);

  it("shares a row when spans don't overlap and opens one when they do", () => {
    expect(lanes([[0, 10], [10, 20], [5, 15], [20, 30], [12, 14]])).toEqual([
      [[0, 10], 0],
      [[5, 15], 1],
      [[10, 20], 0],
      [[12, 14], 2],
      [[20, 30], 0],
    ]);
  });

  it("never puts two overlapping spans in one row", () => {
    const spans: [number, number][] = Array.from({ length: 60 }, (_, i) => [(i * 7) % 50, ((i * 7) % 50) + 3 + (i % 9)]);
    const placed = packLanes(spans, (s) => s);
    for (const a of placed) {
      for (const b of placed) {
        if (a !== b && a.lane === b.lane) {
          expect(a.item[1] <= b.item[0] || b.item[1] <= a.item[0]).toBe(true);
        }
      }
    }
  });
});

describe("yearTicks", () => {
  it("marks decades, or five-year steps on short ranges", () => {
    expect(yearTicks(1900, 1950)).toEqual([1900, 1910, 1920, 1930, 1940, 1950]);
    expect(yearTicks(1903, 1918)).toEqual([1905, 1910, 1915]);
    expect(yearTicks(1800, 2000)).toEqual([1800, 1825, 1850, 1875, 1900, 1925, 1950, 1975, 2000]);
  });
});
