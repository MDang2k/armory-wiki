import { Suspense } from "react";
import { ResourceEditor } from "../ResourceEditor";

export default function EditResourcePage() {
  return (
    <Suspense fallback={<p className="text-muted">Loading…</p>}>
      <ResourceEditor />
    </Suspense>
  );
}
