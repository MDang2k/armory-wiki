import { describe, expect, it } from "vitest";
import { formatLength, formatMass, formatRange, formatRate, formatVelocity, gToLb, mmToIn, mpsToFps, mToYd } from "./units";

describe("conversions", () => {
  it("uses exact international definitions", () => {
    expect(mmToIn(25.4)).toBe(1);
    expect(gToLb(453.59237)).toBe(1);
    expect(mpsToFps(0.3048)).toBe(1);
    expect(mToYd(0.9144)).toBe(1);
  });
});

describe("formatting", () => {
  it("shows metric first with imperial in parentheses", () => {
    expect(formatLength(870)).toBe("870 mm (34.3 in)");
    expect(formatLength(1100)).toBe("1,100 mm (43.3 in)");
    expect(formatVelocity(715)).toBe("715 m/s (2,346 ft/s)");
    expect(formatRange(400)).toBe("400 m (437 yd)");
  });

  it("uses grams below a kilogram and kilograms above", () => {
    expect(formatMass(750)).toBe("750 g (26.5 oz)");
    expect(formatMass(4300)).toBe("4.3 kg (9.5 lb)");
    expect(formatMass(4310)).toBe("4.31 kg (9.5 lb)");
  });

  it("shows a cyclic rate as one value or a span", () => {
    expect(formatRate(600)).toBe("600 rounds/min");
    expect(formatRate(600, 600)).toBe("600 rounds/min");
    expect(formatRate(550, 650)).toBe("550–650 rounds/min");
  });
});
