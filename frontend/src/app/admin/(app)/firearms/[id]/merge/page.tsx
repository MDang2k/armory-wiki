import type { Metadata } from "next";
import { Suspense } from "react";
import { MergeFirearm } from "./MergeFirearm";

export const metadata: Metadata = { title: "Merge into variant" };

export default function MergePage() {
  return (
    // The screen reads the firearm id from the URL, which is only known per request.
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <MergeFirearm />
    </Suspense>
  );
}
