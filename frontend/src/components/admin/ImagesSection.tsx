"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useId, useMemo, useState, type DragEvent, type FormEvent } from "react";
import { ConfirmButton, Field, Notice, buttonClass, inputClass, primaryButtonClass } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import { IMAGE_TYPES, imageFileProblem, moved } from "@/lib/admin/images";
import { ApiProblem, fieldErrors, unwrap } from "@/lib/admin/problems";
import type { Dto } from "@/lib/api/types";

/** What images can attach to (`/api/admin/<owner>/{id}/images`). */
export type ImageOwner = "firearms" | "people" | "organizations" | "cartridges";

type AdminImage = Dto<"AdminImageDto">;
type Attribution = { caption: string; author: string; license: string; sourceUrl: string };

const listPath = (owner: ImageOwner) => `/api/admin/${owner}/{id}/images` as "/api/admin/firearms/{id}/images";
const orderPath = (owner: ImageOwner) => `/api/admin/${owner}/{id}/images/order` as "/api/admin/firearms/{id}/images/order";
const blank: Attribution = { caption: "", author: "", license: "", sourceUrl: "" };

/** An API failure as field messages (keys as in {@link Attribution}, plus "file") and a message for the rest. */
function problemOf(error: unknown): { fields: Record<string, string>; text: string | null } {
  if (!(error instanceof ApiProblem)) {
    return { fields: {}, text: "Could not reach the API." };
  }

  if (error.status === 401) {
    return { fields: {}, text: null };
  }

  const fields = fieldErrors(error.errors);
  return { fields, text: Object.keys(fields).length > 0 ? null : error.message };
}

/** The attribution fields every image needs (PLAN.md Phase 7d): license, author and source; the caption is optional. */
function AttributionFields({
  value,
  onChange,
  errors,
  licenses,
}: {
  value: Attribution;
  onChange: (next: Attribution) => void;
  errors: Record<string, string>;
  licenses: Dto<"LicenseDto">[];
}) {
  const uid = useId();
  const set = (key: keyof Attribution) => (e: { target: { value: string } }) => onChange({ ...value, [key]: e.target.value });
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <Field label="Author" htmlFor={`${uid}-author`} error={errors.author} hint="As the source credits it.">
        <input id={`${uid}-author`} value={value.author} onChange={set("author")} required className={inputClass} />
      </Field>
      <Field label="License" htmlFor={`${uid}-license`} error={errors.license}>
        <select id={`${uid}-license`} value={value.license} onChange={set("license")} required className={inputClass}>
          <option value="">Choose…</option>
          {licenses.map((l) => (
            <option key={l.id} value={l.id}>
              {l.name}
            </option>
          ))}
        </select>
      </Field>
      <Field label="Source" htmlFor={`${uid}-source`} error={errors.sourceUrl} hint="Where the image comes from, e.g. its Wikimedia Commons page.">
        <input id={`${uid}-source`} type="url" value={value.sourceUrl} onChange={set("sourceUrl")} required className={inputClass} />
      </Field>
      <Field label="Caption" htmlFor={`${uid}-caption`} error={errors.caption}>
        <input id={`${uid}-caption`} value={value.caption} onChange={set("caption")} className={inputClass} />
      </Field>
    </div>
  );
}

/** One image: thumbnail and attribution, move up or down, edit its attribution, delete. */
function ImageRow({
  image,
  index,
  count,
  licenses,
  onMove,
  busy,
  refresh,
}: {
  image: AdminImage;
  index: number;
  count: number;
  licenses: Dto<"LicenseDto">[];
  onMove: (index: number, by: -1 | 1) => void;
  busy: boolean;
  refresh: () => void;
}) {
  const [editing, setEditing] = useState<Attribution | null>(null);
  const [problem, setProblem] = useState<{ fields: Record<string, string>; text: string | null }>({ fields: {}, text: null });
  const i = image.image;
  const save = useMutation({
    mutationFn: async (a: Attribution) =>
      unwrap(
        await adminApi.PUT("/api/admin/images/{imageId}", {
          params: { path: { imageId: image.id } },
          body: { caption: a.caption.trim() || null, author: a.author.trim(), license: a.license, sourceUrl: a.sourceUrl.trim() },
        }),
      ),
    onMutate: () => setProblem({ fields: {}, text: null }),
    onSuccess: () => {
      setEditing(null);
      refresh();
    },
    onError: (e) => setProblem(problemOf(e)),
  });
  const remove = useMutation({
    mutationFn: async () => unwrap(await adminApi.DELETE("/api/admin/images/{imageId}", { params: { path: { imageId: image.id } } })),
    onSuccess: refresh,
    onError: (e) => setProblem(problemOf(e)),
  });

  return (
    <li className="space-y-2 border-b border-border pb-3">
      <div className="flex flex-wrap items-start gap-3">
        {/* eslint-disable-next-line @next/next/no-img-element -- an already-resized thumbnail from our own media store */}
        <img src={i.thumbUrl} alt={i.caption ?? ""} width={120} height={Math.round((120 * i.height) / i.width)} className="rounded border border-border" />
        <div className="min-w-48 flex-1 space-y-0.5 text-sm">
          <div>{i.caption ?? <span className="text-muted">No caption</span>}</div>
          <div className="text-muted">
            {i.author} · {i.license.name} ·{" "}
            <a href={i.sourceUrl} target="_blank" rel="noopener noreferrer">
              source
            </a>{" "}
            · {i.width}×{i.height}
            {index === 0 && " · shown first"}
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <button type="button" className={buttonClass} aria-label="Move up" disabled={busy || index === 0} onClick={() => onMove(index, -1)}>
            ↑
          </button>
          <button type="button" className={buttonClass} aria-label="Move down" disabled={busy || index === count - 1} onClick={() => onMove(index, 1)}>
            ↓
          </button>
          <button
            type="button"
            className={buttonClass}
            onClick={() => setEditing(editing ? null : { caption: i.caption ?? "", author: i.author, license: i.license.id, sourceUrl: i.sourceUrl })}
          >
            {editing ? "Cancel" : "Edit"}
          </button>
          <ConfirmButton
            label="Delete"
            question={<>Delete this image{i.caption ? <> (“{i.caption}”)</> : null}? This cannot be undone.</>}
            confirmLabel="Delete"
            disabled={remove.isPending}
            onConfirm={() => remove.mutate()}
          />
        </div>
      </div>
      {problem.text && <Notice>{problem.text}</Notice>}
      {editing && (
        <form
          className="space-y-3"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(editing);
          }}
        >
          <AttributionFields value={editing} onChange={setEditing} errors={problem.fields} licenses={licenses} />
          <button type="submit" className={primaryButtonClass} disabled={save.isPending}>
            Save image details
          </button>
        </form>
      )}
    </li>
  );
}

/**
 * The images of one entry (PLAN.md Phase 7d): upload one at a time with its attribution, then edit, reorder and
 * delete. Images are add/remove entries like citations: no version, no conflicts with edits in progress.
 */
export function ImagesSection({ owner, id }: { owner: ImageOwner; id: number }) {
  const queryClient = useQueryClient();
  const uid = useId();
  const key = ["admin", "images", owner, id] as const;
  const images = useQuery({
    queryKey: key,
    queryFn: async () => unwrap(await adminApi.GET(listPath(owner), { params: { path: { id } } })),
  });
  const licenses = useQuery({
    queryKey: ["admin", "image-licenses"],
    queryFn: async () => unwrap(await adminApi.GET("/api/admin/images/licenses")),
    staleTime: Infinity,
  });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: key });

  const [file, setFile] = useState<File | null>(null);
  const [attribution, setAttribution] = useState<Attribution>(blank);
  const [problem, setProblem] = useState<{ fields: Record<string, string>; text: string | null }>({ fields: {}, text: null });
  const [dragging, setDragging] = useState(false);

  // The preview is a blob URL: freed when the file changes or the section goes.
  const preview = useMemo(() => (file ? URL.createObjectURL(file) : null), [file]);
  useEffect(
    () => () => {
      if (preview) {
        URL.revokeObjectURL(preview);
      }
    },
    [preview],
  );

  const choose = (chosen: File | undefined) => {
    if (!chosen) {
      return;
    }

    const wrong = imageFileProblem(chosen);
    setProblem({ fields: wrong ? { file: wrong } : {}, text: null });
    setFile(wrong ? null : chosen);
  };

  const upload = useMutation({
    mutationFn: async () => {
      const form = new FormData();
      form.append("file", file!);
      form.append("author", attribution.author.trim());
      form.append("license", attribution.license);
      form.append("sourceUrl", attribution.sourceUrl.trim());
      if (attribution.caption.trim()) {
        form.append("caption", attribution.caption.trim());
      }

      // ponytail: the generated type describes the fields; the body goes as the multipart form itself.
      return unwrap(await adminApi.POST(listPath(owner), { params: { path: { id } }, body: form as unknown as Dto<"ImageUploadForm"> }));
    },
    onMutate: () => setProblem({ fields: {}, text: null }),
    onSuccess: () => {
      setFile(null);
      setAttribution(blank);
      refresh();
    },
    onError: (e) => setProblem(problemOf(e)),
  });

  const reorder = useMutation({
    mutationFn: async (imageIds: number[]) =>
      unwrap(await adminApi.PUT(orderPath(owner), { params: { path: { id } }, body: { imageIds } })),
    onSettled: refresh,
    onError: (e) => setProblem(problemOf(e)),
  });

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (file) {
      upload.mutate();
    }
  };

  const drop = (event: DragEvent) => {
    event.preventDefault();
    setDragging(false);
    choose(event.dataTransfer.files[0]);
  };

  const list = images.data ?? [];
  const ids = list.map((i) => i.id);
  return (
    <section className="space-y-3 rounded border border-border p-4">
      <h2 className="font-serif text-xl">Images</h2>
      {images.isError && <Notice>Could not load the images: {images.error.message}</Notice>}
      {images.data && list.length === 0 && <p className="text-sm text-muted">No images yet.</p>}
      {list.length > 0 && (
        <ul className="space-y-3">
          {list.map((image, index) => (
            <ImageRow
              key={image.id}
              image={image}
              index={index}
              count={list.length}
              licenses={licenses.data ?? []}
              busy={reorder.isPending}
              onMove={(i, by) => reorder.mutate(moved(ids, i, by))}
              refresh={refresh}
            />
          ))}
        </ul>
      )}

      <form onSubmit={submit} className="space-y-3">
        <h3 className="font-medium">Upload an image</h3>
        {problem.text && <Notice>{problem.text}</Notice>}
        <label
          htmlFor={`${uid}-file`}
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={drop}
          className={`flex cursor-pointer flex-wrap items-center gap-4 rounded border-2 border-dashed p-4 text-sm ${dragging ? "border-foreground" : "border-border"}`}
        >
          {/* eslint-disable-next-line @next/next/no-img-element -- a local blob preview of the chosen file */}
          {preview && <img src={preview} alt="Preview of the chosen image" className="max-h-32 rounded" />}
          <span>
            {file ? `${file.name} (${(file.size / 1024 / 1024).toFixed(1)} MB). Drop or choose another to replace it.` : "Drop a JPEG, PNG or WebP here, or click to choose one (up to 20 MB)."}
          </span>
          <input
            id={`${uid}-file`}
            type="file"
            accept={IMAGE_TYPES.join(",")}
            className="sr-only"
            onChange={(e) => {
              choose(e.target.files?.[0]);
              e.target.value = ""; // choosing the same file again still fires
            }}
          />
        </label>
        {problem.fields.file && (
          <p role="alert" className="text-sm text-red-700 dark:text-red-400">
            {problem.fields.file}
          </p>
        )}
        <AttributionFields value={attribution} onChange={setAttribution} errors={problem.fields} licenses={licenses.data ?? []} />
        <button type="submit" className={primaryButtonClass} disabled={!file || upload.isPending}>
          {upload.isPending ? "Uploading…" : "Upload"}
        </button>
      </form>
    </section>
  );
}
