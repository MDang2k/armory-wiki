import type { Metadata } from "next";
import { Suspense } from "react";
import { EntityList, pageOf } from "@/components/EntityList";
import { LIST_PAGE_SIZE, loadCartridges } from "@/lib/api/loaders";
import { formatFuzzyDate } from "@/lib/dates";
import { CARTRIDGE_TYPES } from "@/lib/labels";

export const metadata: Metadata = { title: "Cartridges", description: "Ammunition types the firearms are chambered for." };

export default function CartridgesPage({ searchParams }: PageProps<"/cartridges">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <Cartridges searchParams={searchParams} />
    </Suspense>
  );
}

async function Cartridges({ searchParams }: { searchParams: PageProps<"/cartridges">["searchParams"] }) {
  const page = pageOf(await searchParams);
  const data = await loadCartridges(page);
  return (
    <EntityList
      title="Cartridges"
      intro="Ammunition types, each defined by its case and bullet, that firearms are chambered for."
      basePath="/cartridges"
      page={page}
      pageSize={LIST_PAGE_SIZE}
      total={data.totalCount}
      items={data.items.map((c) => ({
        slug: c.slug,
        name: c.name,
        detail: [c.type && CARTRIDGE_TYPES[c.type], c.country?.name, c.designDate && formatFuzzyDate(c.designDate)]
          .filter(Boolean)
          .join(", "),
      }))}
    />
  );
}
