"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import type { Dto } from "@/lib/api/types";
import { compareHref, MAX_COMPARED } from "@/lib/compare";

const DEBOUNCE_MS = 300;

/** Search to add a firearm to the comparison; the browser calls the public list API (same origin). */
export function AddFirearm({ slugs }: { slugs: string[] }) {
  const [q, setQ] = useState("");
  const [results, setResults] = useState<Dto<"FirearmListItemDto">[]>([]);

  useEffect(() => {
    const term = q.trim();
    const controller = new AbortController();
    const timer = setTimeout(async () => {
      if (term.length < 2) {
        setResults([]);
        return;
      }

      try {
        const response = await fetch(`/api/firearms?q=${encodeURIComponent(term)}&pageSize=8`, { signal: controller.signal });
        if (response.ok) {
          const page: Dto<"PagedResultOfFirearmListItemDto"> = await response.json();
          setResults(page.items.filter((f) => !slugs.includes(f.slug)));
        }
      } catch {
        // aborted by a newer search, or offline: keep the previous results
      }
    }, DEBOUNCE_MS);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [q, slugs]);

  if (slugs.length >= MAX_COMPARED) {
    return <p className="text-sm text-muted">Up to {MAX_COMPARED} firearms can be compared; remove one to add another.</p>;
  }

  return (
    <div className="relative max-w-md">
      <label htmlFor="compare-search" className="mb-1 block text-sm font-semibold">
        Add a firearm
      </label>
      <input
        id="compare-search"
        type="search"
        value={q}
        onChange={(e) => setQ(e.target.value)}
        placeholder="Search by name"
        autoComplete="off"
        className="w-full rounded border border-border bg-background px-3 py-2"
      />
      {results.length > 0 && (
        <ul className="absolute z-10 mt-1 w-full rounded border border-border bg-background shadow">
          {results.map((f) => (
            <li key={f.slug}>
              <Link
                href={compareHref([...slugs, f.slug])}
                onClick={() => setQ("")}
                className="block px-3 py-1.5 hover:bg-surface hover:no-underline"
              >
                {f.name}
                {f.designDate && <span className="text-sm text-muted"> ({f.designDate.year})</span>}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
