import type { Metadata } from "next";
import Link from "next/link";
import { Suspense, type ReactNode } from "react";
import { Section, Sources } from "@/components/Article";
import { FootnoteMarks } from "@/components/FootnoteMarks";
import { ImageFigure } from "@/components/ImageFigure";
import { Markdown } from "@/components/Markdown";
import { loadFirearm } from "@/lib/api/loaders";
import { settle } from "@/lib/api/settle";
import type { Dto } from "@/lib/api/types";
import { footnotes, type Footnotes } from "@/lib/citations";
import { formatFuzzyDate, type FuzzyDate } from "@/lib/dates";
import { wikipediaUrl } from "@/lib/wikipedia";
import { VariantSpecs } from "./VariantSpecs";
import { VideoList } from "./VideoList";

type Firearm = Dto<"FirearmDetailDto">;

export async function generateMetadata({ params }: PageProps<"/firearms/[slug]">): Promise<Metadata> {
  const loaded = await loadFirearm((await params).slug);
  if (loaded.status !== "ok") {
    return { title: "Firearm not found" };
  }

  const f = loaded.data;
  return {
    title: f.name,
    description: f.summary ?? `${f.name}: history, designers, specifications and variants.`,
    openGraph: {
      title: f.name,
      description: f.summary ?? undefined,
      images: f.images[0] ? [{ url: f.images[0].mediumUrl }] : undefined,
    },
  };
}

/** Rendered per request from cached data; the slug is read inside Suspense so the shell prerenders (PLAN.md §5). */
export default function FirearmPage({ params }: PageProps<"/firearms/[slug]">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <FirearmArticle params={params} />
    </Suspense>
  );
}

async function FirearmArticle({ params }: { params: PageProps<"/firearms/[slug]">["params"] }) {
  const { slug } = await params;
  const f = settle(await loadFirearm(slug), (current) => `/firearms/${current}`);
  const notes = footnotes(f.citations);
  const [lead, ...gallery] = f.images;

  return (
    <article className="space-y-6">
      <header className="space-y-1 border-b border-border pb-2">
        <h1 className="font-serif text-3xl">{f.name}</h1>
        {f.alternativeNames.length > 0 && (
          <p className="text-sm text-muted">Also known as {f.alternativeNames.join(", ")}</p>
        )}
      </header>

      {/* The summary leads on every screen; the infobox floats beside the prose on wide ones. */}
      {f.summary && <p className="text-lg">{f.summary}</p>}
      <div className="flow-root">
        <Infobox firearm={f} lead={lead} notes={notes} />
        <p className="text-sm">
          <Link href={`/compare?slugs=${encodeURIComponent(f.slug)}`}>Compare with other firearms</Link>
        </p>

        {f.developmentHistory && (
          <Section title="Development history">
            <Markdown>{f.developmentHistory}</Markdown>
          </Section>
        )}
        {f.serviceHistory && (
          <Section title="Service history">
            <Markdown>{f.serviceHistory}</Markdown>
          </Section>
        )}
      </div>

      {f.adoptions.length > 0 && <Adoptions adoptions={f.adoptions} notes={notes} />}
      <Related firearm={f} />
      {gallery.length > 0 && (
        <Section title="Gallery">
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {gallery.map((image, i) => (
              <li key={i}>
                <ImageFigure image={image} size="thumb" alt={f.name} />
              </li>
            ))}
          </ul>
        </Section>
      )}
      {f.videos.length > 0 && (
        <Section title="Videos">
          <VideoList videos={f.videos} />
        </Section>
      )}
      <Sources citations={f.citations} variantName={(v) => f.variants.find((x) => x.slug === v)?.name} />
      {f.wikipedia && (
        <p className="border-t border-border pt-2 text-xs text-muted">
          Text adapted from the Wikipedia article{" "}
          <a href={wikipediaUrl(f.wikipedia.title, f.wikipedia.revisionId)} rel="noopener noreferrer">
            “{f.wikipedia.title}”
          </a>
          {f.wikipedia.revisionId !== null && " (the revision linked)"}, available under the{" "}
          <a href="https://creativecommons.org/licenses/by-sa/4.0/" rel="license noopener noreferrer">
            CC BY-SA 4.0
          </a>{" "}
          license; see the article&apos;s history for its authors.
        </p>
      )}
    </article>
  );
}

// Infobox ----------------------------------------------------------------------------------------------------------

const ROLES: Record<Dto<"DesignerRole">, string> = {
  LeadDesigner: "lead designer",
  CoDesigner: "co-designer",
  Developer: "developer",
};

const FEED: Record<NonNullable<Dto<"FeedSystem">>, string> = {
  DetachableBoxMagazine: "Detachable box magazine",
  InternalMagazine: "Internal magazine",
  Belt: "Belt",
  Drum: "Drum magazine",
  Tubular: "Tubular magazine",
  Cylinder: "Revolving cylinder",
  StripperClip: "Stripper clip",
  EnBlocClip: "En-bloc clip",
  SingleShot: "Single shot",
  Other: "Other",
};

const span = (from: FuzzyDate | null, to: FuzzyDate | null) =>
  from && to ? `${formatFuzzyDate(from)} – ${formatFuzzyDate(to)}` : from ? `from ${formatFuzzyDate(from)}` : to ? `until ${formatFuzzyDate(to)}` : null;

function Infobox({ firearm: f, lead, notes }: { firearm: Firearm; lead?: Dto<"ImageDto">; notes: Footnotes }) {
  const tags = (group: Dto<"TagGroup">) =>
    f.tags.filter((t) => t.group === group).map((t) => (
      <Link key={t.slug} href={`/firearms?tag=${t.slug}`} className="mr-2">
        {t.name}
      </Link>
    ));
  const rows: [string, ReactNode, string][] = [
    ["Type", tags("Type"), "tags"],
    ["Role", tags("Role"), ""], // a tags citation is marked once, on Type
    ["Country of origin", f.country && <Link href={`/firearms?country=${f.country.slug}`}>{f.country.name}</Link>, "country"],
    ["Designed", f.designDate && formatFuzzyDate(f.designDate), "designDate"],
    [
      f.designers.length > 1 ? "Designers" : "Designer",
      f.designers.length > 0 && (
        <ul>
          {f.designers.map((d) => {
            const who = d.person
              ? <Link href={`/people/${d.person.slug}`}>{d.person.name}</Link>
              : d.organization && <Link href={`/organizations/${d.organization.slug}`}>{d.organization.name}</Link>;
            return (
              <li key={(d.person ?? d.organization)?.slug}>
                {who}
                {f.designers.length > 1 && <span className="text-muted"> ({ROLES[d.role]})</span>}
              </li>
            );
          })}
        </ul>
      ),
      "designers",
    ],
    [
      f.manufacturers.length > 1 ? "Manufacturers" : "Manufacturer",
      f.manufacturers.length > 0 && (
        <ul>
          {f.manufacturers.map((m) => (
            <li key={m.organization.slug}>
              <Link href={`/organizations/${m.organization.slug}`}>{m.organization.name}</Link>
              {(m.isLicensed || m.from || m.to) && (
                <span className="text-muted">
                  {" "}({[m.isLicensed && "under license", span(m.from, m.to)].filter(Boolean).join(", ")})
                </span>
              )}
            </li>
          ))}
        </ul>
      ),
      "manufacturers",
    ],
    ["Produced", span(f.productionStart, f.productionEnd), "productionStart"],
    ["Operating mechanism", f.mechanism && <Link href={`/mechanisms/${f.mechanism.slug}`}>{f.mechanism.name}</Link>, "mechanism"],
    ["Feed", f.feedSystem && FEED[f.feedSystem], "feedSystem"],
    ["Era", tags("Era"), ""],
  ];
  const marks: Record<string, number[]> = {};
  for (const v of f.variants) {
    for (const field of ["chamberings", "introducedDate", "weightEmptyG", "overallLengthMm", "overallLengthFoldedMm", "barrelLengthMm",
      "rateOfFireMinRpm", "muzzleVelocityMps", "effectiveRangeM", "magazineCapacityStandard", "firingModes", "extraSpecs"]) {
      const numbers = notes.of(field, v.slug);
      if (numbers.length > 0) {
        marks[`${v.slug}/${field}`] = numbers;
      }
    }
  }

  return (
    <aside aria-label="Infobox" className="mb-4 w-full border border-border bg-surface/60 p-2 text-sm md:float-right md:ml-6 md:w-80">
      {lead && <ImageFigure image={lead} size="medium" alt={f.name} eager />}
      <table className="mt-2 w-full">
        <tbody>
          {rows
            .filter(([, value]) => value && !(Array.isArray(value) && value.length === 0))
            .map(([label, value, field]) => (
              <tr key={label} className="border-t border-border align-top">
                <th scope="row" className="w-32 py-1 pr-2 text-left font-semibold">
                  {label}
                </th>
                <td className="py-1">
                  {value}
                  <FootnoteMarks numbers={notes.of(field)} />
                </td>
              </tr>
            ))}
          <VariantSpecs variants={f.variants} marks={marks} />
        </tbody>
      </table>
    </aside>
  );
}

// Below the prose ----------------------------------------------------------------------------------------------------

function Adoptions({ adoptions, notes }: { adoptions: Dto<"AdoptionDto">[]; notes: Footnotes }) {
  return (
    <Section title="Adoption">
      <table className="w-full max-w-2xl text-sm">
        <thead>
          <tr className="text-left">
            <th className="py-1 pr-4">Country<FootnoteMarks numbers={notes.of("adoptions")} /></th>
            <th className="py-1 pr-4">In service</th>
            <th className="py-1">Note</th>
          </tr>
        </thead>
        <tbody>
          {adoptions.map((a, i) => (
            <tr key={i} className="border-t border-border align-top">
              <td className="py-1 pr-4">{a.country.name}</td>
              <td className="py-1 pr-4">{span(a.from, a.to) ?? "—"}</td>
              <td className="py-1">{a.note}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Section>
  );
}

// How the other firearm relates to this one, read from each side (GLOSSARY.md: how firearms relate).
const FROM: Record<Dto<"RelationType">, string> = {
  DerivedFrom: "Derived from",
  CopyOf: "Copy of",
  InfluencedBy: "Influenced by",
  CompetedAgainst: "Competed against",
  Replaced: "Replaced",
};
const TO: Record<Dto<"RelationType">, string> = {
  DerivedFrom: "Derivatives",
  CopyOf: "Copies",
  InfluencedBy: "Influenced",
  CompetedAgainst: "Competed against",
  Replaced: "Replaced by",
};

function Related({ firearm: f }: { firearm: Firearm }) {
  const groups = new Map<string, Dto<"SlugName">[]>();
  const add = (label: string, other: Dto<"SlugName">) => {
    const list = groups.get(label) ?? [];
    if (!list.some((o) => o.slug === other.slug)) {
      groups.set(label, [...list, other]);
    }
  };
  f.relationsFrom.forEach((r) => add(FROM[r.type], r.firearm));
  f.relationsTo.forEach((r) => add(TO[r.type], r.firearm));
  if (groups.size === 0) {
    return null;
  }

  return (
    <Section title="Related firearms">
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        {[...groups].map(([label, others]) => (
          <div key={label} className="contents">
            <dt className="font-semibold">{label}</dt>
            <dd>
              {others.map((o, i) => (
                <span key={o.slug}>
                  {i > 0 && ", "}
                  <Link href={`/firearms/${o.slug}`}>{o.name}</Link>
                </span>
              ))}
            </dd>
          </div>
        ))}
      </dl>
    </Section>
  );
}
