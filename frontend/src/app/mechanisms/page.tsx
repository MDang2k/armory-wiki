import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { Suspense } from "react";
import { loadMechanismTree } from "@/lib/api/loaders";
import type { Dto } from "@/lib/api/types";

export const metadata: Metadata = {
  title: "Operating mechanisms",
  description: "How firearms use energy to cycle their action, from manual operation to gas and recoil systems.",
};

export default function MechanismsPage() {
  return (
    <div className="space-y-4">
      <h1 className="font-serif text-3xl">Operating mechanisms</h1>
      <p className="max-w-2xl text-muted">
        How a firearm uses energy, from the shooter or the cartridge, to cycle its action. Each mechanism includes the
        ones beneath it.
      </p>
      <Suspense fallback={<p className="text-muted">Loading…</p>}>
        <Tree />
      </Suspense>
    </div>
  );
}

/** Per request from cached data, so building the site never needs the API. */
async function Tree() {
  await connection();
  return <Nodes nodes={await loadMechanismTree()} />;
}

function Nodes({ nodes }: { nodes: Dto<"MechanismNodeDto">[] }) {
  return (
    <ul className="space-y-1 border-l border-border pl-4 first:border-l-0 first:pl-0">
      {nodes.map((node) => (
        <li key={node.slug}>
          <Link href={`/mechanisms/${node.slug}`}>{node.name}</Link>
          {node.children.length > 0 && (
            <div className="mt-1">
              <Nodes nodes={node.children} />
            </div>
          )}
        </li>
      ))}
    </ul>
  );
}
