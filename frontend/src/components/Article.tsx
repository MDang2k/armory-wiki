import Link from "next/link";
import type { ReactNode } from "react";
import type { Dto } from "@/lib/api/types";
import { footnoteId, type Citation } from "@/lib/citations";
import { FootnoteMarks } from "./FootnoteMarks";

/** The building blocks every encyclopedia page shares: sections, a facts table, sources, paging. */

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mt-6">
      <h2 className="mb-2 border-b border-border pb-1 font-serif text-2xl">{title}</h2>
      {children}
    </section>
  );
}

/** Label → value rows; rows without a value are left out. `marks` are footnote numbers per row. */
export function FactTable({ rows }: { rows: { label: string; value: ReactNode; marks?: number[] }[] }) {
  const shown = rows.filter((r) => r.value !== null && r.value !== undefined && r.value !== false && r.value !== "");
  if (shown.length === 0) {
    return null;
  }

  return (
    <table className="w-full text-sm">
      <tbody>
        {shown.map((row, i) => (
          <tr key={i} className="border-t border-border align-top first:border-t-0">
            <th scope="row" className="w-36 py-1 pr-2 text-left font-semibold">
              {row.label}
            </th>
            <td className="py-1">
              {row.value}
              <FootnoteMarks numbers={row.marks ?? []} />
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The numbered Sources list footnote marks point to (PLAN.md §5). */
export function Sources({ citations, variantName }: { citations: Citation[]; variantName?: (slug: string) => string | undefined }) {
  if (citations.length === 0) {
    return null;
  }

  return (
    <Section title="Sources">
      <ol className="list-decimal space-y-1 pl-6 text-sm">
        {citations.map((c, i) => (
          <li key={i} id={footnoteId(i + 1)} className="target:bg-link/10">
            <SourceLine source={c.source} locator={c.locator} />
            {c.variant && <span className="text-muted"> (for the {variantName?.(c.variant) ?? c.variant})</span>}
          </li>
        ))}
      </ol>
    </Section>
  );
}

function SourceLine({ source: s, locator }: { source: Dto<"SourceDto">; locator: string | null }) {
  const details = [s.publisher, s.year].filter(Boolean).join(", ");
  const locatorIsLink = locator?.startsWith("https://") || locator?.startsWith("http://");
  return (
    <>
      {s.author && `${s.author}, `}
      {s.url ? (
        <a href={s.url} rel="noopener noreferrer">
          <cite>{s.title}</cite>
        </a>
      ) : (
        <cite>{s.title}</cite>
      )}
      {details && ` (${details})`}
      {locator &&
        (locatorIsLink ? (
          <>
            {", "}
            <a href={locator} rel="noopener noreferrer">
              {locator.replace(/^https?:\/\//, "")}
            </a>
          </>
        ) : (
          `, ${locator}`
        ))}
    </>
  );
}

/** Numbered pages with a window around the current one. */
export function Pager({ page, pages, href }: { page: number; pages: number; href: (page: number) => string }) {
  if (pages <= 1) {
    return null;
  }

  const current = Math.min(page, pages);
  const shown = [...new Set([1, current - 2, current - 1, current, current + 1, current + 2, pages])]
    .filter((p) => p >= 1 && p <= pages)
    .sort((a, b) => a - b);

  return (
    <nav aria-label="Pages" className="flex flex-wrap items-center gap-1 text-sm">
      {current > 1 && <Link href={href(current - 1)}>‹ Previous</Link>}
      {shown.map((p, i) => (
        <span key={p} className="flex items-center gap-1">
          {i > 0 && p - shown[i - 1] > 1 && <span className="text-muted">…</span>}
          {p === current ? (
            <strong aria-current="page" className="px-1.5">
              {p}
            </strong>
          ) : (
            <Link href={href(p)} className="px-1.5">
              {p}
            </Link>
          )}
        </span>
      ))}
      {current < pages && <Link href={href(current + 1)}>Next ›</Link>}
    </nav>
  );
}

/** "1919–2013", "born 1922", "c. 1900s–1953". */
export function lifespan(from: string | null, to: string | null) {
  return from && to ? `${from} – ${to}` : from ? `born ${from}` : to ? `died ${to}` : null;
}
