import type { Metadata } from "next";
import { Suspense } from "react";
import { EntityList, pageOf } from "@/components/EntityList";
import { LIST_PAGE_SIZE, loadOrganizations } from "@/lib/api/loaders";
import { ORGANIZATION_TYPES } from "@/lib/labels";

export const metadata: Metadata = {
  title: "Organizations",
  description: "Companies, design bureaus and state arsenals that designed or manufactured firearms.",
};

export default function OrganizationsPage({ searchParams }: PageProps<"/organizations">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <Organizations searchParams={searchParams} />
    </Suspense>
  );
}

async function Organizations({ searchParams }: { searchParams: PageProps<"/organizations">["searchParams"] }) {
  const page = pageOf(await searchParams);
  const data = await loadOrganizations(page);
  return (
    <EntityList
      title="Organizations"
      intro="Companies, design bureaus and state arsenals behind the firearms."
      basePath="/organizations"
      page={page}
      pageSize={LIST_PAGE_SIZE}
      total={data.totalCount}
      items={data.items.map((o) => ({
        slug: o.slug,
        name: o.name,
        detail: [o.type && ORGANIZATION_TYPES[o.type], o.country?.name].filter(Boolean).join(", "),
      }))}
    />
  );
}
