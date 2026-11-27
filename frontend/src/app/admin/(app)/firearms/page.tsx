import type { Metadata } from "next";
import { Suspense } from "react";
import { FirearmList } from "./FirearmList";

export const metadata: Metadata = { title: "Firearms" };

export default function AdminFirearmsPage() {
  return (
    // The list reads its filters from the URL, which is only known per request.
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <FirearmList />
    </Suspense>
  );
}
