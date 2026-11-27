"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { ResourceForm } from "@/components/admin/ResourceForm";
import { ConfirmButton, Notice } from "@/components/admin/ui";
import { ApiProblem } from "@/lib/admin/problems";
import { deleteResource, loadResource, resourceKeys } from "@/lib/admin/resourceApi";
import { sourceSpec } from "@/lib/admin/resources";

/** One source: its details, and delete (which the API refuses while citations use it). */
export function SourceEditor() {
  const { id: rawId } = useParams<{ id: string }>();
  const id = Number(rawId);
  const router = useRouter();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const source = useQuery({ queryKey: resourceKeys.one("sources", id), queryFn: () => loadResource("sources", id), enabled: Number.isInteger(id) });
  const remove = useMutation({
    mutationFn: () => deleteResource("sources", id, source.data!.version),
    onMutate: () => setError(null),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: resourceKeys.one("sources", id) });
      void queryClient.invalidateQueries({ queryKey: resourceKeys.lists("sources") });
      router.replace("/admin/sources");
    },
    onError: (e) => setError(e instanceof ApiProblem ? (e.status === 401 ? null : e.message) : "Could not reach the API."),
  });

  if (!Number.isInteger(id) || (source.error instanceof ApiProblem && source.error.status === 404)) {
    return <Notice>There is no source #{rawId}.</Notice>;
  }

  if (source.isPending) {
    return <p className="text-muted">Loading…</p>;
  }

  if (source.isError) {
    return <Notice>Could not load the source: {source.error.message}</Notice>;
  }

  const s = source.data;
  const cited = s.citationCount ?? 0;
  return (
    <div className="space-y-6">
      <p className="text-sm">
        <Link href="/admin/sources">← Sources</Link>
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-serif text-2xl">{String(s.details.title)}</h1>
        <span className="text-sm text-muted">
          cited {cited} {cited === 1 ? "time" : "times"}
        </span>
        <div className="ml-auto">
          <ConfirmButton
            label="Delete"
            question={<>Delete the source <strong>{String(s.details.title)}</strong>? This cannot be undone.</>}
            confirmLabel="Delete"
            disabled={remove.isPending || cited > 0}
            onConfirm={() => remove.mutate()}
          />
        </div>
      </div>
      {cited > 0 && <p className="text-sm text-muted">Remove its citations first to delete it.</p>}
      {error && <Notice>{error}</Notice>}
      <ResourceForm route="sources" spec={sourceSpec} entity={s} />
    </div>
  );
}
