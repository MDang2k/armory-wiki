import { Suspense } from "react";
import { ResourceCreate } from "../ResourceEditor";

export default function NewResourcePage() {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <ResourceCreate />
    </Suspense>
  );
}
