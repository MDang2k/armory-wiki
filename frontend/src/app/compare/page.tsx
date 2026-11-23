import type { Metadata } from "next";
import Link from "next/link";
import { Suspense, type ReactNode } from "react";
import { loadComparison } from "@/lib/api/loaders";
import type { Dto } from "@/lib/api/types";
import { compareHref, parseCompareSlugs } from "@/lib/compare";
import { formatLength, formatMass, formatRange, formatRate, formatVelocity } from "@/lib/units";
import { AddFirearm } from "./AddFirearm";

export const metadata: Metadata = {
  title: "Compare firearms",
  description: "Specifications of up to four firearms side by side.",
  robots: { index: false }, // endless combinations of the same data
};

export default function ComparePage({ searchParams }: PageProps<"/compare">) {
  return (
    <div className="space-y-4">
      <h1 className="font-serif text-3xl">Compare firearms</h1>
      <Suspense fallback={<p className="text-muted">Loading…</p>}>
        <Comparison searchParams={searchParams} />
      </Suspense>
    </div>
  );
}

const MODES: Record<Dto<"FiringModes">, string> = { SemiAutomatic: "semi-auto", Burst: "burst", Automatic: "automatic" };

const ROWS: [string, (i: Dto<"CompareItemDto">) => ReactNode][] = [
  ["Variant", (i) => i.variantName],
  ["Cartridge", (i) => i.primaryCartridge && <Link href={`/cartridges/${i.primaryCartridge.slug}`}>{i.primaryCartridge.name}</Link>],
  ["Empty weight", (i) => i.specs.weightEmptyG && formatMass(i.specs.weightEmptyG)],
  ["Length", (i) => i.specs.overallLengthMm && formatLength(i.specs.overallLengthMm)],
  ["Barrel length", (i) => i.specs.barrelLengthMm && formatLength(i.specs.barrelLengthMm)],
  ["Rate of fire", (i) => i.specs.rateOfFireMinRpm && formatRate(i.specs.rateOfFireMinRpm, i.specs.rateOfFireMaxRpm)],
  ["Muzzle velocity", (i) => i.specs.muzzleVelocityMps && formatVelocity(i.specs.muzzleVelocityMps)],
  ["Effective range", (i) => i.specs.effectiveRangeM && formatRange(i.specs.effectiveRangeM)],
  ["Magazine", (i) => i.specs.magazineCapacityStandard && `${i.specs.magazineCapacityStandard} rounds`],
  ["Firing modes", (i) => i.specs.firingModes.map((m) => MODES[m]).join(", ")],
];

async function Comparison({ searchParams }: { searchParams: PageProps<"/compare">["searchParams"] }) {
  const slugs = parseCompareSlugs((await searchParams).slugs);
  const loaded = slugs.length > 0 ? await loadComparison(slugs.join(",")) : null;

  return (
    <>
      <AddFirearm slugs={slugs} />
      {slugs.length === 0 && <p className="text-muted">Add firearms to see their base variants side by side.</p>}
      {loaded?.status === "missing" && (
        <div className="space-y-1">
          <p>
            One of these firearms could not be found: it may have been renamed, or not be published. Remove it and add it again
            with the search above:
          </p>
          <ul className="flex flex-wrap gap-3">
            {slugs.map((slug) => (
              <li key={slug}>
                <Link href={compareHref(slugs.filter((s) => s !== slug))}>Remove {slug}</Link>
              </li>
            ))}
          </ul>
        </div>
      )}
      {loaded?.status === "ok" && (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr>
                <th className="w-36" />
                {loaded.data.map((item) => (
                  <th key={item.slug} scope="col" className="px-2 py-1 text-left align-bottom">
                    <Link href={`/firearms/${item.slug}`} className="font-serif text-lg">
                      {item.name}
                    </Link>
                    <Link
                      href={compareHref(slugs.filter((s) => s !== item.slug))}
                      className="ml-2 text-xs font-normal"
                      aria-label={`Remove ${item.name}`}
                    >
                      remove
                    </Link>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {ROWS.map(([label, value]) => (
                <tr key={label} className="border-t border-border align-top">
                  <th scope="row" className="py-1 pr-2 text-left font-semibold">
                    {label}
                  </th>
                  {loaded.data.map((item) => (
                    <td key={item.slug} className="px-2 py-1">
                      {value(item) || <span className="text-muted">—</span>}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
