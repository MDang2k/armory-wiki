import type { Metadata } from "next";
import { Suspense } from "react";
import { SourceEditor } from "./SourceEditor";

export const metadata: Metadata = { title: "Edit source" };

export default function EditSourcePage() {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <SourceEditor />
    </Suspense>
  );
}
