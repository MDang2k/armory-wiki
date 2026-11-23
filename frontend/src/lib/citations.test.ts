import { describe, expect, it } from "vitest";
import { footnotes, type Citation } from "./citations";
import { wikipediaUrl } from "./wikipedia";

const source = { type: "Book", title: "A book", author: null, publisher: null, year: null, url: null, isbn: null } as const;
const cite = (fieldName: string | null, variant: string | null = null): Citation =>
  ({ source, fieldName, locator: null, variant }) as Citation;

describe("footnotes", () => {
  it("numbers citations in order and finds a field's numbers", () => {
    const notes = footnotes([cite(null), cite("designDate"), cite("weightEmptyG", "ak-47"), cite("designDate")]);

    expect(notes.of("designDate")).toEqual([2, 4]);
    expect(notes.of("weightEmptyG", "ak-47")).toEqual([3]);
    expect(notes.of("weightEmptyG", "akm")).toEqual([]);
    expect(notes.of("weightEmptyG")).toEqual([]); // a variant's field is not the firearm's
  });
});

describe("wikipediaUrl", () => {
  it("links the exact revision when known", () => {
    expect(wikipediaUrl("M1 Garand", 1386668058)).toBe("https://en.wikipedia.org/w/index.php?title=M1_Garand&oldid=1386668058");
    expect(wikipediaUrl("Mosin–Nagant", null)).toBe("https://en.wikipedia.org/wiki/Mosin%E2%80%93Nagant");
  });
});
