"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { Notice, buttonClass, inputClass, primaryButtonClass, useDebounced } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import { unwrap } from "@/lib/admin/problems";
import { resourceKeys } from "@/lib/admin/resourceApi";

const PAGE_SIZE = 50;

/** Sources (GLOSSARY.md: source), by title, with how many citations use each. */
export function SourceList() {
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const q = useDebounced(search.trim(), 350);
  const query = { q: q || undefined, page, pageSize: PAGE_SIZE };
  const list = useQuery({
    queryKey: [...resourceKeys.lists("sources"), query],
    queryFn: async () => unwrap(await adminApi.GET("/api/admin/sources", { params: { query } })),
    placeholderData: keepPreviousData,
  });
  const total = list.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-serif text-2xl">Sources</h1>
        <Link href="/admin/sources/new" className={`${primaryButtonClass} ml-auto no-underline`}>
          New source
        </Link>
      </div>
      <label className="block max-w-xl space-y-1 text-sm">
        <span className="block font-medium">Search</span>
        <input
          type="search"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          placeholder="Title or author"
          className={inputClass}
        />
      </label>
      {list.isError && <Notice>Could not load sources: {list.error.message}</Notice>}
      {list.data && (
        <>
          <p className="text-sm text-muted">
            {total} {total === 1 ? "source" : "sources"}
          </p>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-border text-left">
                <tr>
                  <th className="py-2 pr-3 font-medium">Title</th>
                  <th className="py-2 pr-3 font-medium">Author</th>
                  <th className="py-2 pr-3 font-medium">Year</th>
                  <th className="py-2 pr-3 font-medium">Type</th>
                  <th className="py-2 font-medium">Citations</th>
                </tr>
              </thead>
              <tbody>
                {list.data.items.map((s) => (
                  <tr key={s.id} className="border-b border-border align-top">
                    <td className="py-2 pr-3">
                      <Link href={`/admin/sources/${s.id}`}>{s.details.title}</Link>
                    </td>
                    <td className="py-2 pr-3">{s.details.author}</td>
                    <td className="py-2 pr-3">{s.details.year}</td>
                    <td className="py-2 pr-3">{s.details.type}</td>
                    <td className="py-2">{s.citationCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pages > 1 && (
            <nav aria-label="Pages" className="flex items-center gap-3 text-sm">
              <button type="button" className={buttonClass} disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Previous
              </button>
              <span>
                Page {page} of {pages}
              </span>
              <button type="button" className={buttonClass} disabled={page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}
