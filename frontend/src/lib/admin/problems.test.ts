import { describe, expect, it } from "vitest";
import { ApiProblem, fieldErrors, isVersionConflict, problemField, unwrap } from "./problems";

describe("fieldErrors", () => {
  it("maps C# property paths to camelCase input fields and joins their messages", () => {
    expect(
      fieldErrors({
        Name: ["'Name' must not be empty."],
        "DesignDate.Year": ["Year is out of range."],
        "DesignDate.Precision": ["Precision must match."],
        "AlternativeNames[0]": ["Too long."],
      }),
    ).toEqual({
      name: "'Name' must not be empty.",
      designDate: "Year is out of range. Precision must match.",
      alternativeNames: "Too long.",
    });
  });
});

describe("problemField", () => {
  it.each([
    ["Unknown country", "country"],
    ["Unknown mechanism", "mechanism"],
    ["Slug taken", "slug"],
    ["Wikidata id taken", "wikidataId"],
    ["Already in use", null],
  ])("puts %j on %j", (title, field) => {
    expect(problemField(new ApiProblem(400, title))).toBe(field);
  });
});

describe("isVersionConflict", () => {
  it("tells a stale version from other conflicts", () => {
    expect(isVersionConflict(new ApiProblem(409, "Version conflict"))).toBe(true);
    expect(isVersionConflict(new ApiProblem(409, "Slug taken"))).toBe(false);
    expect(isVersionConflict(new Error("Version conflict"))).toBe(false);
  });
});

describe("unwrap", () => {
  it("returns the data of a success", () => {
    expect(unwrap({ data: { id: 1 }, response: new Response(null, { status: 200 }) })).toEqual({ id: 1 });
  });

  it("throws the problem of a failure", () => {
    const failure = () =>
      unwrap({
        error: { title: "Stale version", detail: "Reload.", errors: null },
        response: new Response(null, { status: 409 }),
      });
    expect(failure).toThrow(ApiProblem);
    expect(failure).toThrow("Stale version: Reload.");
  });

  it("names a failure without a problem body by its status", () => {
    expect.assertions(1);
    try {
      unwrap({ error: "", response: new Response(null, { status: 502 }) });
    } catch (e) {
      expect(e).toMatchObject({ status: 502, title: "Request failed (502)", errors: {} });
    }
  });
});
