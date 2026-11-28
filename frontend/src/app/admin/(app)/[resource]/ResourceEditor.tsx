"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useState } from "react";
import { CitationsSection } from "@/components/admin/CitationsSection";
import { ImagesSection, type ImageOwner } from "@/components/admin/ImagesSection";
import { ResourceForm } from "@/components/admin/ResourceForm";
import { ConfirmButton, Notice, StatusBadge, buttonClass, useDocumentTitle } from "@/components/admin/ui";
import { ApiProblem, isVersionConflict } from "@/lib/admin/problems";
import { deleteResource, loadResource, resourceKeys, setResourcePublished, type AdminEntity } from "@/lib/admin/resourceApi";
import { RESOURCES, isResourceRoute, type ResourceRoute } from "@/lib/admin/resources";
import { MISSING_FOR_PUBLISH } from "@/lib/labels";

/** The generic resources that have images (firearms have their own editor). */
const IMAGE_OWNERS: readonly string[] = ["people", "organizations", "cartridges"] satisfies ImageOwner[];

function useRoute() {
  const { resource } = useParams<{ resource: string }>();
  return isResourceRoute(resource) ? resource : { unknown: resource };
}

export function ResourceCreate() {
  const route = useRoute();
  useDocumentTitle(typeof route === "string" ? `New ${RESOURCES[route].title.toLowerCase()}` : null);
  if (typeof route !== "string") {
    return <Notice>There is no admin list called “{route.unknown}”.</Notice>;
  }

  const spec = RESOURCES[route];
  return (
    <div className="space-y-4">
      <p className="text-sm">
        <Link href={`/admin/${route}`}>← {spec.plural}</Link>
      </p>
      <h1 className="font-serif text-2xl">New {spec.title.toLowerCase()}</h1>
      {spec.publishable && <p className="text-sm text-muted">It starts as a draft.</p>}
      <ResourceForm route={route} spec={spec} entity={null} />
    </div>
  );
}

/** One generic resource: its status and lifecycle, its details, and its sources when it takes citations. */
export function ResourceEditor() {
  const route = useRoute();
  const { id } = useParams<{ id: string }>();
  if (typeof route !== "string") {
    return <Notice>There is no admin list called “{route.unknown}”.</Notice>;
  }

  return <Editor route={route} id={Number(id)} rawId={id} />;
}

function Editor({ route, id, rawId }: { route: ResourceRoute; id: number; rawId: string }) {
  const spec = RESOURCES[route];
  const router = useRouter();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const entity = useQuery({ queryKey: resourceKeys.one(route, id), queryFn: () => loadResource(route, id), enabled: Number.isInteger(id) });
  useDocumentTitle(entity.data ? String(entity.data.details[spec.nameField] ?? spec.title) : null);

  const onError = (e: unknown) => {
    if (!(e instanceof ApiProblem && e.status === 401)) {
      setError(
        isVersionConflict(e)
          ? "This changed elsewhere since you opened it. Reload the page and try again."
          : e instanceof Error
            ? e.message
            : "Could not reach the API.",
      );
    }
  };
  const store = (saved: AdminEntity) => {
    queryClient.setQueryData(resourceKeys.one(route, saved.id), saved);
    void queryClient.invalidateQueries({ queryKey: resourceKeys.lists(route) });
  };

  const status = useMutation({
    mutationFn: (publish: boolean) => setResourcePublished(route, id, entity.data!.version, publish),
    onMutate: () => setError(null),
    onSuccess: store,
    onError,
  });
  const remove = useMutation({
    mutationFn: () => deleteResource(route, id, entity.data!.version),
    onMutate: () => setError(null),
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: resourceKeys.one(route, id) });
      void queryClient.invalidateQueries({ queryKey: resourceKeys.lists(route) });
      router.replace(`/admin/${route}`);
    },
    onError,
  });

  if (!Number.isInteger(id) || (entity.error instanceof ApiProblem && entity.error.status === 404)) {
    return <Notice>There is no {spec.title.toLowerCase()} #{rawId}.</Notice>;
  }

  if (entity.isPending) {
    return <p className="text-muted">Loading…</p>;
  }

  if (entity.isError) {
    return <Notice>Could not load it: {entity.error.message}</Notice>;
  }

  const e = entity.data;
  const name = String(e.details[spec.nameField] ?? "");
  const slug = String(e.details.slug ?? "");
  const missing = e.missingForPublish ?? [];
  const busy = status.isPending || remove.isPending;
  return (
    <div className="space-y-6">
      <p className="text-sm">
        <Link href={`/admin/${route}`}>← {spec.plural}</Link>
      </p>
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="font-serif text-2xl">{name}</h1>
        {spec.publishable && <StatusBadge status={e.status ?? null} />}
        {spec.publicPath && (!spec.publishable || e.status === "Published") && (
          <Link href={`/${spec.publicPath}/${slug}`} target="_blank" className="text-sm">
            View on the site
          </Link>
        )}
        <div className="ml-auto flex gap-2">
          {spec.publishable && e.status === "Draft" && (
            <button type="button" className={buttonClass} disabled={busy || missing.length > 0} onClick={() => status.mutate(true)}>
              Publish
            </button>
          )}
          {spec.publishable && e.status === "Published" ? (
            <ConfirmButton
              label="Unpublish"
              question={<>Unpublish <strong>{name}</strong>? It disappears from the public site until published again.</>}
              confirmLabel="Unpublish"
              disabled={busy}
              onConfirm={() => status.mutate(false)}
            />
          ) : (
            <ConfirmButton
              label="Delete"
              question={<>Delete <strong>{name}</strong>? This cannot be undone. Anything still referring to it must change first.</>}
              confirmLabel="Delete"
              disabled={busy}
              onConfirm={() => remove.mutate()}
            />
          )}
        </div>
      </div>

      {error && <Notice>{error}</Notice>}
      {spec.publishable && e.status === "Draft" && missing.length > 0 && (
        <Notice tone="info">Before publishing, fill in: {missing.map((m) => MISSING_FOR_PUBLISH[m] ?? m).join(", ")}.</Notice>
      )}

      <section className="space-y-3">
        <h2 className="font-serif text-xl">Details</h2>
        <ResourceForm route={route} spec={spec} entity={e} />
      </section>

      {IMAGE_OWNERS.includes(route) && <ImagesSection owner={route as ImageOwner} id={e.id} />}
      {spec.cited && <CitationsSection subject={route as "people"} id={e.id} fields={Object.keys(spec.fields)} />}
    </div>
  );
}
