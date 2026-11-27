import { describe, expect, it } from "vitest";
import type { Dto } from "@/lib/api/types";
import {
  adoptionsForm,
  adoptionsSchema,
  chamberingsForm,
  chamberingsSchema,
  designersForm,
  designersSchema,
  manufacturersForm,
  manufacturersSchema,
  relationsForm,
  relationsSchema,
  tagsForm,
  tagsSchema,
  toAdoptionsInput,
  toChamberingsInput,
  toDesignersInput,
  toManufacturersInput,
  toRelationsInput,
  toTagsInput,
  toVariantInput,
  variantForm,
  variantSchema,
} from "./firearmParts";

const year = (y: number): Dto<"FuzzyDateDto"> => ({ year: y, month: null, day: null, yearTo: null, precision: "Year", isApproximate: false });

describe("firearm part forms round-trip what the API stored", () => {
  it("tags", () => {
    expect(toTagsInput(tagsSchema.parse(tagsForm(["assault-rifle", "cold-war"])))).toEqual({ tags: ["assault-rifle", "cold-war"] });
  });

  it("designers, a person and an organization", () => {
    const designers: Dto<"DesignerInput">[] = [
      { person: "mikhail-kalashnikov", organization: null, role: "LeadDesigner" },
      { person: null, organization: "izhmash", role: "Developer" },
    ];
    expect(toDesignersInput(designersSchema.parse(designersForm(designers)))).toEqual({ designers });
  });

  it("manufacturers", () => {
    const manufacturers: Dto<"ManufacturerInput">[] = [{ organization: "izhmash", from: year(1949), to: null, isLicensed: false }];
    expect(toManufacturersInput(manufacturersSchema.parse(manufacturersForm(manufacturers)))).toEqual({ manufacturers });
  });

  it("adoptions", () => {
    const adoptions: Dto<"AdoptionInput">[] = [{ country: "soviet-union", from: year(1949), to: year(1974), note: "Standard rifle." }];
    expect(toAdoptionsInput(adoptionsSchema.parse(adoptionsForm(adoptions)))).toEqual({ adoptions });
  });

  it("relations", () => {
    const relations: Dto<"RelationInput">[] = [{ type: "InfluencedBy", firearm: "stg-44", note: null }];
    expect(toRelationsInput(relationsSchema.parse(relationsForm(relations)))).toEqual({ relations });
  });

  it("chamberings, keeping which one is primary", () => {
    const chamberings: Dto<"ChamberingInput">[] = [
      { cartridge: "7-62x39mm", isPrimary: false },
      { cartridge: "5-45x39mm", isPrimary: true },
    ];
    expect(toChamberingsInput(chamberingsSchema.parse(chamberingsForm(chamberings)))).toEqual({ chamberings });
    expect(toChamberingsInput(chamberingsSchema.parse(chamberingsForm([{ cartridge: "x", isPrimary: false }])))).toEqual({
      chamberings: [{ cartridge: "x", isPrimary: false }],
    });
  });

  it("a variant, numbers, firing modes and extra specs included", () => {
    const variant: Dto<"VariantInput"> = {
      slug: "akms",
      name: "AKMS",
      introducedDate: year(1959),
      weightEmptyG: 3100,
      overallLengthMm: 880,
      overallLengthFoldedMm: 640,
      barrelLengthMm: 415,
      rateOfFireMinRpm: 600,
      rateOfFireMaxRpm: null,
      muzzleVelocityMps: 715,
      effectiveRangeM: null,
      magazineCapacityStandard: 30,
      firingModes: ["SemiAutomatic", "Automatic"],
      extraSpecs: { Stock: "underfolding steel", "Sight radius": "378 mm" },
    };
    expect(toVariantInput(variantSchema.parse(variantForm(variant)))).toEqual(variant);
  });
});

describe("firearm part forms reject on the field", () => {
  it("a row without its entity", () => {
    expect(designersSchema.safeParse({ rows: [{ kind: "person", slug: " ", role: "LeadDesigner" }] }).success).toBe(false);
  });

  it("a bad date in a row", () => {
    const result = adoptionsSchema.safeParse({ rows: [{ country: "x", from: "spring 1949", to: "", note: "" }] });
    expect(result.error?.issues[0].path).toEqual(["rows", 0, "from"]);
  });

  it("variant numbers and extra spec lines", () => {
    const result = variantSchema.safeParse({ ...variantForm(null), name: "X", weightEmptyG: "3.1 kg", extraSpecs: "Stock: wood\nno colon" });
    expect(result.error?.issues.map((i) => i.path[0]).sort()).toEqual(["extraSpecs", "weightEmptyG"]);
  });

  it("an empty variant sends nulls, not empty strings", () => {
    expect(toVariantInput(variantSchema.parse({ ...variantForm(null), name: "X" }))).toMatchObject({
      slug: null,
      introducedDate: null,
      weightEmptyG: null,
      firingModes: [],
      extraSpecs: null,
    });
  });
});
