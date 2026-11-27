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

/**
 * Unlocks one lock key: a field ("designDate"), a list ("designers") or a variant's ("variants/12/weightEmptyG",
 * "chamberings/12"). The route's key is a catch-all, so its slashes go as path separators, not as %2F.
 */
export async function unlockField(id: number, version: number, key: string) {
  return unwrap(
    await adminApi.DELETE("/api/admin/firearms/{id}/locks/{key}", {
      params: { path: { id, key }, query: { version } },
      pathSerializer: (path) =>
        path.replace("{id}", String(id)).replace("{key}", key.split("/").map(encodeURIComponent).join("/")),
    }),
  );
}
