import { describe, expect, it } from "vitest";
import { parseFilters, toApiQuery, toggle, toQueryString, withFilters, withPage } from "./filters";

describe("parseFilters", () => {
  it("normalizes: sorted, deduplicated, invalid values dropped", () => {
    const filters = parseFilters({
      tag: "carbine,assault-rifle,carbine,Bad Slug,",
      country: ["soviet-union", "russia"],
      q: "  kalashnikov  ",
      decade: "1947",
      sort: "price",
      page: "-3",
    });

    expect(filters.tag).toEqual(["assault-rifle", "carbine"]);
    expect(filters.country).toEqual(["russia", "soviet-union"]);
    expect(filters.q).toBe("kalashnikov");
    expect(filters.decade).toBeNull(); // not a decade
    expect(filters.sort).toBeNull();
    expect(filters.page).toBe(1);
  });

  it("reads a decade, a sort and a page", () => {
    const filters = parseFilters({ decade: "1940", sort: "-designYear", page: "3" });

    expect([filters.decade, filters.sort, filters.page]).toEqual([1940, "-designYear", 3]);
  });

  it("caps absurd page numbers", () => {
    expect(parseFilters({ page: "99999999999" }).page).toBe(10_000);
  });
});

describe("round trip", () => {
  it("gives one canonical URL for equal filters", () => {
    const a = toQueryString(parseFilters({ country: "russia,soviet-union", tag: "carbine" }));
    const b = toQueryString(parseFilters({ tag: "carbine", country: "soviet-union,russia" }));

    expect(a).toBe(b);
    expect(a).toBe("?tag=carbine&country=russia%2Csoviet-union");
  });

  it("survives parse → string → parse", () => {
    const filters = parseFilters({ q: "m1 garand", tag: "rifle", mechanism: "gas-operated", decade: "1930", page: "2" });
    const again = parseFilters(Object.fromEntries(new URLSearchParams(toQueryString(filters))));

    expect(again).toEqual(filters);
  });

  it("is empty when nothing is filtered", () => {
    expect(toQueryString(parseFilters({}))).toBe("");
  });
});

describe("changing filters", () => {
  const filters = withPage(parseFilters({ tag: "carbine" }), 4);

  it("toggles a value and goes back to the first page", () => {
    expect(toggle(filters, "tag", "assault-rifle")).toMatchObject({ tag: ["assault-rifle", "carbine"], page: 1 });
    expect(toggle(filters, "tag", "carbine")).toMatchObject({ tag: [], page: 1 });
    expect(withFilters(filters, { q: "ak" }).page).toBe(1);
  });
});

describe("toApiQuery", () => {
  it("joins lists and turns a decade into a design-year range", () => {
    const query = toApiQuery(parseFilters({ tag: "rifle,carbine", decade: "1940", page: "2" }));

    expect(query).toEqual({ tag: "carbine,rifle", designedFrom: 1940, designedTo: 1949, page: 2, pageSize: 24 });
  });
});
