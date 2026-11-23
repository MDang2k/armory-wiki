import type { components } from "./schema";

/** Shorthand for a DTO from the generated OpenAPI schema: `Dto<"FirearmDetailDto">`. */
export type Dto<Name extends keyof components["schemas"]> = components["schemas"][Name];
