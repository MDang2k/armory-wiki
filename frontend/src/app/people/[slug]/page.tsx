import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { FactTable, Section, Sources } from "@/components/Article";
import { ImageFigure } from "@/components/ImageFigure";
import { Markdown } from "@/components/Markdown";
import { loadPerson } from "@/lib/api/loaders";
import { settle } from "@/lib/api/settle";
import { footnotes } from "@/lib/citations";
import { formatFuzzyDate } from "@/lib/dates";
import { DESIGNER_ROLES } from "@/lib/labels";

export async function generateMetadata({ params }: PageProps<"/people/[slug]">): Promise<Metadata> {
  const loaded = await loadPerson((await params).slug);
  if (loaded.status !== "ok") {
    return { title: "Person not found" };
  }

  const p = loaded.data;
  return {
    title: p.fullName,
    description: `${p.fullName}: firearms designed${p.firearms.length > 0 ? `, including ${p.firearms.slice(0, 3).map((f) => f.name).join(", ")}` : ""}.`,
    openGraph: { images: p.images[0] ? [{ url: p.images[0].mediumUrl }] : undefined },
  };
}

export default function PersonPage({ params }: PageProps<"/people/[slug]">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <Person params={params} />
    </Suspense>
  );
}

async function Person({ params }: { params: PageProps<"/people/[slug]">["params"] }) {
  const { slug } = await params;
  const p = settle(await loadPerson(slug), (current) => `/people/${current}`);
  const notes = footnotes(p.citations);
  // The design timeline: firearms in order of design (PLAN.md §5).
  const firearms = [...p.firearms].sort((a, b) => (a.designDate?.year ?? 9999) - (b.designDate?.year ?? 9999));

  return (
    <article className="space-y-4">
      <h1 className="border-b border-border pb-2 font-serif text-3xl">{p.fullName}</h1>
      <div className="flow-root">
        <aside aria-label="Infobox" className="mb-4 w-full border border-border bg-surface/60 p-2 md:float-right md:ml-6 md:w-72">
          {p.images[0] && <ImageFigure image={p.images[0]} size="medium" alt={p.fullName} eager />}
          <FactTable
            rows={[
              { label: "Born", value: p.birthDate && formatFuzzyDate(p.birthDate), marks: notes.of("birthDate") },
              { label: "Died", value: p.deathDate && formatFuzzyDate(p.deathDate), marks: notes.of("deathDate") },
              { label: "Nationality", value: p.nationality?.name, marks: notes.of("nationality") },
            ]}
          />
        </aside>
        {p.biography && <Markdown>{p.biography}</Markdown>}
        {firearms.length > 0 && (
          <Section title="Firearms designed">
            <ol className="space-y-1">
              {firearms.map((f) => (
                <li key={f.slug}>
                  <span className="inline-block w-24 text-muted">{f.designDate ? formatFuzzyDate(f.designDate) : "—"}</span>
                  <Link href={`/firearms/${f.slug}`}>{f.name}</Link>
                  <span className="text-sm text-muted"> ({DESIGNER_ROLES[f.role]})</span>
                </li>
              ))}
            </ol>
            <p className="mt-2 text-sm">
              <Link href={`/firearms?designer=${p.slug}`}>Filter firearms by this designer</Link>
            </p>
          </Section>
        )}
      </div>
      <Sources citations={p.citations} />
    </article>
  );
}
