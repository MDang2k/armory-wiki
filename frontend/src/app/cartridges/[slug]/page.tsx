import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { FactTable, Section, Sources } from "@/components/Article";
import { ImageFigure } from "@/components/ImageFigure";
import { Markdown } from "@/components/Markdown";
import { loadCartridge } from "@/lib/api/loaders";
import { settle } from "@/lib/api/settle";
import { footnotes } from "@/lib/citations";
import { formatFuzzyDate } from "@/lib/dates";
import { CARTRIDGE_TYPES } from "@/lib/labels";
import { formatSmallLength } from "@/lib/units";

export async function generateMetadata({ params }: PageProps<"/cartridges/[slug]">): Promise<Metadata> {
  const loaded = await loadCartridge((await params).slug);
  if (loaded.status !== "ok") {
    return { title: "Cartridge not found" };
  }

  const c = loaded.data;
  return {
    title: c.name,
    description: `${c.name}: dimensions, origin and the firearms chambered for it.`,
    openGraph: { images: c.images[0] ? [{ url: c.images[0].mediumUrl }] : undefined },
  };
}

export default function CartridgePage({ params }: PageProps<"/cartridges/[slug]">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <Cartridge params={params} />
    </Suspense>
  );
}

async function Cartridge({ params }: { params: PageProps<"/cartridges/[slug]">["params"] }) {
  const { slug } = await params;
  const c = settle(await loadCartridge(slug), (current) => `/cartridges/${current}`);
  const notes = footnotes(c.citations);

  return (
    <article className="space-y-4">
      <header className="space-y-1 border-b border-border pb-2">
        <h1 className="font-serif text-3xl">{c.name}</h1>
        {c.alternativeNames.length > 0 && <p className="text-sm text-muted">Also known as {c.alternativeNames.join(", ")}</p>}
      </header>
      <div className="flow-root">
        <aside aria-label="Infobox" className="mb-4 w-full border border-border bg-surface/60 p-2 md:float-right md:ml-6 md:w-72">
          {c.images[0] && <ImageFigure image={c.images[0]} size="medium" alt={c.name} eager />}
          <FactTable
            rows={[
              { label: "Type", value: c.type && CARTRIDGE_TYPES[c.type], marks: notes.of("type") },
              { label: "Country of origin", value: c.country?.name, marks: notes.of("country") },
              { label: "Designed", value: c.designDate && formatFuzzyDate(c.designDate), marks: notes.of("designDate") },
              {
                label: "Parent cartridge",
                value: c.parent && <Link href={`/cartridges/${c.parent.slug}`}>{c.parent.name}</Link>,
                marks: notes.of("parent"),
              },
              { label: "Bullet diameter", value: c.bulletDiameterMm && formatSmallLength(c.bulletDiameterMm), marks: notes.of("bulletDiameterMm") },
              { label: "Case length", value: c.caseLengthMm && formatSmallLength(c.caseLengthMm), marks: notes.of("caseLengthMm") },
              { label: "Overall length", value: c.overallLengthMm && formatSmallLength(c.overallLengthMm), marks: notes.of("overallLengthMm") },
            ]}
          />
        </aside>
        {c.description && <Markdown>{c.description}</Markdown>}
        {c.children.length > 0 && (
          <Section title="Derived cartridges">
            <ul className="space-y-1">
              {c.children.map((child) => (
                <li key={child.slug}>
                  <Link href={`/cartridges/${child.slug}`}>{child.name}</Link>
                </li>
              ))}
            </ul>
          </Section>
        )}
        {c.firearms.length > 0 && (
          <Section title="Firearms chambered for it">
            <ul className="grid gap-x-6 gap-y-1 sm:grid-cols-2">
              {c.firearms.map((f) => (
                <li key={f.slug}>
                  <Link href={`/firearms/${f.slug}`}>{f.name}</Link>
                  {f.designDate && <span className="text-sm text-muted"> ({formatFuzzyDate(f.designDate)})</span>}
                </li>
              ))}
            </ul>
            <p className="mt-2 text-sm">
              <Link href={`/firearms?cartridge=${c.slug}`}>Filter firearms by this cartridge</Link>
            </p>
          </Section>
        )}
      </div>
      <Sources citations={c.citations} />
    </article>
  );
}
