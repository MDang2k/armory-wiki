"use client";

export default function ErrorPage({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  return (
    <div className="space-y-3">
      <h1 className="font-serif text-2xl">Something went wrong</h1>
      <p className="text-muted">This page could not be loaded. It may be a temporary problem.</p>
      <button type="button" onClick={reset} className="rounded border border-border bg-surface px-3 py-1 text-sm">
        Try again
      </button>
    </div>
  );
}
