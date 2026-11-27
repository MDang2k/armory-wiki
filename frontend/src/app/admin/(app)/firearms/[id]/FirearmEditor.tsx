"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { ConfirmButton, Notice, StatusBadge, buttonClass } from "@/components/admin/ui";
import { ApiProblem, isVersionConflict } from "@/lib/admin/problems";
import { MISSING_FOR_PUBLISH } from "@/lib/labels";
import { FirearmDetailsForm } from "../FirearmDetailsForm";
import { deleteFirearm, firearmKeys, loadFirearm, setPublished } from "../firearmApi";

/** One firearm: its status and lifecycle, then its sections (details in 7a; the rest arrive in 7b). */
export function FirearmEditor() {
  const { id } = useParams<{ id: string }>();
  const firearmId = Number(id);
  const router = useRouter();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const firearm = useQuery({
    queryKey: firearmKeys.one(firearmId),
    queryFn: () => loadFirearm(firearmId),
    enabled: Number.isInteger(firearmId),
  });

  const onError = (e: unknown) => {
    if (e instanceof ApiProblem && e.status === 401) {
      return;
    }

    setError(
      isVersionConflict(e)
        ? "This firearm changed elsewhere since you opened it. Reload the page and try again."
        : e instanceof Error
          ? e.message
          : "Could not reach the API.",
    );
  };

  const status = useMutation({
    mutationFn: (publish: boolean) => setPublished(firearmId, firearm.data!.version, publish),
    onMutate: () => setError(null),
    onSuccess: (saved) => {
      queryClient.setQueryData(firearmKeys.one(saved.id), saved);
      void queryClient.invalidateQueries({ queryKey: firearmKeys.lists });
    },
    onError,
  });

  const remove = useMutation({
    mutationFn: () => deleteFirearm(firearmId, firearm.data!.version),
    onMutate: () => setError(null),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: firearmKeys.one(firearmId) });
      void queryClient.invalidateQueries({ queryKey: firearmKeys.lists });
      router.replace("/admin/firearms");
    },
    onError,
  });

  if (!Number.isInteger(firearmId) || (firearm.error instanceof ApiProblem && firearm.error.status === 404)) {
    return <Notice>There is no firearm #{id}.</Notice>;
  }

  if (firearm.isPending) {
    return <p className="text-muted">Loading…</p>;
  }

  if (firearm.isError) {
    return <Notice>Could not load the firearm: {firearm.error.message}</Notice>;
  }

  const f = firearm.data;
  const busy = status.isPending || remove.isPending;
  return (
    <div className="space-y-6">
      <p className="text-sm">
        <Link href="/admin/firearms">← Firearms</Link>
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-serif text-2xl">{f.details.name}</h1>
        <StatusBadge status={f.status} />
        {f.status === "Published" && (
          <Link href={`/firearms/${f.details.slug}`} target="_blank" className="text-sm">
            View on the site
          </Link>
        )}
        <div className="ml-auto flex gap-2">
          {f.status === "Draft" ? (
            <>
              <button type="button" className={buttonClass} disabled={busy || f.missingForPublish.length > 0} onClick={() => status.mutate(true)}>
                Publish
              </button>
              <ConfirmButton
                label="Delete"
                question={<>Delete <strong>{f.details.name}</strong> with its variants, images and citations? This cannot be undone.</>}
                confirmLabel="Delete"
                disabled={busy}
                onConfirm={() => remove.mutate()}
              />
            </>
          ) : (
            <ConfirmButton
              label="Unpublish"
              question={<>Unpublish <strong>{f.details.name}</strong>? It disappears from the public site until published again.</>}
              confirmLabel="Unpublish"
              disabled={busy}
              onConfirm={() => status.mutate(false)}
            />
          )}
        </div>
      </div>

      {error && <Notice>{error}</Notice>}
      {f.status === "Draft" && f.missingForPublish.length > 0 && (
        <Notice tone="info">
          Before publishing, fill in: {f.missingForPublish.map((m) => MISSING_FOR_PUBLISH[m] ?? m).join(", ")}.
        </Notice>
      )}

      <section className="space-y-3">
        <h2 className="font-serif text-xl">Details</h2>
        {/* Not keyed by version: publishing or unlocking must not wipe unsaved input. The form sends the latest version. */}
        <FirearmDetailsForm firearm={f} />
      </section>
    </div>
  );
}
