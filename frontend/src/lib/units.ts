/**
 * Measurements are stored in metric (CLAUDE.md); this is the only place imperial appears. Every value is shown in
 * both systems, metric first: "870 mm (34.3 in)" (PLAN.md §5).
 */

export const mmToIn = (mm: number) => mm / 25.4;
export const gToLb = (g: number) => g / 453.59237;
export const gToOz = (g: number) => g / 28.349523125;
export const mpsToFps = (mps: number) => mps / 0.3048;
export const mToYd = (m: number) => m / 0.9144;

const number = (value: number, decimals = 0) =>
  value.toLocaleString("en-US", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });

/** Overall or barrel length: "870 mm (34.3 in)". */
export const formatLength = (mm: number) => `${number(mm)} mm (${number(mmToIn(mm), 1)} in)`;

/** Weight: grams under a kilogram ("750 g (26.5 oz)"), kilograms above ("4.3 kg (9.5 lb)"). */
export const formatMass = (g: number) =>
  g < 1000
    ? `${number(g)} g (${number(gToOz(g), 1)} oz)`
    : `${number(g / 1000, g % 100 === 0 ? 1 : 2)} kg (${number(gToLb(g), 1)} lb)`;

/** Muzzle velocity: "715 m/s (2,346 ft/s)". */
export const formatVelocity = (mps: number) => `${number(mps)} m/s (${number(mpsToFps(mps))} ft/s)`;

/** Effective range: "400 m (437 yd)". */
export const formatRange = (m: number) => `${number(m)} m (${number(mToYd(m))} yd)`;

/** Cyclic rate: "600 rounds/min", or a span "550–650 rounds/min". Has no imperial form. */
export const formatRate = (min: number, max?: number | null) =>
  max == null || max === min ? `${number(min)} rounds/min` : `${number(min)}–${number(max)} rounds/min`;
