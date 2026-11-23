import type { Metadata } from "next";
import { Suspense } from "react";
import { EntityList, pageOf } from "@/components/EntityList";
import { lifespan } from "@/components/Article";
import { LIST_PAGE_SIZE, loadPeople } from "@/lib/api/loaders";
import { formatFuzzyDate } from "@/lib/dates";

export const metadata: Metadata = { title: "People", description: "Designers of firearms and cartridges." };

export default function PeoplePage({ searchParams }: PageProps<"/people">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <People searchParams={searchParams} />
    </Suspense>
  );
}

async function People({ searchParams }: { searchParams: PageProps<"/people">["searchParams"] }) {
  const page = pageOf(await searchParams);
  const data = await loadPeople(page);
  return (
    <EntityList
      title="People"
      intro="The people credited with designing firearms."
      basePath="/people"
      page={page}
      pageSize={LIST_PAGE_SIZE}
      total={data.totalCount}
      items={data.items.map((p) => ({
        slug: p.slug,
        name: p.fullName,
        detail: [
          lifespan(p.birthDate && formatFuzzyDate(p.birthDate), p.deathDate && formatFuzzyDate(p.deathDate)),
          p.nationality?.name,
        ].filter(Boolean).join(", "),
      }))}
    />
  );
}
