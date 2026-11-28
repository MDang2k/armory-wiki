import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { loadReferenceLists, loadTimeline } from "@/lib/api/loaders";
import type { Dto } from "@/lib/api/types";
import { packLanes, parseTimelineFilters, timelineQuery, yearTicks, type TimelineFilters } from "@/lib/timeline";

export const metadata: Metadata = {
  title: "Timeline",
  description: "Firearms by the year they were designed, filtered by country and type.",
};

const PX_PER_YEAR = 28;
const ROW = 26; // px per row
const LABEL_PX_PER_CHAR = 7; // rough width of a label character at text-xs, for packing rows only

/** Rendered per request from cached data; the filters are read inside Suspense so the shell prerenders. */
export default function TimelinePage({ searchParams }: PageProps<"/timeline">) {
  return (
    <div className="space-y-4">
      <h1 className="font-serif text-3xl">Timeline</h1>
      <p className="text-sm text-muted">Firearms by the year their design began; a bar runs to the year it was finished when that is known.</p>
      <Suspense fallback={<p className="text-muted">Loading…</p>}>
        <Timeline searchParams={searchParams} />
      </Suspense>
    </div>
  );
}

async function Timeline({ searchParams }: { searchParams: PageProps<"/timeline">["searchParams"] }) {
  const filters = parseTimelineFilters(await searchParams);
  const [timeline, lists] = await Promise.all([loadTimeline(timelineQuery(filters)), loadReferenceLists()]);
  return (
    <>
      <Filters filters={filters} countries={lists.countries} tags={lists.tags} />
      {timeline.total > timeline.items.length && (
        <p role="status" className="rounded border border-border bg-surface px-3 py-2 text-sm">
          {timeline.total} firearms match: showing the first {timeline.items.length} by design year. Narrow the years, or pick a
          country or a type, to see them all.
        </p>
      )}
      {timeline.items.length === 0 ? <p>No firearm with a known design date matches these filters.</p> : <Chart items={timeline.items} filters={filters} />}
    </>
  );
}

/** A plain GET form: works without JavaScript, and the URL keeps the view. */
function Filters({ filters, countries, tags }: { filters: TimelineFilters; countries: Dto<"CountryDto">[]; tags: Dto<"TagDto">[] }) {
  const input = "rounded border border-border bg-background px-2 py-1 text-sm";
  return (
    <form action="/timeline" className="flex flex-wrap items-end gap-3 text-sm">
      <label className="space-y-1">
        <span className="block font-medium">From</span>
        <input name="from" type="number" min={1300} max={2100} defaultValue={filters.from} className={`${input} w-24`} />
      </label>
      <label className="space-y-1">
        <span className="block font-medium">To</span>
        <input name="to" type="number" min={1300} max={2100} defaultValue={filters.to} className={`${input} w-24`} />
      </label>
      <label className="space-y-1">
        <span className="block font-medium">Country</span>
        <select name="country" defaultValue={filters.country} className={input}>
          <option value="">Any</option>
          {countries.map((c) => (
            <option key={c.slug} value={c.slug}>
              {c.name}
            </option>
          ))}
        </select>
      </label>
      <label className="space-y-1">
        <span className="block font-medium">Type</span>
        <select name="tag" defaultValue={filters.tag} className={input}>
          <option value="">Any</option>
          {tags.map((t) => (
            <option key={t.slug} value={t.slug}>
              {t.name}
            </option>
          ))}
        </select>
      </label>
      <button type="submit" className="rounded border border-border bg-surface px-3 py-1">
        Show
      </button>
      {timelineQuery(filters) !== "" && <Link href="/timeline">Reset</Link>}
    </form>
  );
}

/**
 * The chart: a year axis and one bar per firearm, packed into as few rows as overlaps allow (labels included),
 * scrolling sideways. Server-rendered positioned links: no chart library, no client JavaScript.
 */
function Chart({ items, filters }: { items: Dto<"TimelineItemDto">[]; filters: TimelineFilters }) {
  const x = (year: number) => (Math.min(Math.max(year, filters.from), filters.to + 1) - filters.from) * PX_PER_YEAR;
  const width = (filters.to - filters.from + 1) * PX_PER_YEAR;
  const placed = packLanes(items, (i) => {
    const start = x(i.designYear);
    const barEnd = x((i.designYearTo ?? i.designYear) + 1);
    return [start, Math.max(barEnd, start + 6) + 4 + i.name.length * LABEL_PX_PER_CHAR + 8]; // the label follows the bar
  });
  const rows = Math.max(...placed.map((p) => p.lane)) + 1;

  return (
    <div className="overflow-x-auto rounded border border-border">
      <div className="relative" style={{ width: Math.max(width, 320), height: 28 + rows * ROW + 8 }}>
        {yearTicks(filters.from, filters.to).map((year) => (
          <div key={year} className="absolute top-0 bottom-0 border-l border-border" style={{ left: x(year) }}>
            <span className="ml-1 text-xs text-muted">{year}</span>
          </div>
        ))}
        <ol aria-label="Firearms by design year">
          {placed.map(({ item, lane }) => {
            const left = x(item.designYear);
            const bar = Math.max(x((item.designYearTo ?? item.designYear) + 1) - left, 6);
            const years = item.designYearTo && item.designYearTo !== item.designYear ? `${item.designYear}–${item.designYearTo}` : `${item.designYear}`;
            return (
              <li key={item.slug} className="absolute flex items-center gap-1 whitespace-nowrap" style={{ left, top: 28 + lane * ROW, height: ROW - 4 }}>
                <span aria-hidden="true" className="h-2.5 rounded-sm bg-link/60" style={{ width: bar }} />
                <Link href={`/firearms/${item.slug}`} className="text-xs" title={`${item.name}, ${years}`}>
                  {item.name}
                  <span className="sr-only"> ({years})</span>
                </Link>
              </li>
            );
          })}
        </ol>
      </div>
    </div>
  );
}
