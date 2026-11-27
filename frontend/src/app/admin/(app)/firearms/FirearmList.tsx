"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { ConfirmButton, Notice, StatusBadge, buttonClass, inputClass, primaryButtonClass, useDebounced } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import { ApiProblem, isVersionConflict, unwrap } from "@/lib/admin/problems";
import type { Dto } from "@/lib/api/types";
import { MISSING_FOR_PUBLISH } from "@/lib/labels";
import { firearmKeys, setPublished } from "./firearmApi";

const PAGE_SIZE = 50;
type Row = Dto<"AdminFirearmListItemDto">;

/**
 * The firearm list, which is also the draft queue (PLAN.md Phase 7): drafts first by default, what each still
 * lacks for publishing, and publish or unpublish per row. Filters live in the URL, so a filtered view can be
 * bookmarked and Back works.
 */
export function FirearmList() {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const queryClient = useQueryClient();

  const status = params.get("status") ?? "Draft"; // "Draft", "Published" or "all"
  const ready = params.get("ready") === "1";
  const q = params.get("q") ?? "";
  const page = Math.max(1, Number(params.get("page")) || 1);

  function setFilters(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries({ ...changes, page: changes.page ?? null })) {
      if (value === null || value === "") {
        next.delete(key);
      } else {
        next.set(key, value);
      }
    }

    router.replace(`${pathname}?${next}`, { scroll: false });
  }

  // The search box follows the URL, and the URL follows the box after a pause.
  const [search, setSearch] = useState(q);
  const [shownQ, setShownQ] = useState(q);
  if (q !== shownQ) {
    // The URL changed from outside the box (Back, a link): show its search.
    setShownQ(q);
    if (q !== search.trim()) {
      setSearch(q);
    }
  }

  const settled = useDebounced(search.trim(), 350);
  useEffect(() => {
    if (settled !== q) {
      setFilters({ q: settled });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only the settled text triggers a search
  }, [settled]);

  const query = {
    status: status === "all" ? undefined : (status as Dto<"PublishStatus">),
    ready: ready || undefined,
    q: q || undefined,
    page,
    pageSize: PAGE_SIZE,
  };
  const list = useQuery({
    queryKey: [...firearmKeys.lists, query],
    queryFn: async () => unwrap(await adminApi.GET("/api/admin/firearms", { params: { query } })),
    placeholderData: keepPreviousData,
  });

  const [rowError, setRowError] = useState<string | null>(null);
  const toggle = useMutation({
    mutationFn: ({ row, publish }: { row: Row; publish: boolean }) => setPublished(row.id, row.version, publish),
    onMutate: () => setRowError(null),
    onSuccess: (firearm) => queryClient.setQueryData(firearmKeys.one(firearm.id), firearm),
    onError: (error, { row }) => {
      if (error instanceof ApiProblem && error.status === 401) {
        return;
      }

      setRowError(
        isVersionConflict(error)
          ? `${row.name} changed since the list loaded. The list has been reloaded: try again.`
          : `${row.name}: ${error.message}`,
      );
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: firearmKeys.lists }),
  });

  const total = list.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-serif text-2xl">Firearms</h1>
        <Link href="/admin/firearms/new" className={`${primaryButtonClass} ml-auto no-underline`}>
          New firearm
        </Link>
      </div>

      <div className="flex flex-wrap items-end gap-3 text-sm">
        <label className="space-y-1">
          <span className="block font-medium">Status</span>
          <select value={status} onChange={(e) => setFilters({ status: e.target.value === "Draft" ? null : e.target.value })} className={inputClass}>
            <option value="Draft">Drafts</option>
            <option value="Published">Published</option>
            <option value="all">All</option>
          </select>
        </label>
        <label className="flex items-center gap-2 pb-2">
          <input type="checkbox" checked={ready} onChange={(e) => setFilters({ ready: e.target.checked ? "1" : null })} />
          Ready to publish
        </label>
        <label className="min-w-56 flex-1 space-y-1">
          <span className="block font-medium">Search</span>
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name or alternative name" className={inputClass} />
        </label>
      </div>

      {rowError && <Notice>{rowError}</Notice>}
      {list.isError && <Notice>Could not load firearms: {list.error.message}</Notice>}

      {list.data && (
        <>
          <p className="text-sm text-muted" aria-live="polite">
            {total} {total === 1 ? "firearm" : "firearms"}
            {list.isFetching ? " · updating…" : ""}
          </p>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-border text-left">
                <tr>
                  <th className="py-2 pr-3 font-medium">Name</th>
                  <th className="py-2 pr-3 font-medium">Status</th>
                  <th className="py-2 pr-3 font-medium">Missing for publish</th>
                  <th className="py-2 pr-3 font-medium">Wikidata</th>
                  <th className="py-2 pr-3 font-medium">Updated</th>
                  <th className="py-2 font-medium">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {list.data.items.map((row) => (
                  <tr key={row.id} className="border-b border-border align-top">
                    <td className="py-2 pr-3">
                      <Link href={`/admin/firearms/${row.id}`}>{row.name}</Link>
                      <div className="text-xs text-muted">{row.slug}</div>
                    </td>
                    <td className="py-2 pr-3">
                      <StatusBadge status={row.status} />
                    </td>
                    <td className="py-2 pr-3">
                      {row.missingForPublish.length === 0 ? (
                        <span className="text-muted">nothing</span>
                      ) : (
                        row.missingForPublish.map((m) => MISSING_FOR_PUBLISH[m] ?? m).join(", ")
                      )}
                    </td>
                    <td className="py-2 pr-3">
                      {row.wikidataId && (
                        <a href={`https://www.wikidata.org/wiki/${row.wikidataId}`} target="_blank" rel="noopener noreferrer">
                          {row.wikidataId}
                        </a>
                      )}
                    </td>
                    <td className="py-2 pr-3 whitespace-nowrap">{new Date(row.updatedAt).toLocaleDateString()}</td>
                    <td className="py-2 text-right whitespace-nowrap">
                      {row.status === "Draft" ? (
                        <button
                          type="button"
                          className={buttonClass}
                          disabled={row.missingForPublish.length > 0 || toggle.isPending}
                          title={row.missingForPublish.length > 0 ? "Fill in what is missing first" : undefined}
                          onClick={() => toggle.mutate({ row, publish: true })}
                        >
                          Publish
                        </button>
                      ) : (
                        <ConfirmButton
                          label="Unpublish"
                          question={<>Unpublish <strong>{row.name}</strong>? It disappears from the public site until published again.</>}
                          confirmLabel="Unpublish"
                          disabled={toggle.isPending}
                          onConfirm={() => toggle.mutate({ row, publish: false })}
                        />
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pages > 1 && (
            <nav aria-label="Pages" className="flex items-center gap-3 text-sm">
              <button type="button" className={buttonClass} disabled={page <= 1} onClick={() => setFilters({ page: String(page - 1) })}>
                Previous
              </button>
              <span>
                Page {page} of {pages}
              </span>
              <button type="button" className={buttonClass} disabled={page >= pages} onClick={() => setFilters({ page: String(page + 1) })}>
                Next
              </button>
            </nav>
          )}
        </>
      )}
    </div>
  );
}
