import type { Dto } from "@/lib/api/types";
import { adminApi } from "./client";
import { unwrap } from "./problems";

/*
 * The generic admin resources (people, organizations, cartridges, countries, tags, mechanisms) and sources share
 * one shape of endpoints: list, detail, create, update, delete, and for the publishable ones publish, unpublish
 * and unlock. The calls go through one typed path each and are read back as these shapes.
 * ponytail: one path stands in for all of them; per-resource typing would repeat the same calls six times.
 */

export type ResourceApiRoute = "people" | "organizations" | "cartridges" | "countries" | "tags" | "mechanisms" | "sources";

/** An entity as the admin editor reads it (`AdminEntityDto<TInput>`, or `AdminSourceDto` for a source). */
export type AdminEntity = {
  id: number;
  version: number;
  status?: Dto<"PublishStatus"> | null;
  lockedFields?: string[];
  missingForPublish?: string[];
  citationCount?: number;
  details: Record<string, unknown>;
};

export type AdminListItem = Dto<"AdminEntityListItemDto">;
export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number };

const one = (route: ResourceApiRoute) => `/api/admin/${route}/{id}` as "/api/admin/people/{id}";
const all = (route: ResourceApiRoute) => `/api/admin/${route}` as "/api/admin/people";
const at = (id: number, version: number) => ({ params: { path: { id }, query: { version } } });

export const resourceKeys = {
  lists: (route: ResourceApiRoute) => ["admin", route] as const,
  one: (route: ResourceApiRoute, id: number) => ["admin", route, "one", id] as const,
};

export async function listResource(route: ResourceApiRoute, query: { status?: Dto<"PublishStatus">; q?: string; page?: number; pageSize?: number }) {
  return unwrap(await adminApi.GET(all(route), { params: { query } })) as unknown as Paged<AdminListItem>;
}

export async function loadResource(route: ResourceApiRoute, id: number) {
  return unwrap(await adminApi.GET(one(route), { params: { path: { id } } })) as unknown as AdminEntity;
}

export async function createResource(route: ResourceApiRoute, body: unknown) {
  return unwrap(await adminApi.POST(all(route), { body: body as Dto<"PersonInput"> })) as unknown as AdminEntity;
}

export async function updateResource(route: ResourceApiRoute, id: number, version: number, body: unknown) {
  return unwrap(await adminApi.PUT(one(route), { ...at(id, version), body: body as Dto<"PersonInput"> })) as unknown as AdminEntity;
}

export async function deleteResource(route: ResourceApiRoute, id: number, version: number) {
  unwrap(await adminApi.DELETE(one(route), at(id, version)));
}

export async function setResourcePublished(route: ResourceApiRoute, id: number, version: number, publish: boolean) {
  const path = `/api/admin/${route}/{id}/${publish ? "publish" : "unpublish"}` as "/api/admin/people/{id}/publish";
  return unwrap(await adminApi.POST(path, at(id, version))) as unknown as AdminEntity;
}

export async function unlockResourceField(route: ResourceApiRoute, id: number, version: number, key: string) {
  const path = `/api/admin/${route}/{id}/locks/{key}` as "/api/admin/people/{id}/locks/{key}";
  return unwrap(await adminApi.DELETE(path, { params: { path: { id, key }, query: { version } } })) as unknown as AdminEntity;
}

// Citations ----------------------------------------------------------------------------------------------------

/** What citations can attach to (`/api/admin/<subject>/{id}/citations`). */
export type CitationSubject = "firearms" | "variants" | "people" | "organizations" | "cartridges";

export const citationKeys = { of: (subject: CitationSubject, id: number) => ["admin", "citations", subject, id] as const };

export async function listCitations(subject: CitationSubject, id: number) {
  const path = `/api/admin/${subject}/{id}/citations` as "/api/admin/firearms/{id}/citations";
  return unwrap(await adminApi.GET(path, { params: { path: { id } } }));
}

export async function addCitation(subject: CitationSubject, id: number, body: Dto<"CitationInput">) {
  const path = `/api/admin/${subject}/{id}/citations` as "/api/admin/firearms/{id}/citations";
  return unwrap(await adminApi.POST(path, { params: { path: { id } }, body }));
}

export async function removeCitation(citationId: number) {
  unwrap(await adminApi.DELETE("/api/admin/citations/{citationId}", { params: { path: { citationId } } }));
}

export async function searchSources(q: string) {
  return unwrap(await adminApi.GET("/api/admin/sources", { params: { query: { q: q || undefined, pageSize: 20 } } }));
}
