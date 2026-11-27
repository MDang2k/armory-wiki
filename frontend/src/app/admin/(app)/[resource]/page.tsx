import { Suspense } from "react";
import { ResourceList } from "./ResourceList";

export default function ResourceListPage() {
  return (
    // The list reads the resource and its filters from the URL, which is only known per request.
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <ResourceList />
    </Suspense>
  );
}
