/** /compare?slugs=a,b,c: up to four firearms, in the order given (the API compares at most four). */
export const MAX_COMPARED = 4;

const SLUG = /^[a-z0-9]+(-[a-z0-9]+)*$/;

export function parseCompareSlugs(raw: string | string[] | undefined): string[] {
  const values = (Array.isArray(raw) ? raw : [raw ?? ""]).flatMap((v) => v.split(","));
  return [...new Set(values.map((v) => v.trim()).filter((v) => SLUG.test(v)))].slice(0, MAX_COMPARED);
}

export const compareHref = (slugs: string[]) =>
  slugs.length === 0 ? "/compare" : `/compare?slugs=${slugs.join(",")}`;
