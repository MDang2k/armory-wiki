import createClient from "openapi-fetch";
import type { paths } from "@/lib/api/schema";

/**
 * The admin UI's API client, for the browser only (PLAN.md Phase 7). `/api` is same-origin (Caddy in production,
 * the Next proxy in dev), so the browser sends the admin cookie itself. Writes carry the antiforgery token, which
 * is fetched after login, kept in memory, and fetched again once if the API rejects it.
 */
export const adminApi = createClient<paths>({ baseUrl: "", fetch: adminFetch });

const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);
const LOGIN_PATH = "/api/admin/auth/login"; // has no session yet, so no token: the API exempts it
// The API's title for a missing or rejected token (AdminAuth.RequireAntiforgeryHeader).
const ANTIFORGERY_PROBLEM = "Missing or invalid antiforgery token";

let antiforgery: Promise<{ token: string; headerName: string }> | null = null;

/** Drops the token: it belongs to the admin who fetched it, so call this on login and logout. */
export function forgetAntiforgeryToken() {
  antiforgery = null;
}

function antiforgeryToken(refresh = false) {
  if (refresh || antiforgery === null) {
    antiforgery = fetch("/api/admin/auth/antiforgery").then(async (response) => {
      if (!response.ok) {
        antiforgery = null;
        // A 401 here means the session ended: let the write itself answer 401, which sends the admin to login.
        return { token: "", headerName: "X-XSRF-TOKEN" };
      }

      return response.json();
    });
  }

  return antiforgery;
}

async function adminFetch(request: Request): Promise<Response> {
  if (SAFE_METHODS.has(request.method) || new URL(request.url).pathname === LOGIN_PATH) {
    return fetch(request);
  }

  const retry = request.clone();
  const first = await antiforgeryToken();
  request.headers.set(first.headerName, first.token);
  const response = await fetch(request);
  if (response.status !== 400 || (await response.clone().json().catch(() => null))?.title !== ANTIFORGERY_PROBLEM) {
    return response;
  }

  const fresh = await antiforgeryToken(true);
  retry.headers.set(fresh.headerName, fresh.token);
  return fetch(retry);
}
