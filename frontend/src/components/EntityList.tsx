import Link from "next/link";
import type { ReactNode } from "react";
import { Pager } from "./Article";

/** An alphabetical, paged list of entities (people, organizations, cartridges). */
export function EntityList({
  title,
  intro,
  items,
  page,
  pageSize,
  total,
  basePath,
}: {
  title: string;
  intro: string;
  items: { slug: string; name: string; detail: ReactNode }[];
  page: number;
  pageSize: number;
  total: number;
  basePath: string;
}) {
  return (
    <div className="space-y-4">
      <h1 className="font-serif text-3xl">{title}</h1>
      <p className="text-muted">{intro}</p>
      {items.length === 0 ? (
        <p>{total > 0 ? <Link href={basePath}>Back to the first page</Link> : "Nothing published yet."}</p>
      ) : (
        <ul className="grid gap-x-6 gap-y-1 sm:grid-cols-2">
          {items.map((item) => (
            <li key={item.slug}>
              <Link href={`${basePath}/${item.slug}`}>{item.name}</Link>
              {item.detail && <span className="text-sm text-muted"> · {item.detail}</span>}
            </li>
          ))}
        </ul>
      )}
      <Pager page={page} pages={Math.ceil(total / pageSize)} href={(p) => (p === 1 ? basePath : `${basePath}?page=${p}`)} />
    </div>
  );
}

/** The ?page= of a list URL: 1 unless a sensible positive integer. */
export function pageOf(params: Record<string, string | string[] | undefined>) {
  const raw = Number(Array.isArray(params.page) ? params.page[0] : params.page);
  return Number.isInteger(raw) && raw > 1 ? Math.min(raw, 10_000) : 1;
}
