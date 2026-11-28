"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { ConfirmButton, Field, Notice, StatusBadge, inputClass, primaryButtonClass, useDebounced } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import { ApiProblem, isVersionConflict, unwrap } from "@/lib/admin/problems";
import type { Dto } from "@/lib/api/types";
import { firearmKeys, loadFirearm } from "../../firearmApi";

type Row = Dto<"AdminFirearmListItemDto">;

/**
 * Merge into variant (GLOSSARY.md, ADR-0007, PLAN.md Phase 7e): pick the firearm this draft is really a version of,
 * read what moves and what is dropped, confirm. The API runs the merge from the same plan it previewed, at the
 * versions shown here.
 */
export function MergeFirearm() {
  const { id: rawId } = useParams<{ id: string }>();
  const id = Number(rawId);
  const router = useRouter();
  const queryClient = useQueryClient();
  const source = useQuery({ queryKey: firearmKeys.one(id), queryFn: () => loadFirearm(id), enabled: Number.isInteger(id) });

  const [search, setSearch] = useState("");
  const q = useDebounced(search.trim());
  const candidates = useQuery({
    queryKey: ["admin", "firearms", "merge-targets", q],
    queryFn: async () => unwrap(await adminApi.GET("/api/admin/firearms", { params: { query: { q: q || undefined, pageSize: 20, sort: "name" } } })),
    enabled: q.length > 0,
  });
  const [target, setTarget] = useState<Row | null>(null);

  const preview = useQuery({
    queryKey: ["admin", "merge-preview", id, target?.id, target?.version],
    queryFn: async () =>
      unwrap(await adminApi.GET("/api/admin/firearms/{id}/merge-into/{targetId}", { params: { path: { id, targetId: target!.id } } })),
    enabled: target !== null,
  });

  const [error, setError] = useState<string | null>(null);
  const merge = useMutation({
    mutationFn: async () =>
      unwrap(
        await adminApi.POST("/api/admin/firearms/{id}/merge-into/{targetId}", {
          params: { path: { id, targetId: target!.id }, query: { version: source.data!.version, targetVersion: target!.version } },
        }),
      ),
    onMutate: () => setError(null),
    onSuccess: (merged) => {
      queryClient.removeQueries({ queryKey: firearmKeys.one(id) });
      queryClient.setQueryData(firearmKeys.one(merged.id), merged);
      void queryClient.invalidateQueries({ queryKey: firearmKeys.lists });
      router.replace(`/admin/firearms/${merged.id}`);
    },
    onError: (e) => {
      if (e instanceof ApiProblem && e.status === 401) {
        return;
      }

      setError(
        isVersionConflict(e)
          ? "One of the two firearms changed since this preview. Pick the target again to see the current preview."
          : e instanceof Error
            ? e.message
            : "Could not reach the API.",
      );
    },
  });

  if (!Number.isInteger(id) || (source.error instanceof ApiProblem && source.error.status === 404)) {
    return <Notice>There is no firearm #{rawId}.</Notice>;
  }

  if (source.isPending) {
    return <p className="text-muted">Loading…</p>;
  }

  if (source.isError) {
    return <Notice>Could not load the firearm: {source.error.message}</Notice>;
  }

  const s = source.data;
  const p = preview.data;
  return (
    <div className="space-y-5">
      <p className="text-sm">
        <Link href={`/admin/firearms/${s.id}`}>← {s.details.name}</Link>
      </p>
      <h1 className="font-serif text-2xl">Merge {s.details.name} into a variant</h1>
      <p className="max-w-3xl text-sm text-muted">
        For a draft that turns out to be a version of another design (an M4A1 imported as a firearm of its own, which belongs
        under the M4). It becomes a variant of the firearm you pick; this firearm is then deleted, and its address leads there.
      </p>

      <div className="grid max-w-3xl gap-3 sm:grid-cols-2">
        <Field label="Find the firearm it belongs to" htmlFor="merge-search">
          <input id="merge-search" type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Name" className={inputClass} />
        </Field>
        <Field label="Merge into" htmlFor="merge-target">
          <select
            id="merge-target"
            value={target?.id ?? ""}
            onChange={(e) => {
              setError(null);
              setTarget(candidates.data?.items.find((c) => c.id === Number(e.target.value)) ?? null);
            }}
            className={inputClass}
          >
            <option value="">{q ? "Choose…" : "Search first"}</option>
            {candidates.data?.items
              .filter((c) => c.id !== s.id)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.slug}){c.status === "Draft" ? ", draft" : ""}
                </option>
              ))}
          </select>
        </Field>
      </div>

      {error && <Notice>{error}</Notice>}
      {preview.isError && <Notice>Could not preview the merge: {preview.error.message}</Notice>}
      {target && preview.isPending && <p className="text-muted">Working out what would happen…</p>}
      {target && p && (
        <section className="max-w-3xl space-y-4 rounded border border-border p-4">
          <h2 className="flex flex-wrap items-center gap-2 font-serif text-xl">
            {p.source} → {p.target} <StatusBadge status={target.status} />
          </h2>
          {p.blockers.length > 0 ? (
            <Notice>
              <ul className="list-disc space-y-1 pl-5">
                {p.blockers.map((b) => (
                  <li key={b}>{b}</li>
                ))}
              </ul>
            </Notice>
          ) : (
            <>
              <div>
                <h3 className="font-medium">Carried over to {p.target}</h3>
                <ul className="mt-1 list-disc space-y-0.5 pl-5 text-sm">
                  {p.moves.map((m) => (
                    <li key={m}>{m}</li>
                  ))}
                </ul>
              </div>
              {p.drops.length > 0 && (
                <div>
                  <h3 className="font-medium">Dropped</h3>
                  <ul className="mt-1 list-disc space-y-0.5 pl-5 text-sm">
                    {p.drops.map((d) => (
                      <li key={d}>{d}</li>
                    ))}
                  </ul>
                </div>
              )}
              <ConfirmButton
                label={`Merge into ${p.target}`}
                className={primaryButtonClass}
                question={
                  <>
                    Merge <strong>{p.source}</strong> into <strong>{p.target}</strong>? {p.source} is deleted afterwards, and what is
                    listed as dropped is lost. This cannot be undone.
                  </>
                }
                confirmLabel="Merge"
                disabled={merge.isPending}
                onConfirm={() => merge.mutate()}
              />
            </>
          )}
        </section>
      )}
    </div>
  );
}
