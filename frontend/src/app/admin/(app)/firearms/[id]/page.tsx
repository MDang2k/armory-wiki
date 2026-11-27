import type { Metadata } from "next";
import { Suspense } from "react";
import { FirearmEditor } from "./FirearmEditor";

export const metadata: Metadata = { title: "Edit firearm" };

export default function EditFirearmPage() {
  return (
    // The editor reads the id from the URL, which is only known per request.
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <FirearmEditor />
    </Suspense>
  );
}
