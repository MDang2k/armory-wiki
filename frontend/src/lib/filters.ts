/**
 * The /firearms filter state, which lives only in the URL (PLAN.md §5). Multi-value filters are comma-separated
 * slugs: OR within one filter, AND across filters, as the API reads them. Parsing normalizes (sorted, deduplicated,
 * invalid values dropped), so equal filters always give the same URL and the same cache key.
 */

export const MULTI = ["tag", "country", "mechanism", "designer", "manufacturer", "cartridge"] as const;
export type MultiKey = (typeof MULTI)[number];

export const SORTS = ["name", "designYear", "-designYear", "relevance"] as const;
export type Sort = (typeof SORTS)[number];

export const PAGE_SIZE = 24;

export type FirearmFilters = Record<MultiKey, string[]> & {
  q: string;
  /** First year of a decade (single choice: the API takes one design-year range). */
  decade: number | null;
  sort: Sort | null;
  page: number;
};

type SearchParams = Record<string, string | string[] | undefined>;

const SLUG = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const MAX_Q = 200;
const MAX_PAGE = 10_000; // beyond any real result count; keeps absurd values away from the API

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value);

export function parseFilters(params: SearchParams): FirearmFilters {
  const multi = Object.fromEntries(
    MULTI.map((key) => {
      const raw = params[key];
      const values = (Array.isArray(raw) ? raw : [raw ?? ""]).flatMap((v) => v.split(","));
      return [key, [...new Set(values.map((v) => v.trim()).filter((v) => SLUG.test(v)))].sort()];
    }),
  ) as Record<MultiKey, string[]>;

  const decade = Number(first(params.decade));
  const page = Number(first(params.page));
  const sort = first(params.sort);
  return {
    ...multi,
    q: (first(params.q) ?? "").trim().slice(0, MAX_Q),
    decade: Number.isInteger(decade) && decade >= 1000 && decade <= 2990 && decade % 10 === 0 ? decade : null,
    sort: SORTS.includes(sort as Sort) ? (sort as Sort) : null,
    page: Number.isInteger(page) && page > 1 ? Math.min(page, MAX_PAGE) : 1,
  };
}

/** The canonical query string ("" when unfiltered), in a fixed key order. */
export function toQueryString(filters: FirearmFilters): string {
  const params = new URLSearchParams();
  if (filters.q) params.set("q", filters.q);
  for (const key of MULTI) {
    if (filters[key].length > 0) params.set(key, filters[key].join(","));
  }
  if (filters.decade !== null) params.set("decade", String(filters.decade));
  if (filters.sort !== null) params.set("sort", filters.sort);
  if (filters.page > 1) params.set("page", String(filters.page));
  const query = params.toString();
  return query ? `?${query}` : "";
}

/** `/firearms` with these filters. */
export const firearmsHref = (filters: FirearmFilters) => `/firearms${toQueryString(filters)}`;

/** Selects or unselects one value; any change of filters goes back to the first page. */
export function toggle(filters: FirearmFilters, key: MultiKey, value: string): FirearmFilters {
  const values = filters[key].includes(value)
    ? filters[key].filter((v) => v !== value)
    : [...filters[key], value].sort();
  return { ...filters, [key]: values, page: 1 };
}

/** Changes anything but the page, and goes back to the first page. */
export const withFilters = (filters: FirearmFilters, change: Partial<Omit<FirearmFilters, "page">>): FirearmFilters => ({
  ...filters,
  ...change,
  page: 1,
});

export const withPage = (filters: FirearmFilters, page: number): FirearmFilters => ({ ...filters, page });

export const isFiltered = (filters: FirearmFilters) =>
  filters.q !== "" || filters.decade !== null || MULTI.some((key) => filters[key].length > 0);

/** The query the API's list endpoint takes; facets take the same filters without paging and sort. */
export function toApiQuery(filters: FirearmFilters, { paging = true } = {}) {
  return {
    ...(filters.q ? { q: filters.q } : {}),
    ...Object.fromEntries(MULTI.filter((key) => filters[key].length > 0).map((key) => [key, filters[key].join(",")])),
    ...(filters.decade !== null ? { designedFrom: filters.decade, designedTo: filters.decade + 9 } : {}),
    ...(paging && filters.sort !== null ? { sort: filters.sort } : {}),
    ...(paging ? { page: filters.page, pageSize: PAGE_SIZE } : {}),
  } as {
    q?: string;
    designedFrom?: number;
    designedTo?: number;
    sort?: string;
    page?: number;
    pageSize?: number;
  } & Partial<Record<MultiKey, string>>;
}
