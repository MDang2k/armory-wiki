import { timingSafeEqual } from "node:crypto";

export const SECRET_HEADER = "x-revalidate-secret";
const MAX_TAGS = 100;
const MAX_TAG_LENGTH = 256; // Next ignores longer tags

/** Whether the request carries the shared secret. An unset secret rejects everything. */
export function isAuthorized(header: string | null, secret: string | undefined): boolean {
  if (!secret || !header) {
    return false;
  }

  const given = Buffer.from(header);
  const expected = Buffer.from(secret);
  return given.length === expected.length && timingSafeEqual(given, expected);
}

/** The tags of a `{ "tags": [...] }` body, or null when the body is not that. */
export function parseTags(body: unknown): string[] | null {
  if (typeof body !== "object" || body === null || !("tags" in body) || !Array.isArray(body.tags)) {
    return null;
  }

  const tags: unknown[] = body.tags;
  if (tags.length === 0 || tags.length > MAX_TAGS) {
    return null;
  }

  return tags.every((t): t is string => typeof t === "string" && t.length > 0 && t.length <= MAX_TAG_LENGTH)
    ? [...new Set(tags)]
    : null;
}
