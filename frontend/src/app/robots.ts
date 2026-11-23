import type { MetadataRoute } from "next";
import { connection } from "next/server";
import { SITE_URL } from "@/lib/site";

/** Per request: prerendered at build, it would carry the build's fallback origin instead of the deployed SITE_URL. */
export default async function robots(): Promise<MetadataRoute.Robots> {
  await connection();
  return {
    rules: { userAgent: "*", allow: "/", disallow: ["/api/", "/admin", "/compare"] },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
