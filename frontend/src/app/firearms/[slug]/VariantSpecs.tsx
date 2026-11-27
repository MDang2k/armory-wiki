"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { FootnoteMarks } from "@/components/FootnoteMarks";
import type { Dto } from "@/lib/api/types";
import { formatFuzzyDate } from "@/lib/dates";
import { FIRING_MODES } from "@/lib/labels";
import { formatLength, formatMass, formatRange, formatRate, formatVelocity } from "@/lib/units";

/**
 * The specifications of one variant, with a switcher when there are several (base variant first). Server-rendered
 * for the base variant; switching needs no request. `marks` maps "<variant slug>/<field>" to footnote numbers.
 */
export function VariantSpecs({ variants, marks }: { variants: Dto<"VariantDto">[]; marks: Record<string, number[]> }) {
  const ordered = [...variants].sort((a, b) => Number(b.isBaseVariant) - Number(a.isBaseVariant));
  const [slug, setSlug] = useState(ordered[0]?.slug);
  const variant = ordered.find((v) => v.slug === slug) ?? ordered[0];
  if (!variant) {
    return null;
  }

  const s = variant.specs;
  const marksOf = (field: string) => marks[`${variant.slug}/${field}`] ?? [];
  // The rate row shows a span, so citations of either end belong to it.
  const note = (field: string) => (
    <FootnoteMarks numbers={field === "rateOfFireMinRpm" ? [...marksOf(field), ...marksOf("rateOfFireMaxRpm")].sort((a, b) => a - b) : marksOf(field)} />
  );
  const rows: [string, ReactNode, string][] = [];
  const add = (label: string, value: ReactNode | null | undefined, field: string) => {
    if (value !== null && value !== undefined && value !== "") {
      rows.push([label, value, field]);
    }
  };

  const chamberings = [...variant.chamberings].sort((a, b) => Number(b.isPrimary) - Number(a.isPrimary));
  add(
    chamberings.length > 1 ? "Cartridges" : "Cartridge",
    chamberings.length > 0 && (
      <ul>
        {chamberings.map((c) => (
          <li key={c.cartridge.slug}>
            <Link href={`/cartridges/${c.cartridge.slug}`}>{c.cartridge.name}</Link>
            {c.isPrimary && chamberings.length > 1 && <span className="text-muted"> (primary)</span>}
          </li>
        ))}
      </ul>
    ),
    "chamberings",
  );
  add("Introduced", variant.introducedDate && formatFuzzyDate(variant.introducedDate), "introducedDate");
  add("Empty weight", s.weightEmptyG && formatMass(s.weightEmptyG), "weightEmptyG");
  add("Length", s.overallLengthMm && formatLength(s.overallLengthMm), "overallLengthMm");
  add("Length, stock folded", s.overallLengthFoldedMm && formatLength(s.overallLengthFoldedMm), "overallLengthFoldedMm");
  add("Barrel length", s.barrelLengthMm && formatLength(s.barrelLengthMm), "barrelLengthMm");
  add("Rate of fire", s.rateOfFireMinRpm && formatRate(s.rateOfFireMinRpm, s.rateOfFireMaxRpm), "rateOfFireMinRpm");
  add("Muzzle velocity", s.muzzleVelocityMps && formatVelocity(s.muzzleVelocityMps), "muzzleVelocityMps");
  add("Effective range", s.effectiveRangeM && formatRange(s.effectiveRangeM), "effectiveRangeM");
  add("Magazine", s.magazineCapacityStandard && `${s.magazineCapacityStandard} rounds`, "magazineCapacityStandard");
  add("Firing modes", s.firingModes.map((m) => FIRING_MODES[m]).join(", "), "firingModes");
  Object.entries(variant.extraSpecs).forEach(([name, value], i) => {
    add(name.charAt(0).toUpperCase() + name.slice(1), value, i === 0 ? "extraSpecs" : ""); // one mark for the group
  });

  return (
    <>
      <tr>
        <th colSpan={2} className="bg-surface px-2 py-1 text-center">
          {ordered.length > 1 ? (
            <label className="flex items-center justify-center gap-2 font-semibold">
              Specifications of
              <select
                value={variant.slug}
                onChange={(e) => setSlug(e.target.value)}
                className="rounded border border-border bg-background px-1 py-0.5 font-normal"
              >
                {ordered.map((v) => (
                  <option key={v.slug} value={v.slug}>
                    {v.name}
                  </option>
                ))}
              </select>
            </label>
          ) : (
            "Specifications"
          )}
        </th>
      </tr>
      {rows.map(([label, value, field], i) => (
        <tr key={i} className="border-t border-border align-top">
          <th scope="row" className="py-1 pr-2 text-left font-semibold">
            {label}
          </th>
          <td className="py-1">
            {value}
            {note(field)}
          </td>
        </tr>
      ))}
      {rows.length === 0 && (
        <tr>
          <td colSpan={2} className="py-1 text-muted">
            No specifications recorded yet.
          </td>
        </tr>
      )}
    </>
  );
}
