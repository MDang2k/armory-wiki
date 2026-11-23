import createClient from "openapi-fetch";
import type { paths } from "./schema";

/**
 * Typed client for the ArmoryWiki API, used by Server Components only. Types come from the generated OpenAPI
 * schema (`npm run gen:api`). Redirects are not followed: a renamed slug answers 301, which the page turns into
 * a permanent redirect of its own (see {@link toLoaded}).
 */
export const api = createClient<paths>({
  baseUrl: process.env.API_BASE_URL ?? "http://localhost:5080",
  fetch: (request) => fetch(request, { redirect: "manual" }),
});

/**
 * What a cached loader returns for one entity. Loaders cannot call `notFound()` or `permanentRedirect()`
 * themselves (they would throw inside the cache), so they return this and the page settles it.
 */
export type Loaded<T> =
  | { status: "ok"; data: T }
  | { status: "moved"; slug: string }
  | { status: "missing" };

export function toLoaded<T>(result: { data?: T; response: Response }): Loaded<T> {
  const { data, response } = result;
  if (response.status === 301) {
    // Location: /api/<resource>/<current-slug>
    const slug = decodeURIComponent(response.headers.get("location")?.split("/").pop() ?? "");
    if (!slug) {
      throw new Error(`API 301 without a Location for ${response.url}`); // never a permanent redirect to nowhere
    }

    return { status: "moved", slug };
  }

  if (response.status === 404) {
    return { status: "missing" };
  }

  if (!response.ok || data === undefined) {
    throw new Error(`API ${response.status} for ${response.url}`);
  }

  return { status: "ok", data };
}
