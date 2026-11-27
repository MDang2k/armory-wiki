import { describe, expect, it } from "vitest";
import type { Dto } from "@/lib/api/types";
import { firearmFormSchema, firearmFormValues, toFirearmInput } from "./firearmForm";

const stored: Dto<"FirearmInput"> = {
  slug: "stg-44",
  name: "StG 44",
  alternativeNames: ["MP 43", "MP 44"],
  wikidataId: "Q155176",
  country: "germany",
  designDate: { year: 1942, month: null, day: null, yearTo: null, precision: "Year", isApproximate: false },
  productionStart: { year: 1943, month: 3, day: null, yearTo: null, precision: "Month", isApproximate: false },
  productionEnd: null,
  mechanism: "gas-operated-long-stroke",
  feedSystem: "DetachableBoxMagazine",
  summary: "A selective-fire rifle.",
  developmentHistory: null,
  serviceHistory: null,
  wikipediaTitle: "StG 44",
  wikipediaRevisionId: 123,
};

describe("firearm form", () => {
  it("round-trips a stored firearm unchanged", () => {
    const values = firearmFormSchema.parse(firearmFormValues(stored));
    expect(toFirearmInput(values, stored.wikipediaRevisionId)).toEqual(stored);
  });

  it("turns blank fields into nulls and one name per line into a list", () => {
    const values = firearmFormSchema.parse({
      ...firearmFormValues(null),
      name: "  Probe ",
      alternativeNames: "One\n\n  Two  \n",
    });
    expect(toFirearmInput(values, null)).toMatchObject({
      name: "Probe",
      slug: null,
      alternativeNames: ["One", "Two"],
      country: null,
      designDate: null,
      feedSystem: null,
      summary: null,
    });
  });

  it("rejects what the API would anyway, on the field", () => {
    const result = firearmFormSchema.safeParse({
      ...firearmFormValues(null),
      name: " ",
      wikidataId: "155176",
      designDate: "spring 1942",
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((i) => i.path[0]).sort()).toEqual(["designDate", "name", "wikidataId"]);
  });
});
