import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { loadFirearmFacets, loadFirearmList } from "@/lib/api/loaders";
import type { Dto } from "@/lib/api/types";
import { formatFuzzyDate } from "@/lib/dates";
import {
  firearmsHref,
  isFiltered,
  MULTI,
  PAGE_SIZE,
  parseFilters,
  toggle,
  toQueryString,
  withFilters,
  withPage,
  type FirearmFilters,
  type MultiKey,
  type Sort,
} from "@/lib/filters";
import { Pager } from "@/components/Article";
import { SearchBox } from "./SearchBox";

export const metadata: Metadata = {
  title: "Firearms",
  description: "Search and filter firearms by type, era, country, mechanism and design decade.",
};

/** The shell prerenders; results depend on the URL, so they render per request inside Suspense (PLAN.md §5). */
export default function FirearmsPage({ searchParams }: PageProps<"/firearms">) {
  return (
    <div className="space-y-4">
      <h1 className="font-serif text-3xl">Firearms</h1>
      <Suspense fallback={<p className="text-muted">Loading firearms…</p>}>
        <Results searchParams={searchParams} />
      </Suspense>
    </div>
  );
}

async function Results({ searchParams }: { searchParams: PageProps<"/firearms">["searchParams"] }) {
  const filters = parseFilters(await searchParams);
  const query = toQueryString(filters);
  const [list, facets] = await Promise.all([loadFirearmList(query), loadFirearmFacets(query)]);
  const labels = labelsOf(facets);

  return (
    <div className="grid gap-6 md:grid-cols-[16rem_1fr]">
      <section aria-label="Results" className="min-w-0 space-y-4">
        <SearchBox filters={filters} />
        {/* Small screens show results first; the filters follow them. */}
        <p className="text-sm md:hidden">
          <a href="#filters">Filters ↓</a>
        </p>
        <ActiveFilters filters={filters} labels={labels} />
        <div className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
          <p className="text-muted">
            {list.totalCount.toLocaleString("en-US")} {list.totalCount === 1 ? "firearm" : "firearms"}
          </p>
          <SortLinks filters={filters} />
        </div>
        {list.items.length === 0 && list.totalCount > 0 ? (
          <p>
            This page is past the last one.{" "}
            <Link href={firearmsHref(withPage(filters, Math.ceil(list.totalCount / PAGE_SIZE)))}>Go to the last page</Link>
          </p>
        ) : list.items.length === 0 ? (
          <p>No firearm matches these filters.</p>
        ) : (
          <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {list.items.map((firearm) => (
              <li key={firearm.slug}>
                <FirearmCard firearm={firearm} />
              </li>
            ))}
          </ul>
        )}
        <Pager page={filters.page} pages={Math.ceil(list.totalCount / PAGE_SIZE)} href={(p) => firearmsHref(withPage(filters, p))} />
      </section>
      <aside id="filters" aria-label="Filters" className="space-y-5 text-sm md:order-first">
        <TagFacets filters={filters} tags={facets.tags} />
        <Facet title="Country" filterKey="country" filters={filters} options={facets.countries} scroll />
        <Facet title="Operating mechanism" filterKey="mechanism" filters={filters} options={facets.mechanisms} />
        <DecadeFacet filters={filters} decades={facets.decades} />
      </aside>
    </div>
  );
}

// Facets ---------------------------------------------------------------------------------------------------------

type Option = { slug: string; name: string; count: number };

/** One checkbox-like link per option: selecting needs no JavaScript, and every state is a shareable URL. */
function Facet({
  title,
  filterKey,
  filters,
  options,
  scroll = false,
}: {
  title: string;
  filterKey: MultiKey;
  filters: FirearmFilters;
  options: Option[];
  scroll?: boolean;
}) {
  const shown = options.filter((o) => o.count > 0 || filters[filterKey].includes(o.slug));
  if (shown.length === 0) {
    return null;
  }

  return (
    <fieldset>
      <legend className="mb-1 font-semibold">{title}</legend>
      <ul className={scroll ? "max-h-64 space-y-0.5 overflow-y-auto pr-1" : "space-y-0.5"}>
        {shown.map((option) => {
          const selected = filters[filterKey].includes(option.slug);
          return (
            <li key={option.slug}>
              <Link
                href={firearmsHref(toggle(filters, filterKey, option.slug))}
                scroll={false}
                aria-current={selected ? "true" : undefined}
                className="flex items-center gap-2 text-foreground! hover:no-underline"
              >
                <span
                  aria-hidden
                  className={`inline-block size-3.5 shrink-0 rounded-sm border ${selected ? "border-link bg-link" : "border-border"}`}
                />
                <span className="flex-1">
                  {option.name}
                  {selected && <span className="sr-only"> (selected; select again to remove)</span>}
                </span>
                <span className="text-xs text-muted">{option.count}</span>
              </Link>
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}

const TAG_GROUPS = [
  ["Type", "Type"],
  ["Role", "Role"],
  ["Era", "Era"],
] as const;

function TagFacets({ filters, tags }: { filters: FirearmFilters; tags: Dto<"TagFacetDto">[] }) {
  return (
    <>
      {TAG_GROUPS.map(([group, title]) => (
        <Facet key={group} title={title} filterKey="tag" filters={filters} options={tags.filter((t) => t.group === group)} />
      ))}
    </>
  );
}

/** One decade at a time: the API filters by a single design-year range. */
function DecadeFacet({ filters, decades }: { filters: FirearmFilters; decades: Dto<"DecadeFacetDto">[] }) {
  const shown = decades.filter((d) => d.count > 0 || d.decade === filters.decade);
  if (shown.length === 0) {
    return null;
  }

  return (
    <fieldset>
      <legend className="mb-1 font-semibold">Designed in</legend>
      <ul className="flex flex-wrap gap-1">
        {shown.map((d) => {
          const selected = d.decade === filters.decade;
          return (
            <li key={d.decade}>
              <Link
                href={firearmsHref(withFilters(filters, { decade: selected ? null : d.decade }))}
                scroll={false}
                aria-current={selected ? "true" : undefined}
                className={`inline-block rounded border px-2 py-0.5 text-xs hover:no-underline ${selected ? "border-link bg-link text-background!" : "border-border text-foreground!"}`}
              >
                {d.decade}s <span className={selected ? "" : "text-muted"}>{d.count}</span>
                {selected && <span className="sr-only"> (selected; select again to remove)</span>}
              </Link>
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}

// Results ----------------------------------------------------------------------------------------------------------

/** Names for the selected values the facets know; others (designer, cartridge links from other pages) show their slug. */
function labelsOf(facets: Dto<"FirearmFacetsDto">) {
  return new Map<string, string>([
    ...facets.tags.map((t) => [`tag:${t.slug}`, t.name] as const),
    ...facets.countries.map((c) => [`country:${c.slug}`, c.name] as const),
    ...facets.mechanisms.map((m) => [`mechanism:${m.slug}`, m.name] as const),
  ]);
}

const FILTER_NAMES: Record<MultiKey, string> = {
  tag: "Tag",
  country: "Country",
  mechanism: "Mechanism",
  designer: "Designer",
  manufacturer: "Manufacturer",
  cartridge: "Cartridge",
};

function ActiveFilters({ filters, labels }: { filters: FirearmFilters; labels: Map<string, string> }) {
  if (!isFiltered(filters)) {
    return null;
  }

  const chips = [
    ...(filters.q ? [{ label: `“${filters.q}”`, href: firearmsHref(withFilters(filters, { q: "" })) }] : []),
    ...MULTI.flatMap((key) =>
      filters[key].map((slug) => ({
        label: `${FILTER_NAMES[key]}: ${labels.get(`${key}:${slug}`) ?? slug}`,
        href: firearmsHref(toggle(filters, key, slug)),
      })),
    ),
    ...(filters.decade !== null
      ? [{ label: `${filters.decade}s`, href: firearmsHref(withFilters(filters, { decade: null })) }]
      : []),
  ];

  return (
    <ul aria-label="Active filters" className="flex flex-wrap items-center gap-1.5 text-xs">
      {chips.map((chip) => (
        <li key={chip.href}>
          <Link
            href={chip.href}
            scroll={false}
            className="inline-flex items-center gap-1 rounded-full border border-border bg-surface px-2 py-0.5 text-foreground! hover:no-underline"
          >
            {chip.label} <span aria-label="Remove">×</span>
          </Link>
        </li>
      ))}
      <li>
        <Link href="/firearms" className="px-1">
          Clear all
        </Link>
      </li>
    </ul>
  );
}

const SORT_LABELS: [Sort | null, string][] = [
  [null, "Default"],
  ["name", "Name"],
  ["designYear", "Oldest"],
  ["-designYear", "Newest"],
];

function SortLinks({ filters }: { filters: FirearmFilters }) {
  return (
    <p className="flex flex-wrap gap-x-3">
      <span className="text-muted">Sort:</span>
      {SORT_LABELS.map(([sort, label]) =>
        filters.sort === sort ? (
          <strong key={label} aria-current="true">
            {sort === null && filters.q ? "Relevance" : label}
          </strong>
        ) : (
          <Link key={label} href={firearmsHref(withFilters(filters, { sort }))} scroll={false}>
            {sort === null && filters.q ? "Relevance" : label}
          </Link>
        ),
      )}
    </p>
  );
}

function FirearmCard({ firearm }: { firearm: Dto<"FirearmListItemDto"> }) {
  const types = firearm.tags.filter((t) => t.group === "Type").map((t) => t.name);
  return (
    <Link
      href={`/firearms/${firearm.slug}`}
      className="flex h-full gap-3 rounded border border-border bg-surface p-2 text-foreground! hover:no-underline hover:border-link"
    >
      {firearm.thumbnailUrl ? (
        // Our own pre-sized WebP thumbnail: no next/image optimizer (PLAN.md §5).
        // eslint-disable-next-line @next/next/no-img-element
        <img src={firearm.thumbnailUrl} alt="" width={96} height={64} loading="lazy" className="h-16 w-24 shrink-0 rounded-sm object-contain" />
      ) : (
        <span aria-hidden className="h-16 w-24 shrink-0 rounded-sm bg-border/40" />
      )}
      <span className="min-w-0 space-y-0.5">
        <span className="block font-semibold text-link">{firearm.name}</span>
        <span className="block text-xs text-muted">
          {[firearm.designDate && formatFuzzyDate(firearm.designDate), firearm.country?.name].filter(Boolean).join(" · ")}
        </span>
        <span className="block text-xs">{[...types, firearm.primaryCartridge?.name].filter(Boolean).join(" · ")}</span>
      </span>
    </Link>
  );
}
