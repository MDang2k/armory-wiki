import type { MetadataRoute } from "next";
import { connection } from "next/server";
import { loadSitemapPaths } from "@/lib/api/loaders";
import { SITE_URL } from "@/lib/site";

/** Published pages only (the API lists nothing else). Built per request from cached data: no API at build time. */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  await connection();
  const lists = ["/", "/firearms", "/cartridges", "/mechanisms", "/people", "/organizations", "/timeline"];
  return [...lists, ...(await loadSitemapPaths())].map((path) => ({ url: `${SITE_URL}${path}` }));
}
