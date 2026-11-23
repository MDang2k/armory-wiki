import type { NextConfig } from "next";

const apiBaseUrl = process.env.API_BASE_URL ?? "http://localhost:5080";

const nextConfig: NextConfig = {
  output: "standalone", // the Docker image runs .next/standalone/server.js (PLAN.md Phase 6)
  cacheComponents: true,
  partialPrefetching: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
  // Development only: the browser talks to one origin, as it will behind Caddy in production (PLAN.md §5).
  // Array rewrites apply after the filesystem, so app/api/revalidate is served by Next, not proxied.
  async rewrites() {
    if (process.env.NODE_ENV !== "development") {
      return [];
    }

    return [
      { source: "/api/:path*", destination: `${apiBaseUrl}/api/:path*` },
      { source: "/media/:path*", destination: `${apiBaseUrl}/media/:path*` },
    ];
  },
};

export default nextConfig;
