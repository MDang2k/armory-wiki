/** The timeline's filters (PLAN.md Phase 8b), as read from the URL. Years default to 1900–1950. */
export type TimelineFilters = { from: number; to: number; country: string; tag: string };

export const TIMELINE_DEFAULTS = { from: 1900, to: 1950 } as const;
const EARLIEST = 1300;
const LATEST = 2100;

type Search = Record<string, string | string[] | undefined>;

const first = (value: string | string[] | undefined) => (Array.isArray(value) ? value[0] : value) ?? "";

function year(value: string | string[] | undefined, fallback: number) {
  const n = Number(first(value));
  return Number.isInteger(n) && n >= EARLIEST && n <= LATEST ? n : fallback;
}

/** The filters in the URL, with defaults; a reversed range is put the right way round. */
export function parseTimelineFilters(search: Search): TimelineFilters {
  const a = year(search.from, TIMELINE_DEFAULTS.from);
  const b = year(search.to, TIMELINE_DEFAULTS.to);
  const slug = (v: string | string[] | undefined) => (/^[a-z0-9-]+$/.test(first(v)) ? first(v) : "");
  return { from: Math.min(a, b), to: Math.max(a, b), country: slug(search.country), tag: slug(search.tag) };
}

/** The canonical query string for a cache key and links: defaults and empty filters left out. */
export function timelineQuery(f: TimelineFilters): string {
  const params = new URLSearchParams();
  if (f.from !== TIMELINE_DEFAULTS.from) params.set("from", String(f.from));
  if (f.to !== TIMELINE_DEFAULTS.to) params.set("to", String(f.to));
  if (f.country) params.set("country", f.country);
  if (f.tag) params.set("tag", f.tag);
  return params.toString();
}

/**
 * Puts each span in the first row where it fits after the spans already there (greedy interval packing, in
 * start order), so a timeline needs as few rows as the overlaps force. Spans are [start, end) in any unit.
 */
export function packLanes<T>(items: T[], span: (item: T) => [number, number]): { item: T; lane: number }[] {
  const ends: number[] = []; // per lane: where its last span ends
  return items
    .map((item) => ({ item, at: span(item) }))
    .sort((a, b) => a.at[0] - b.at[0] || a.at[1] - b.at[1])
    .map(({ item, at: [start, end] }) => {
      let lane = ends.findIndex((laneEnd) => laneEnd <= start);
      if (lane === -1) {
        lane = ends.length;
        ends.push(end);
      } else {
        ends[lane] = end;
      }

      return { item, lane };
    });
}

/** Axis labels: every decade in the range, or every 5 or 25 years when the range is short or long. */
export function yearTicks(from: number, to: number): number[] {
  const span = to - from;
  const step = span <= 20 ? 5 : span <= 120 ? 10 : 25;
  const ticks: number[] = [];
  for (let y = Math.ceil(from / step) * step; y <= to; y += step) {
    ticks.push(y);
  }

  return ticks;
}
