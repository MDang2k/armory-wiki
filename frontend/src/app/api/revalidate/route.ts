import { revalidateTag } from "next/cache";
import { isAuthorized, parseTags, SECRET_HEADER } from "@/lib/revalidate";

/**
 * Called by the API after admin writes and import runs (PLAN.md §5). Tags expire at once (`expire: 0`): the next
 * visit renders fresh data instead of being served the stale page while it refreshes.
 */
export async function POST(request: Request) {
  if (!isAuthorized(request.headers.get(SECRET_HEADER), process.env.REVALIDATE_SECRET)) {
    return Response.json({ error: "Unauthorized" }, { status: 401 });
  }

  const tags = parseTags(await request.json().catch(() => null));
  if (tags === null) {
    return Response.json({ error: "Expected { \"tags\": [\"…\"] }" }, { status: 400 });
  }

  for (const tag of tags) {
    revalidateTag(tag, { expire: 0 });
  }

  return Response.json({ revalidated: tags });
}
