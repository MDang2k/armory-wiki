import { cacheLife, cacheTag } from "next/cache";
import { api, toLoaded } from "./client";
import { parseFilters, toApiQuery } from "../filters";

/**
 * Cached reads of the API (PLAN.md §5). Each loader tags every entity type it shows and "all"; the API expires
 * those tags after admin writes and import runs. Arguments are the cache key, so filters arrive as the canonical
 * query string.
 */

const filtersOf = (query: string) => parseFilters(Object.fromEntries(new URLSearchParams(query)));

/** One page of /firearms results. */
export async function loadFirearmList(query: string) {
  "use cache";
  cacheLife("minutes");
  cacheTag("firearms", "reference", "mechanisms", "cartridges", "all");
  const { data, response } = await api.GET("/api/firearms", { params: { query: toApiQuery(filtersOf(query)) } });
  if (!data) {
    throw new Error(`API ${response.status} listing firearms`);
  }

  return data;
}

/** Facet counts for the same filters (each facet ignores its own filter: disjunctive faceting). */
export async function loadFirearmFacets(query: string) {
  "use cache";
  cacheLife("minutes");
  cacheTag("firearms", "reference", "mechanisms", "all");
  const filters = toApiQuery(filtersOf(query), { paging: false });
  const { data, response } = await api.GET("/api/firearms/facets", { params: { query: filters } });
  if (!data) {
    throw new Error(`API ${response.status} counting facets`);
  }

  return data;
}

/** What the home page shows: how many firearms there are and how they divide by tag, era and country. */
export async function loadOverview() {
  "use cache";
  cacheLife("hours");
  cacheTag("firearms", "reference", "all");
  const [list, facets] = await Promise.all([
    api.GET("/api/firearms", { params: { query: { pageSize: 1 } } }),
    api.GET("/api/firearms/facets"),
  ]);
  if (!list.data || !facets.data) {
    throw new Error(`API ${list.response.status}/${facets.response.status} loading the overview`);
  }

  return { total: list.data.totalCount, facets: facets.data };
}

/** A firearm's page: everything it shows (designers, cartridges, mechanism, countries, related firearms). */
export async function loadFirearm(slug: string) {
  "use cache";
  cacheLife("hours");
  cacheTag(`firearm:${slug}`, "firearms", "people", "organizations", "cartridges", "mechanisms", "reference", "all");
  return toLoaded(await api.GET("/api/firearms/{slug}", { params: { path: { slug } } }));
}
