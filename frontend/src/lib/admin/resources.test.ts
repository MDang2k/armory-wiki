import { describe, expect, it } from "vitest";
import type { Dto } from "@/lib/api/types";
import { RESOURCES, isResourceRoute, resourceFormValues, resourceSchema, sourceSpec, toResourceInput, type ResourceSpec } from "./resources";

const year = (y: number): Dto<"FuzzyDateDto"> => ({ year: y, month: null, day: null, yearTo: null, precision: "Year", isApproximate: false });

function roundTrip<I>(spec: ResourceSpec<I>, input: I) {
  return toResourceInput<I>(spec, resourceSchema(spec).parse(resourceFormValues(spec, input)));
}

describe("resource forms round-trip what the API stored", () => {
  it("a person", () => {
    const person: Dto<"PersonInput"> = {
      slug: "mikhail-kalashnikov",
      fullName: "Mikhail Kalashnikov",
      birthDate: year(1919),
      deathDate: { year: 2013, month: 12, day: 23, yearTo: null, precision: "Day", isApproximate: false },
      nationality: "soviet-union",
      biography: "A **designer**.\n\nSecond paragraph.",
      wikidataId: "Q182237",
    };
    expect(roundTrip(RESOURCES.people, person)).toEqual(person);
  });

  it("an organization", () => {
    const org: Dto<"OrganizationInput"> = {
      slug: "izhmash",
      name: "Izhmash",
      type: "StateArsenal",
      country: "russia",
      founded: year(1807),
      description: null,
      wikidataId: null,
    };
    expect(roundTrip(RESOURCES.organizations, org)).toEqual(org);
  });

  it("a cartridge, with decimals and alternative names", () => {
    const cartridge: Dto<"CartridgeInput"> = {
      slug: "7-62x39mm",
      name: "7.62×39mm",
      alternativeNames: ["M43"],
      wikidataId: "Q463497",
      type: "Intermediate",
      bulletDiameterMm: 7.92,
      caseLengthMm: 38.7,
      overallLengthMm: 56,
      parent: null,
      designDate: year(1943),
      country: "soviet-union",
      description: null,
    };
    expect(roundTrip(RESOURCES.cartridges, cartridge)).toEqual(cartridge);
  });

  it("a country, a tag, a mechanism and a source", () => {
    const country: Dto<"CountryInput"> = { slug: "soviet-union", name: "Soviet Union", isoCode: null, isHistorical: true, wikidataId: "Q15180" };
    const tag: Dto<"TagInput"> = { slug: "cold-war", name: "Cold War", group: "Era" };
    const mechanism: Dto<"MechanismInput"> = { slug: "long-stroke", name: "Long-stroke piston", parent: "gas-operated", explanation: null };
    const source: Dto<"SourceInput"> = { type: "Book", title: "The AK-47", author: "G. Rottman", publisher: null, year: 2011, url: null, isbn: null };
    expect(roundTrip(RESOURCES.countries, country)).toEqual(country);
    expect(roundTrip(RESOURCES.tags, tag)).toEqual(tag);
    expect(roundTrip(RESOURCES.mechanisms, mechanism)).toEqual(mechanism);
    expect(roundTrip(sourceSpec, source)).toEqual(source);
  });
});

describe("resource forms", () => {
  it("send nulls for empty fields and read a decimal comma", () => {
    const values = { ...resourceFormValues(RESOURCES.cartridges, null), name: "Probe", bulletDiameterMm: "7,92" };
    expect(toResourceInput(RESOURCES.cartridges, resourceSchema(RESOURCES.cartridges).parse(values))).toMatchObject({
      name: "Probe",
      slug: null,
      alternativeNames: [],
      type: null,
      bulletDiameterMm: 7.92,
      designDate: null,
      description: null,
    });
  });

  it("reject on the field", () => {
    const result = resourceSchema(RESOURCES.tags).safeParse({ ...resourceFormValues(RESOURCES.tags, null), name: " " });
    expect(result.error?.issues.map((i) => i.path[0]).sort()).toEqual(["group", "name"]);
  });

  it("know their routes", () => {
    expect(isResourceRoute("people")).toBe(true);
    expect(isResourceRoute("firearms")).toBe(false);
    expect(isResourceRoute("toString")).toBe(false);
  });
});
