import type { Metadata } from "next";
import Link from "next/link";
import { FirearmDetailsForm } from "../FirearmDetailsForm";

export const metadata: Metadata = { title: "New firearm" };

export default function NewFirearmPage() {
  return (
    <div className="space-y-4">
      <p className="text-sm">
        <Link href="/admin/firearms">← Firearms</Link>
      </p>
      <h1 className="font-serif text-2xl">New firearm</h1>
      <p className="text-sm text-muted">It starts as a draft with a base variant of the same name.</p>
      <FirearmDetailsForm firearm={null} />
    </div>
  );
}
