import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { FactTable, Section, Sources } from "@/components/Article";
import { ImageFigure } from "@/components/ImageFigure";
import { Markdown } from "@/components/Markdown";
import { loadOrganization } from "@/lib/api/loaders";
import { settle } from "@/lib/api/settle";
import { footnotes } from "@/lib/citations";
import { formatFuzzyDate } from "@/lib/dates";
import { DESIGNER_ROLES, ORGANIZATION_TYPES } from "@/lib/labels";

export async function generateMetadata({ params }: PageProps<"/organizations/[slug]">): Promise<Metadata> {
  const loaded = await loadOrganization((await params).slug);
  if (loaded.status !== "ok") {
    return { title: "Organization not found" };
  }

  const o = loaded.data;
  return {
    title: o.name,
    description: `${o.name}: firearms designed and manufactured.`,
    openGraph: { images: o.images[0] ? [{ url: o.images[0].mediumUrl }] : undefined },
  };
}

export default function OrganizationPage({ params }: PageProps<"/organizations/[slug]">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <Organization params={params} />
    </Suspense>
  );
}

async function Organization({ params }: { params: PageProps<"/organizations/[slug]">["params"] }) {
  const { slug } = await params;
  const o = settle(await loadOrganization(slug), (current) => `/organizations/${current}`);
  const notes = footnotes(o.citations);
  const years = (from: string | null, to: string | null) =>
    from && to ? `${from} – ${to}` : from ? `from ${from}` : to ? `until ${to}` : null;

  return (
    <article className="space-y-4">
      <h1 className="border-b border-border pb-2 font-serif text-3xl">{o.name}</h1>
      <div className="flow-root">
        <aside aria-label="Infobox" className="mb-4 w-full border border-border bg-surface/60 p-2 md:float-right md:ml-6 md:w-72">
          {o.images[0] && <ImageFigure image={o.images[0]} size="medium" alt={o.name} eager />}
          <FactTable
            rows={[
              { label: "Type", value: o.type && ORGANIZATION_TYPES[o.type], marks: notes.of("type") },
              { label: "Country", value: o.country?.name, marks: notes.of("country") },
              { label: "Founded", value: o.founded && formatFuzzyDate(o.founded), marks: notes.of("founded") },
            ]}
          />
        </aside>
        {o.description && <Markdown>{o.description}</Markdown>}
        {o.designed.length > 0 && (
          <Section title="Firearms designed">
            <ul className="space-y-1">
              {o.designed.map((f) => (
                <li key={f.slug}>
                  <Link href={`/firearms/${f.slug}`}>{f.name}</Link>
                  <span className="text-sm text-muted">
                    {" "}({[f.designDate && formatFuzzyDate(f.designDate), DESIGNER_ROLES[f.role]].filter(Boolean).join(", ")})
                  </span>
                </li>
              ))}
            </ul>
            <p className="mt-2 text-sm">
              <Link href={`/firearms?designer=${o.slug}`}>Filter firearms by this designer</Link>
            </p>
          </Section>
        )}
        {o.manufactured.length > 0 && (
          <Section title="Firearms manufactured">
            <ul className="space-y-1">
              {o.manufactured.map((f) => {
                const span = years(f.from && formatFuzzyDate(f.from), f.to && formatFuzzyDate(f.to));
                return (
                  <li key={f.slug}>
                    <Link href={`/firearms/${f.slug}`}>{f.name}</Link>
                    {(f.isLicensed || span) && (
                      <span className="text-sm text-muted"> ({[f.isLicensed && "under license", span].filter(Boolean).join(", ")})</span>
                    )}
                  </li>
                );
              })}
            </ul>
            <p className="mt-2 text-sm">
              <Link href={`/firearms?manufacturer=${o.slug}`}>Filter firearms by this manufacturer</Link>
            </p>
          </Section>
        )}
      </div>
      <Sources citations={o.citations} />
    </article>
  );
}
