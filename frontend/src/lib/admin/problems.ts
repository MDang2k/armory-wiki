/**
 * An API failure as the admin UI handles it: the status, the problem's title and detail, and per-field messages
 * from a `ValidationProblem` (400). Admin calls throw it, so TanStack Query sees failures as errors.
 */
export class ApiProblem extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null = null,
    readonly errors: Record<string, string[]> = {},
  ) {
    super(detail ? `${title}: ${detail}` : title);
    this.name = "ApiProblem";
  }
}

type ProblemBody = { title?: string | null; detail?: string | null; errors?: Record<string, string[]> | null };

/** Builds the problem from a failed response and its parsed body (whatever openapi-fetch returned as `error`). */
export function toProblem(response: Response, body: unknown): ApiProblem {
  const problem = (typeof body === "object" && body !== null ? body : {}) as ProblemBody;
  return new ApiProblem(
    response.status,
    problem.title ?? `Request failed (${response.status})`,
    problem.detail ?? null,
    problem.errors ?? {},
  );
}

/** The data of an openapi-fetch result, or its failure as an {@link ApiProblem}. */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (!result.response.ok || result.error !== undefined) {
    throw toProblem(result.response, result.error);
  }

  return result.data as T;
}

/** A write sent with a stale `?version=` (AdminProblems.VersionConflict). Other 409s are value clashes. */
export function isVersionConflict(error: unknown): boolean {
  return error instanceof ApiProblem && error.status === 409 && error.title === "Version conflict";
}

/**
 * The input field a single-message problem is about, when its title names one: "Unknown country" (no country
 * has that slug), "Slug taken", "Wikidata id taken". Null when it concerns the whole form.
 */
export function problemField(problem: ApiProblem): string | null {
  const unknown = /^Unknown (\w+)$/.exec(problem.title);
  if (unknown) {
    return unknown[1];
  }

  return { "Slug taken": "slug", "Wikidata id taken": "wikidataId" }[problem.title] ?? null;
}

/**
 * The API's validation messages keyed by form field. The API names fields by C# property path ("DesignDate.Year",
 * "AlternativeNames[0]"); each message goes to the top-level input field in camelCase ("designDate"), which is
 * also the field's lock key.
 */
export function fieldErrors(errors: Record<string, string[]>): Record<string, string> {
  const byField: Record<string, string[]> = {};
  for (const [path, messages] of Object.entries(errors)) {
    const top = path.split(/[.[]/)[0];
    const field = top.charAt(0).toLowerCase() + top.slice(1);
    (byField[field] ??= []).push(...messages);
  }

  return Object.fromEntries(Object.entries(byField).map(([field, messages]) => [field, messages.join(" ")]));
}
