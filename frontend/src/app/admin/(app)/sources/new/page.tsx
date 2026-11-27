import type { Metadata } from "next";
import Link from "next/link";
import { ResourceForm } from "@/components/admin/ResourceForm";
import { sourceSpec } from "@/lib/admin/resources";

export const metadata: Metadata = { title: "New source" };

export default function NewSourcePage() {
  return (
    <div className="space-y-4">
      <p className="text-sm">
        <Link href="/admin/sources">← Sources</Link>
      </p>
      <h1 className="font-serif text-2xl">New source</h1>
      <p className="text-sm text-muted">A book, article, website or other work that entries cite. Sources are cited, never copied.</p>
      <ResourceForm route="sources" spec={sourceSpec} entity={null} />
    </div>
  );
}
