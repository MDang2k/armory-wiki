"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { ConfirmButton, Notice, StatusBadge, buttonClass, inputClass, primaryButtonClass, useDebounced, useDocumentTitle } from "@/components/admin/ui";
import { isVersionConflict, ApiProblem } from "@/lib/admin/problems";
import { listResource, resourceKeys, setResourcePublished, type AdminListItem } from "@/lib/admin/resourceApi";
import { RESOURCES, isResourceRoute } from "@/lib/admin/resources";
import type { Dto } from "@/lib/api/types";
import { MISSING_FOR_PUBLISH } from "@/lib/labels";

const PAGE_SIZE = 50;

/**
 * The list of one generic resource (PLAN.md Phase 7c): search, and for people, organizations and cartridges a
 * status filter (drafts first) with what each still lacks for publishing and publish or unpublish per row.
 */
export function ResourceList() {
  const { resource } = useParams<{ resource: string }>();
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const queryClient = useQueryClient();
  const route = isResourceRoute(resource) ? resource : null;
  const spec = route ? RESOURCES[route] : null;
  useDocumentTitle(spec?.plural ?? null);

  const status = params.get("status") ?? (spec?.publishable ? "Draft" : "all");
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

  const [search, setSearch] = useState(q);
  const [shownQ, setShownQ] = useState(q);
  if (q !== shownQ) {
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
    status: spec?.publishable && status !== "all" ? (status as Dto<"PublishStatus">) : undefined,
    q: q || undefined,
    page,
    pageSize: PAGE_SIZE,
  };
  const list = useQuery({
    queryKey: [...resourceKeys.lists(route ?? "people"), query],
    queryFn: () => listResource(route!, query),
    enabled: route !== null,
    placeholderData: keepPreviousData,
  });

  const [rowError, setRowError] = useState<string | null>(null);
  const toggle = useMutation({
    mutationFn: ({ row, publish }: { row: AdminListItem; publish: boolean }) => setResourcePublished(route!, row.id, row.version, publish),
    onMutate: () => setRowError(null),
    onError: (error, { row }) => {
      if (!(error instanceof ApiProblem && error.status === 401)) {
        setRowError(isVersionConflict(error) ? `${row.name} changed since the list loaded. The list has been reloaded: try again.` : `${row.name}: ${error.message}`);
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: resourceKeys.lists(route!) }),
  });

  if (!route || !spec) {
    return <Notice>There is no admin list called “{resource}”.</Notice>;
  }

  const total = list.data?.totalCount ?? 0;
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-serif text-2xl">{spec.plural}</h1>
        <Link href={`/admin/${route}/new`} className={`${primaryButtonClass} ml-auto no-underline`}>
          New {spec.title.toLowerCase()}
        </Link>
      </div>

      <div className="flex flex-wrap items-end gap-3 text-sm">
        {spec.publishable && (
          <label className="space-y-1">
            <span className="block font-medium">Status</span>
            <select value={status} onChange={(e) => setFilters({ status: e.target.value === "Draft" ? null : e.target.value })} className={inputClass}>
              <option value="Draft">Drafts</option>
              <option value="Published">Published</option>
              <option value="all">All</option>
            </select>
          </label>
        )}
        <label className="min-w-56 flex-1 space-y-1">
          <span className="block font-medium">Search</span>
          <input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name" className={inputClass} />
        </label>
      </div>

      {rowError && <Notice>{rowError}</Notice>}
      {list.isError && <Notice>Could not load the list: {list.error.message}</Notice>}

      {list.data && (
        <>
          <p className="text-sm text-muted" aria-live="polite">
            {total} {total === 1 ? spec.title.toLowerCase() : spec.plural.toLowerCase()}
            {list.isFetching ? " · updating…" : ""}
          </p>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-border text-left">
                <tr>
                  <th className="py-2 pr-3 font-medium">Name</th>
                  {spec.publishable && <th className="py-2 pr-3 font-medium">Status</th>}
                  {spec.publishable && <th className="py-2 pr-3 font-medium">Missing for publish</th>}
                  <th className="py-2 pr-3 font-medium">Updated</th>
                  {spec.publishable && (
                    <th className="py-2 font-medium">
                      <span className="sr-only">Actions</span>
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {list.data.items.map((row) => (
                  <tr key={row.id} className="border-b border-border align-top">
                    <td className="py-2 pr-3">
                      <Link href={`/admin/${route}/${row.id}`}>{row.name}</Link>
                      <div className="text-xs text-muted">{row.slug}</div>
                    </td>
                    {spec.publishable && (
                      <td className="py-2 pr-3">
                        <StatusBadge status={row.status} />
                      </td>
                    )}
                    {spec.publishable && (
                      <td className="py-2 pr-3">
                        {row.missingForPublish.length === 0 ? (
                          <span className="text-muted">nothing</span>
                        ) : (
                          row.missingForPublish.map((m) => MISSING_FOR_PUBLISH[m] ?? m).join(", ")
                        )}
                      </td>
                    )}
                    <td className="py-2 pr-3 whitespace-nowrap">{new Date(row.updatedAt).toLocaleDateString()}</td>
                    {spec.publishable && (
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
                    )}
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
