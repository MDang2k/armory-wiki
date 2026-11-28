import { cacheLife, cacheTag } from "next/cache";
import { api, toLoaded, type Loaded } from "./client";
import type { Dto } from "./types";
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

export const LIST_PAGE_SIZE = 50;

/** A page of people, organizations or cartridges (alphabetical lists, no search yet). */
export async function loadPeople(page: number) {
  "use cache";
  cacheLife("hours");
  cacheTag("people", "reference", "all");
  const { data, response } = await api.GET("/api/people", { params: { query: { page, pageSize: LIST_PAGE_SIZE } } });
  if (!data) throw new Error(`API ${response.status} listing people`);
  return data;
}

export async function loadOrganizations(page: number) {
  "use cache";
  cacheLife("hours");
  cacheTag("organizations", "reference", "all");
  const { data, response } = await api.GET("/api/organizations", { params: { query: { page, pageSize: LIST_PAGE_SIZE } } });
  if (!data) throw new Error(`API ${response.status} listing organizations`);
  return data;
}

export async function loadCartridges(page: number) {
  "use cache";
  cacheLife("hours");
  cacheTag("cartridges", "reference", "all");
  const { data, response } = await api.GET("/api/cartridges", { params: { query: { page, pageSize: LIST_PAGE_SIZE } } });
  if (!data) throw new Error(`API ${response.status} listing cartridges`);
  return data;
}

export async function loadPerson(slug: string) {
  "use cache";
  cacheLife("hours");
  cacheTag(`person:${slug}`, "people", "firearms", "reference", "all");
  return toLoaded(await api.GET("/api/people/{slug}", { params: { path: { slug } } }));
}

export async function loadOrganization(slug: string) {
  "use cache";
  cacheLife("hours");
  cacheTag(`organization:${slug}`, "organizations", "firearms", "reference", "all");
  return toLoaded(await api.GET("/api/organizations/{slug}", { params: { path: { slug } } }));
}

export async function loadCartridge(slug: string) {
  "use cache";
  cacheLife("hours");
  cacheTag(`cartridge:${slug}`, "cartridges", "firearms", "reference", "all");
  return toLoaded(await api.GET("/api/cartridges/{slug}", { params: { path: { slug } } }));
}

export async function loadMechanismTree() {
  "use cache";
  cacheLife("hours");
  cacheTag("mechanisms", "all");
  const { data, response } = await api.GET("/api/mechanisms");
  if (!data) throw new Error(`API ${response.status} loading mechanisms`);
  return data;
}

export async function loadMechanism(slug: string) {
  "use cache";
  cacheLife("hours");
  cacheTag(`mechanism:${slug}`, "mechanisms", "firearms", "all");
  return toLoaded(await api.GET("/api/mechanisms/{slug}", { params: { path: { slug } } }));
}

/** Base variants side by side; the slugs arrive normalized (comma-joined, at most four). */
export async function loadComparison(slugs: string) {
  "use cache";
  cacheLife("minutes");
  cacheTag("firearms", "cartridges", "all");
  return toLoaded(await api.GET("/api/firearms/compare", { params: { query: { slugs } } }));
}

/** Every published page's path, for the sitemap: each list read to the end, 100 at a time (the API maximum). */
export async function loadSitemapPaths() {
  "use cache";
  cacheLife("hours");
  cacheTag("firearms", "people", "organizations", "cartridges", "mechanisms", "all");
  const pageSize = 100;
  const collect = async (path: "/api/firearms" | "/api/people" | "/api/organizations" | "/api/cartridges", prefix: string) => {
    const paths: string[] = [];
    for (let page = 1; ; page++) {
      const { data, response } = await api.GET(path, { params: { query: { page, pageSize } } });
      if (!data) throw new Error(`API ${response.status} listing ${path} for the sitemap`);
      paths.push(...data.items.map((item) => `${prefix}/${item.slug}`));
      if (page * pageSize >= data.totalCount) return paths;
    }
  };
  const flatten = (nodes: { slug: string; children: { slug: string; children: unknown[] }[] }[]): string[] =>
    nodes.flatMap((n) => [`/mechanisms/${n.slug}`, ...flatten(n.children as typeof nodes)]);

  const [firearms, people, organizations, cartridges, mechanisms] = await Promise.all([
    collect("/api/firearms", "/firearms"),
    collect("/api/people", "/people"),
    collect("/api/organizations", "/organizations"),
    collect("/api/cartridges", "/cartridges"),
    loadMechanismTree(),
  ]);
  return [...firearms, ...people, ...organizations, ...cartridges, ...flatten(mechanisms)];
}

/**
 * A firearm's family tree (GLOSSARY.md). A renamed slug answers 301 to /api/firearms/<current>/family-tree, so the
 * current slug is the segment before "family-tree", not the last one.
 */
export async function loadFamilyTree(slug: string, depth: number): Promise<Loaded<Dto<"FamilyTreeDto">>> {
  "use cache";
  cacheLife("hours");
  cacheTag(`firearm:${slug}`, "firearms", "all");
  const result = await api.GET("/api/firearms/{slug}/family-tree", { params: { path: { slug }, query: { depth } } });
  if (result.response.status === 301) {
    const current = decodeURIComponent(result.response.headers.get("location")?.split("/").at(-2) ?? "");
    if (!current) {
      throw new Error(`API 301 without a Location for ${result.response.url}`);
    }

    return { status: "moved", slug: current };
  }

  return toLoaded(result);
}
