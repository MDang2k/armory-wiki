/**
 * Attribution for prose adapted from Wikipedia (CC BY-SA 4.0, ADR-0001): a link to the exact revision the text came
 * from, or to the article when no revision is recorded.
 */
export function wikipediaUrl(title: string, revisionId: number | null): string {
  const page = encodeURIComponent(title.replaceAll(" ", "_"));
  return revisionId === null
    ? `https://en.wikipedia.org/wiki/${page}`
    : `https://en.wikipedia.org/w/index.php?title=${page}&oldid=${revisionId}`;
}
