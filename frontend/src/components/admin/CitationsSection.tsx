"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Field, Notice, buttonClass, compactInputClass, inputClass, primaryButtonClass, useDebounced } from "@/components/admin/ui";
import { ApiProblem } from "@/lib/admin/problems";
import { addCitation, citationKeys, listCitations, removeCitation, searchSources, type CitationSubject } from "@/lib/admin/resourceApi";

const problem = (e: unknown) =>
  e instanceof ApiProblem ? (e.status === 401 ? null : e.message) : "Could not reach the API.";

/**
 * The sources cited for one entry (GLOSSARY.md: citation). A citation is an add/remove entry: it doesn't touch
 * the entry's version, so it never conflicts with an edit in progress. `fields` are the entry's field names a
 * citation can narrow to.
 */
export function CitationsSection({
  subject,
  id,
  fields,
  title = "Sources",
}: {
  subject: CitationSubject;
  id: number;
  fields: string[];
  title?: string;
}) {
  const queryClient = useQueryClient();
  const key = citationKeys.of(subject, id);
  const citations = useQuery({ queryKey: key, queryFn: () => listCitations(subject, id) });
  const [error, setError] = useState<string | null>(null);

  const [search, setSearch] = useState("");
  const q = useDebounced(search.trim());
  const sources = useQuery({ queryKey: ["admin", "sources", "picker", q], queryFn: () => searchSources(q), staleTime: 60_000 });
  const [sourceId, setSourceId] = useState("");
  const [fieldName, setFieldName] = useState("");
  const [locator, setLocator] = useState("");

  const refresh = () => queryClient.invalidateQueries({ queryKey: key });
  const add = useMutation({
    mutationFn: () => addCitation(subject, id, { sourceId: Number(sourceId), fieldName: fieldName || null, locator: locator.trim() || null }),
    onMutate: () => setError(null),
    onSuccess: () => {
      setFieldName("");
      setLocator("");
      void refresh();
    },
    onError: (e) => setError(problem(e)),
  });
  const remove = useMutation({
    mutationFn: removeCitation,
    onMutate: () => setError(null),
    onSuccess: () => void refresh(),
    onError: (e) => setError(problem(e)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (sourceId) {
      add.mutate();
    }
  };

  const prefix = `${subject}-${id}-citation`;
  return (
    <section className="space-y-3 rounded border border-border p-4">
      <h2 className="font-serif text-xl">{title}</h2>
      {error && <Notice>{error}</Notice>}
      {citations.isError && <Notice>Could not load the citations: {citations.error.message}</Notice>}
      {citations.data && citations.data.length === 0 && <p className="text-sm text-muted">Nothing cited yet.</p>}
      {citations.data && citations.data.length > 0 && (
        <ul className="space-y-1 text-sm">
          {citations.data.map((c) => (
            <li key={c.id} className="flex flex-wrap items-center gap-2">
              <Link href={`/admin/sources/${c.sourceId}`}>{c.sourceTitle}</Link>
              {c.locator && <span className="text-muted">{c.locator}</span>}
              <span className="text-muted">· {c.fieldName ? `for ${c.fieldName}` : "the whole entry"}</span>
              <button type="button" className={`${buttonClass} ml-auto`} disabled={remove.isPending} onClick={() => remove.mutate(c.id)}>
                Remove
              </button>
            </li>
          ))}
        </ul>
      )}

      <form onSubmit={submit} className="grid gap-3 sm:grid-cols-2">
        <Field label="Find a source" htmlFor={`${prefix}-search`} hint={<>By title or author. <Link href="/admin/sources/new" target="_blank">New source</Link></>}>
          <input id={`${prefix}-search`} type="search" value={search} onChange={(e) => setSearch(e.target.value)} className={inputClass} />
        </Field>
        <Field label="Source" htmlFor={`${prefix}-source`}>
          <select id={`${prefix}-source`} value={sourceId} onChange={(e) => setSourceId(e.target.value)} className={inputClass}>
            <option value="">Choose…</option>
            {sources.data?.items.map((s) => (
              <option key={s.id} value={s.id}>
                {[s.details.title, s.details.author, s.details.year].filter(Boolean).join(", ")}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Cites" htmlFor={`${prefix}-field`}>
          <select id={`${prefix}-field`} value={fieldName} onChange={(e) => setFieldName(e.target.value)} className={inputClass}>
            <option value="">The whole entry</option>
            {fields.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Where in the source" htmlFor={`${prefix}-locator`} hint="A page, chapter or timestamp, e.g. p. 42.">
          <input id={`${prefix}-locator`} value={locator} onChange={(e) => setLocator(e.target.value)} className={compactInputClass} />
        </Field>
        <div>
          <button type="submit" className={primaryButtonClass} disabled={!sourceId || add.isPending}>
            Add citation
          </button>
        </div>
      </form>
    </section>
  );
}
