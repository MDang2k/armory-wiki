import Link from "next/link";
import { connection } from "next/server";
import { Suspense } from "react";
import { loadOverview } from "@/lib/api/loaders";
import type { Dto } from "@/lib/api/types";

export default function Home() {
  return (
    <div className="space-y-6">
      <section className="space-y-3">
        <h1 className="font-serif text-3xl">ArmoryWiki</h1>
        <p className="max-w-2xl text-muted">
          An encyclopedia of firearms: their designs, the people and organizations behind them, the cartridges they
          fire and how they work.
        </p>
        <form action="/firearms" role="search" className="flex max-w-xl gap-2">
          <label htmlFor="home-search" className="sr-only">
            Search firearms
          </label>
          <input
            id="home-search"
            name="q"
            type="search"
            placeholder="AK-47, Garand, Mauser…"
            className="flex-1 rounded border border-border bg-background px-3 py-2"
          />
          <button type="submit" className="rounded border border-border bg-surface px-4 py-2">
            Search
          </button>
        </form>
      </section>
      <Suspense fallback={<p className="text-muted">Loading…</p>}>
        <Browse />
      </Suspense>
    </div>
  );
}

/** Rendered per request (from cached data), so building the site never needs the API (PLAN.md §5). */
async function Browse() {
  await connection();
  const { total, facets } = await loadOverview();
  const tagsIn = (group: Dto<"TagGroup">) => facets.tags.filter((t) => t.group === group && t.count > 0);
  const countries = [...facets.countries].filter((c) => c.count > 0).sort((a, b) => b.count - a.count).slice(0, 16);

  return (
    <div className="space-y-6">
      <p>
        <Link href="/firearms">
          Browse all {total.toLocaleString("en-US")} {total === 1 ? "firearm" : "firearms"}
        </Link>
      </p>
      <BrowseBlock title="By type" filterKey="tag" options={tagsIn("Type")} />
      <BrowseBlock title="By era of service" filterKey="tag" options={tagsIn("Era")} />
      <BrowseBlock title="By role" filterKey="tag" options={tagsIn("Role")} />
      <BrowseBlock title="By country of origin" filterKey="country" options={countries} />
    </div>
  );
}

function BrowseBlock({
  title,
  filterKey,
  options,
}: {
  title: string;
  filterKey: "tag" | "country";
  options: { slug: string; name: string; count: number }[];
}) {
  if (options.length === 0) {
    return null;
  }

  return (
    <section>
      <h2 className="mb-2 border-b border-border pb-1 font-serif text-xl">{title}</h2>
      <ul className="flex flex-wrap gap-2">
        {options.map((option) => (
          <li key={option.slug}>
            <Link
              href={`/firearms?${filterKey}=${option.slug}`}
              className="inline-block rounded border border-border bg-surface px-3 py-1 text-sm hover:no-underline hover:border-link"
            >
              {option.name} <span className="text-muted">{option.count}</span>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  );
}
