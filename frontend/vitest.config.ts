import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// Unit tests for the logic in src/lib (PLAN.md Phase 5); no component or browser tests.
export default defineConfig({
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  test: { include: ["src/**/*.test.ts"] },
});
