import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { Section } from "@/components/Article";
import { loadMechanism } from "@/lib/api/loaders";
import { settle } from "@/lib/api/settle";
import { formatFuzzyDate } from "@/lib/dates";

export async function generateMetadata({ params }: PageProps<"/mechanisms/[slug]">): Promise<Metadata> {
  const loaded = await loadMechanism((await params).slug);
  if (loaded.status !== "ok") {
    return { title: "Mechanism not found" };
  }

  return { title: loaded.data.name, description: loaded.data.explanation ?? `Firearms using ${loaded.data.name}.` };
}

export default function MechanismPage({ params }: PageProps<"/mechanisms/[slug]">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <Mechanism params={params} />
    </Suspense>
  );
}

async function Mechanism({ params }: { params: PageProps<"/mechanisms/[slug]">["params"] }) {
  const { slug } = await params;
  const m = settle(await loadMechanism(slug), (current) => `/mechanisms/${current}`);

  return (
    <article className="space-y-4">
      <nav aria-label="Breadcrumb" className="text-sm text-muted">
        <Link href="/mechanisms">Operating mechanisms</Link>
        {m.ancestors.map((a) => (
          <span key={a.slug}>
            {" › "}
            <Link href={`/mechanisms/${a.slug}`}>{a.name}</Link>
          </span>
        ))}
      </nav>
      <h1 className="border-b border-border pb-2 font-serif text-3xl">{m.name}</h1>
      {m.explanation && <p className="max-w-3xl text-lg">{m.explanation}</p>}
      {m.children.length > 0 && (
        <Section title="Kinds">
          <ul className="space-y-1">
            {m.children.map((c) => (
              <li key={c.slug}>
                <Link href={`/mechanisms/${c.slug}`}>{c.name}</Link>
              </li>
            ))}
          </ul>
        </Section>
      )}
      <Section title="Firearms">
        {m.firearms.length === 0 ? (
          <p className="text-muted">No published firearm uses this mechanism yet.</p>
        ) : (
          <>
            <ul className="grid gap-x-6 gap-y-1 sm:grid-cols-2">
              {m.firearms.map((f) => (
                <li key={f.slug}>
                  <Link href={`/firearms/${f.slug}`}>{f.name}</Link>
                  {f.designDate && <span className="text-sm text-muted"> ({formatFuzzyDate(f.designDate)})</span>}
                </li>
              ))}
            </ul>
            <p className="mt-2 text-sm">
              <Link href={`/firearms?mechanism=${m.slug}`}>Filter firearms by this mechanism</Link>
            </p>
          </>
        )}
      </Section>
    </article>
  );
}
