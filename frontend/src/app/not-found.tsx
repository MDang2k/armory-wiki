import Link from "next/link";

export default function NotFound() {
  return (
    <div className="space-y-3">
      <h1 className="font-serif text-2xl">Page not found</h1>
      <p className="text-muted">There is no page at this address, or it has not been published yet.</p>
      <p>
        <Link href="/firearms">Browse firearms</Link> or use the search above.
      </p>
    </div>
  );
}
