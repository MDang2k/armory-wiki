import { describe, expect, it } from "vitest";
import { compareHref, parseCompareSlugs } from "./compare";

describe("parseCompareSlugs", () => {
  it("keeps the order, drops duplicates and invalid slugs, and stops at four", () => {
    expect(parseCompareSlugs("ak-47,m16,ak-47,Bad Slug,m14,sks,akm")).toEqual(["ak-47", "m16", "m14", "sks"]);
    expect(parseCompareSlugs(["ak-47", "m1-garand"])).toEqual(["ak-47", "m1-garand"]);
    expect(parseCompareSlugs(undefined)).toEqual([]);
  });

  it("builds the URL back", () => {
    expect(compareHref(["ak-47", "m16"])).toBe("/compare?slugs=ak-47,m16");
    expect(compareHref([])).toBe("/compare");
  });
});
