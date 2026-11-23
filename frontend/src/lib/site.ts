/**
 * The public origin, for absolute URLs in the sitemap, robots.txt and OpenGraph tags. A production server refuses
 * to start without it (search engines would be sent to localhost); `next build` may use the fallback.
 */
const configured = process.env.SITE_URL;
if (!configured && process.env.NODE_ENV === "production" && process.env.NEXT_PHASE !== "phase-production-build") {
  throw new Error("SITE_URL is not set: the sitemap, robots.txt and link previews need the public origin.");
}

export const SITE_URL = (configured ?? "http://localhost:3000").replace(/\/$/, "");
