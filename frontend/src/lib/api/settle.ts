import { notFound, permanentRedirect } from "next/navigation";
import type { Loaded } from "./client";

/** Returns the entity, or ends the request: 404 for unknown and draft slugs, 308 to the current slug of a renamed one. */
export function settle<T>(loaded: Loaded<T>, pathOf: (slug: string) => string): T {
  switch (loaded.status) {
    case "ok":
      return loaded.data;
    case "moved":
      permanentRedirect(pathOf(loaded.slug));
    case "missing":
      notFound();
  }
}
