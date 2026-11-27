import type { Metadata } from "next";
import { Suspense } from "react";
import { SourceList } from "./SourceList";

export const metadata: Metadata = { title: "Sources" };

export default function SourcesPage() {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <SourceList />
    </Suspense>
  );
}
