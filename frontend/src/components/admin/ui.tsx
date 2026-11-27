"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import type { Dto } from "@/lib/api/types";

/** Shared classes, so every admin form looks the same without a component library. */
/** A control sized by its content (selects in a row); {@link inputClass} fills its column. */
export const compactInputClass =
  "rounded border border-border bg-background px-2.5 py-1.5 text-sm aria-invalid:border-red-600 dark:aria-invalid:border-red-400";
export const inputClass = `${compactInputClass} w-full`;
export const buttonClass =
  "rounded border border-border bg-surface px-3 py-1.5 text-sm hover:bg-background disabled:cursor-not-allowed disabled:opacity-50";
export const primaryButtonClass =
  "rounded border border-foreground bg-foreground px-3 py-1.5 text-sm text-background hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50";

/** A labelled field with its error underneath; `aside` sits right of the label (a lock, a hint). */
export function Field({
  label,
  htmlFor,
  error,
  hint,
  aside,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  hint?: ReactNode;
  aside?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="space-y-1">
      <div className="flex items-center gap-2">
        <label htmlFor={htmlFor} className="text-sm font-medium">
          {label}
        </label>
        {aside}
      </div>
      {children}
      {error ? (
        <p id={`${htmlFor}-error`} role="alert" className="text-sm text-red-700 dark:text-red-400">
          {error}
        </p>
      ) : hint ? (
        <p className="text-xs text-muted">{hint}</p>
      ) : null}
    </div>
  );
}

export function StatusBadge({ status }: { status: Dto<"PublishStatus"> | null }) {
  if (!status) {
    return null;
  }

  const published = status === "Published";
  return (
    <span
      className={`rounded px-1.5 py-0.5 text-xs font-medium ${published ? "bg-green-100 text-green-900 dark:bg-green-950 dark:text-green-300" : "bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-300"}`}
    >
      {published ? "Published" : "Draft"}
    </span>
  );
}

/** A message box above a form: errors, conflicts, the result of an action. */
export function Notice({ tone = "error", children }: { tone?: "error" | "info"; children: ReactNode }) {
  const colors = tone === "error" ? "border-red-300 bg-red-50 text-red-900 dark:border-red-800 dark:bg-red-950 dark:text-red-200" : "border-border bg-surface";
  return (
    <div role={tone === "error" ? "alert" : "status"} className={`rounded border px-3 py-2 text-sm ${colors}`}>
      {children}
    </div>
  );
}

/**
 * A button that asks first, in a native modal `<dialog>` (PLAN.md Phase 7: delete, unpublish, merge). `onConfirm`
 * runs only after the admin confirms.
 */
export function ConfirmButton({
  label,
  question,
  confirmLabel,
  onConfirm,
  disabled,
  className = buttonClass,
}: {
  label: string;
  question: ReactNode;
  confirmLabel: string;
  onConfirm: () => void;
  disabled?: boolean;
  className?: string;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  return (
    <>
      <button type="button" className={className} disabled={disabled} onClick={() => dialog.current?.showModal()}>
        {label}
      </button>
      <dialog ref={dialog} className="m-auto w-[min(28rem,calc(100vw-2rem))] rounded border text-left whitespace-normal border-border bg-background p-5 text-foreground backdrop:bg-black/40">
        <form method="dialog" className="space-y-4">
          <div className="text-sm">{question}</div>
          <div className="flex justify-end gap-2">
            <button type="submit" className={buttonClass} autoFocus>
              Cancel
            </button>
            <button
              type="button"
              className={primaryButtonClass}
              onClick={() => {
                dialog.current?.close();
                onConfirm();
              }}
            >
              {confirmLabel}
            </button>
          </div>
        </form>
      </dialog>
    </>
  );
}

// The forms on the page with unsaved changes: one set of listeners for all of them, so leaving asks once.
const dirtyForms = new Set<symbol>();

const warnOnUnload = (event: BeforeUnloadEvent) => event.preventDefault();

/** An in-app link (Next's router never fires beforeunload): ask first, and swallow the click when the answer is no. */
function confirmLink(event: MouseEvent) {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
    return;
  }

  const link = event.target instanceof Element ? event.target.closest<HTMLAnchorElement>("a[href]") : null;
  if (!link || link.target === "_blank" || link.origin !== window.location.origin) {
    return;
  }

  if (!window.confirm("This page has unsaved changes. Leave without saving them?")) {
    event.preventDefault();
    event.stopPropagation(); // capture phase on document: the link's own handler never sees the click
  }
}

/** Warns before leaving the page (closing, reloading, or an in-app link) while any form on it is `dirty`. */
export function useUnsavedWarning(dirty: boolean) {
  useEffect(() => {
    if (!dirty) {
      return;
    }

    const id = Symbol();
    if (dirtyForms.size === 0) {
      window.addEventListener("beforeunload", warnOnUnload);
      document.addEventListener("click", confirmLink, true);
    }

    dirtyForms.add(id);
    return () => {
      dirtyForms.delete(id);
      if (dirtyForms.size === 0) {
        window.removeEventListener("beforeunload", warnOnUnload);
        document.removeEventListener("click", confirmLink, true);
      }
    };
  }, [dirty]);
}

/** `value` once it has stopped changing for `ms`. */
export function useDebounced<T>(value: T, ms = 300): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), ms);
    return () => clearTimeout(timer);
  }, [value, ms]);
  return settled;
}
