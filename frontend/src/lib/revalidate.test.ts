import { describe, expect, it } from "vitest";
import { isAuthorized, parseTags } from "./revalidate";

describe("isAuthorized", () => {
  it("needs the exact secret, and a configured one", () => {
    expect(isAuthorized("s3cret", "s3cret")).toBe(true);
    expect(isAuthorized("s3cre", "s3cret")).toBe(false);
    expect(isAuthorized(null, "s3cret")).toBe(false);
    expect(isAuthorized("", undefined)).toBe(false);
    expect(isAuthorized("anything", "")).toBe(false);
  });
});

describe("parseTags", () => {
  it("accepts a list of tags and drops duplicates", () => {
    expect(parseTags({ tags: ["firearms", "firearm:ak-47", "firearms"] })).toEqual(["firearms", "firearm:ak-47"]);
  });

  it("rejects anything else", () => {
    expect(parseTags(null)).toBeNull();
    expect(parseTags({ tags: "all" })).toBeNull();
    expect(parseTags({ tags: [] })).toBeNull();
    expect(parseTags({ tags: [1] })).toBeNull();
    expect(parseTags({ tags: ["x".repeat(257)] })).toBeNull();
    expect(parseTags({ tags: Array(101).fill("t") })).toBeNull();
  });
});
