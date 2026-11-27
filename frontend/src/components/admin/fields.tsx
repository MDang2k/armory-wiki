"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import type { UseFormRegisterReturn } from "react-hook-form";
import { Markdown } from "@/components/Markdown";
import { adminApi } from "@/lib/admin/client";
import { parseFuzzyDate } from "@/lib/admin/fuzzyDate";
import { unwrap } from "@/lib/admin/problems";
import { formatFuzzyDate } from "@/lib/dates";
import { inputClass, useDebounced } from "./ui";

type Registered = { id: string; registration: UseFormRegisterReturn; invalid?: boolean };

const errorProps = (id: string, invalid?: boolean) =>
  invalid ? { "aria-invalid": true, "aria-describedby": `${id}-error` } : {};

/** One text field for a historical date, showing how it reads ("circa 1940s"); see `parseFuzzyDate`. */
export function FuzzyDateInput({ id, registration, invalid, value }: Registered & { value: string }) {
  const parsed = parseFuzzyDate(value);
  return (
    <div className="flex items-center gap-3">
      <input id={id} {...registration} {...errorProps(id, invalid)} className={`${inputClass} max-w-44`} placeholder="e.g. 1942, c. 1940s" />
      <span className="text-sm text-muted" aria-live="polite">
        {parsed.ok && parsed.date ? formatFuzzyDate(parsed.date) : ""}
      </span>
    </div>
  );
}

/** Markdown prose: a textarea with a preview rendered exactly as the public page renders it. */
export function MarkdownInput({ id, registration, invalid, value, rows = 6 }: Registered & { value: string; rows?: number }) {
  const [preview, setPreview] = useState(false);
  const tab = (on: boolean) => `px-2 py-0.5 text-xs ${on ? "border-b-2 border-foreground font-medium" : "text-muted"}`;
  return (
    <div>
      <div className="mb-1 flex gap-1" role="group" aria-label="Write or preview">
        <button type="button" className={tab(!preview)} aria-pressed={!preview} onClick={() => setPreview(false)}>
          Write
        </button>
        <button type="button" className={tab(preview)} aria-pressed={preview} onClick={() => setPreview(true)}>
          Preview
        </button>
      </div>
      {/* Hidden, not unmounted, so the field stays registered and keeps its text. */}
      <textarea id={id} rows={rows} {...registration} {...errorProps(id, invalid)} className={`${inputClass} font-mono ${preview ? "hidden" : ""}`} />
      {preview && (
        <div className="min-h-24 rounded border border-border px-3 py-1">
          {value.trim() ? <Markdown>{value}</Markdown> : <p className="my-2 text-muted">Nothing to preview.</p>}
        </div>
      )}
    </div>
  );
}

/** The admin list endpoints a picker can search; they all answer pages of `AdminEntityListItemDto`. */
export type PickerResource = "countries" | "mechanisms" | "people" | "organizations" | "cartridges" | "tags" | "firearms";

/**
 * Picks an entity by slug (PLAN.md Phase 7): a text field with a native `<datalist>` of the ten best matches from
 * the resource's admin list, drafts included and labelled. Typing a slug by hand works too; the API reports an
 * unknown one on this field.
 */
export function EntityPicker({
  id,
  registration,
  invalid,
  value,
  resource,
  label,
}: Registered & { value: string; resource: PickerResource; /** For a picker without a visible label (a row in a list). */ label?: string }) {
  const q = useDebounced(value.trim());
  const matches = useQuery({
    queryKey: ["admin", resource, "picker", q],
    queryFn: async () =>
      // ponytail: one path stands in for them all; their query and answer shapes are the same.
      unwrap(await adminApi.GET(`/api/admin/${resource}` as "/api/admin/countries", { params: { query: { q: q || undefined, pageSize: 10 } } })),
    staleTime: 60_000,
  });

  return (
    <>
      <input id={id} aria-label={label} list={`${id}-options`} autoComplete="off" {...registration} {...errorProps(id, invalid)} className={inputClass} />
      <datalist id={`${id}-options`}>
        {matches.data?.items.map((item) => (
          <option key={item.id} value={item.slug} label={item.status === "Draft" ? `${item.name} (draft)` : item.name} />
        ))}
      </datalist>
    </>
  );
}

/** Shown next to a locked field's label: imports leave the field alone until it is unlocked. */
export function LockToggle({ field, onUnlock, pending }: { field: string; onUnlock: (field: string) => void; pending: boolean }) {
  return (
    <span className="inline-flex items-center gap-1 text-xs text-muted" title="Changed by hand: imports will not overwrite it.">
      <svg aria-hidden="true" viewBox="0 0 16 16" className="size-3 fill-current">
        <path d="M4 7V5a4 4 0 1 1 8 0v2h1v8H3V7h1Zm2 0h4V5a2 2 0 1 0-4 0v2Z" />
      </svg>
      Locked
      <button type="button" className="underline disabled:opacity-50" disabled={pending} onClick={() => onUnlock(field)}>
        Unlock
      </button>
    </span>
  );
}
