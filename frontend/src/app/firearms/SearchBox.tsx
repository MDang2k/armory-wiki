"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, useTransition } from "react";
import { firearmsHref, withFilters, type FirearmFilters } from "@/lib/filters";

const DEBOUNCE_MS = 350;

/**
 * Search as you type: the URL follows the box after a pause. Without JavaScript it is a plain GET form that keeps
 * the other filters.
 */
export function SearchBox({ filters }: { filters: FirearmFilters }) {
  const router = useRouter();
  const [value, setValue] = useState(filters.q);
  const [pushed, setPushed] = useState(filters.q);
  const [shownQ, setShownQ] = useState(filters.q);
  const [pending, startTransition] = useTransition();

  // The URL changed from outside the box (a removed filter chip, back button): show its search.
  if (filters.q !== shownQ) {
    setShownQ(filters.q);
    if (filters.q !== pushed) {
      setValue(filters.q);
      setPushed(filters.q);
    }
  }

  useEffect(() => {
    const q = value.trim();
    if (q === pushed) {
      return;
    }

    const timer = setTimeout(() => {
      setPushed(q);
      startTransition(() => router.replace(firearmsHref(withFilters(filters, { q })), { scroll: false }));
    }, DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [value, pushed, filters, router]);

  return (
    <form
      action="/firearms"
      role="search"
      onSubmit={(e) => {
        e.preventDefault(); // Enter searches at once, without waiting for the pause
        const q = value.trim();
        setPushed(q);
        startTransition(() => router.replace(firearmsHref(withFilters(filters, { q })), { scroll: false }));
      }}
      className="relative"
    >
      <label htmlFor="firearm-search" className="sr-only">
        Search firearms
      </label>
      <input
        id="firearm-search"
        name="q"
        type="search"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder="Search by name, alias or summary"
        className="w-full rounded border border-border bg-background px-3 py-2"
      />
      {Object.entries(filters).flatMap(([key, v]) =>
        Array.isArray(v) && v.length > 0 ? [<input key={key} type="hidden" name={key} value={v.join(",")} />] : [],
      )}
      {filters.decade !== null && <input type="hidden" name="decade" value={filters.decade} />}
      {filters.sort !== null && <input type="hidden" name="sort" value={filters.sort} />}
      {pending && (
        <span role="status" className="absolute top-2.5 right-3 text-xs text-muted">
          Searching…
        </span>
      )}
    </form>
  );
}
