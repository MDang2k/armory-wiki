import { adminApi } from "@/lib/admin/client";
import { unwrap } from "@/lib/admin/problems";

/** Query keys: the list (any filters) and one firearm. Writes update the firearm and invalidate the lists. */
export const firearmKeys = {
  lists: ["admin", "firearms"] as const,
  one: (id: number) => ["admin", "firearm", id] as const,
};

export async function loadFirearm(id: number) {
  return unwrap(await adminApi.GET("/api/admin/firearms/{id}", { params: { path: { id } } }));
}

export async function setPublished(id: number, version: number, publish: boolean) {
  const params = { params: { path: { id }, query: { version } } };
  return unwrap(
    publish
      ? await adminApi.POST("/api/admin/firearms/{id}/publish", params)
      : await adminApi.POST("/api/admin/firearms/{id}/unpublish", params),
  );
}

export async function deleteFirearm(id: number, version: number) {
  return unwrap(await adminApi.DELETE("/api/admin/firearms/{id}", { params: { path: { id }, query: { version } } }));
}

export async function unlockField(id: number, version: number, key: string) {
  return unwrap(await adminApi.DELETE("/api/admin/firearms/{id}/locks/{key}", { params: { path: { id, key }, query: { version } } }));
}
