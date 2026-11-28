import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { loadFamilyTree, loadFirearm } from "@/lib/api/loaders";
import { settle } from "@/lib/api/settle";
import { formatFuzzyDate } from "@/lib/dates";
import { layoutTree, treeBranches, treeDepth, type Branch } from "@/lib/familyTree";
import { FamilyGraph } from "./FamilyGraph";

export async function generateMetadata({ params }: PageProps<"/firearms/[slug]/family-tree">): Promise<Metadata> {
  const loaded = await loadFirearm((await params).slug);
  return loaded.status === "ok"
    ? { title: `${loaded.data.name} family tree`, description: `What the ${loaded.data.name} was derived from and what was derived from it.` }
    : { title: "Family tree not found" };
}

/** Rendered per request from cached data; the slug and depth are read inside Suspense so the shell prerenders. */
export default function FamilyTreePage({ params, searchParams }: PageProps<"/firearms/[slug]/family-tree">) {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <FamilyTree params={params} searchParams={searchParams} />
    </Suspense>
  );
}

async function FamilyTree({
  params,
  searchParams,
}: {
  params: PageProps<"/firearms/[slug]/family-tree">["params"];
  searchParams: PageProps<"/firearms/[slug]/family-tree">["searchParams"];
}) {
  const [{ slug }, query] = await Promise.all([params, searchParams]);
  const depth = treeDepth(query.depth);
  const tree = settle(await loadFamilyTree(slug, depth), (current) => `/firearms/${current}/family-tree?depth=${depth}`);
  const { root, ancestors, descendants } = treeBranches(tree);
  const at = layoutTree(tree);
  const year = (n: (typeof tree.nodes)[number]) => (n.designDate ? formatFuzzyDate(n.designDate) : null);

  return (
    <div className="space-y-4">
      <p className="text-sm">
        <Link href={`/firearms/${root.slug}`}>← {root.name}</Link>
      </p>
      <h1 className="font-serif text-3xl">{root.name} family tree</h1>
      <p className="text-sm text-muted">
        What it was derived from or copied from, and what was derived from or copied from it. Copies are drawn dashed.
      </p>

      <nav aria-label="Depth" className="flex flex-wrap items-center gap-2 text-sm">
        <span>Steps away:</span>
        {[1, 2, 3, 4, 5].map((d) =>
          d === depth ? (
            <span key={d} aria-current="true" className="rounded border border-foreground px-2 font-semibold">
              {d}
            </span>
          ) : (
            <Link key={d} href={`/firearms/${root.slug}/family-tree?depth=${d}`} className="rounded border border-border px-2">
              {d}
            </Link>
          ),
        )}
      </nav>

      {tree.truncated && (
        <p role="status" className="rounded border border-border bg-surface px-3 py-2 text-sm">
          This family is large: showing the nearest {tree.nodes.length} firearms. Fewer steps show a smaller, complete part of it.
        </p>
      )}

      {tree.edges.length === 0 ? (
        <p>No firearm is recorded as derived from or copied from the {root.name}, or the other way round.</p>
      ) : (
        <>
          <FamilyGraph
            nodes={tree.nodes.map((n) => ({ slug: n.slug, name: n.name, year: year(n), isRoot: n.isRoot, ...at.get(n.slug)! }))}
            edges={tree.edges}
          />
          <section aria-label="The family as a list" className="grid gap-6 sm:grid-cols-2">
            <div>
              <h2 className="font-serif text-xl">Came from</h2>
              {ancestors.length > 0 ? <Branches branches={ancestors} copyLabel="copy of" year={year} /> : <p className="text-sm text-muted">Nothing recorded.</p>}
            </div>
            <div>
              <h2 className="font-serif text-xl">Led to</h2>
              {descendants.length > 0 ? <Branches branches={descendants} copyLabel="copy" year={year} /> : <p className="text-sm text-muted">Nothing recorded.</p>}
            </div>
          </section>
        </>
      )}
    </div>
  );
}

function Branches({
  branches,
  copyLabel,
  year,
}: {
  branches: Branch[];
  copyLabel: string;
  year: (n: Branch["node"]) => string | null;
}) {
  return (
    <ul className="list-disc space-y-0.5 pl-5 text-sm">
      {branches.map((b) => (
        <li key={b.node.slug}>
          <Link href={`/firearms/${b.node.slug}`}>{b.node.name}</Link>
          {year(b.node) && <span className="text-muted"> ({year(b.node)})</span>}
          {b.isCopy && <span className="text-muted"> · {copyLabel}</span>}
          {b.branches.length > 0 && <Branches branches={b.branches} copyLabel={copyLabel} year={year} />}
        </li>
      ))}
    </ul>
  );
}
