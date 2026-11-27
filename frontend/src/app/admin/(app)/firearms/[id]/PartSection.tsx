"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState, type ComponentType, type ReactNode } from "react";
import { useForm, type DefaultValues, type FieldValues, type UseFormReturn } from "react-hook-form";
import type { z } from "zod";
import { LockToggle } from "@/components/admin/fields";
import { Notice, buttonClass, primaryButtonClass, useUnsavedWarning } from "@/components/admin/ui";
import { ApiProblem, isVersionConflict } from "@/lib/admin/problems";
import type { Dto } from "@/lib/api/types";
import { firearmKeys, loadFirearm, unlockField } from "../firearmApi";

export type Firearm = Dto<"AdminFirearmDto">;

/** What a section's fields get: the form, and the lock marker (with Unlock) for a lock key, or nothing. */
export type PartFieldsProps<F extends FieldValues> = { form: UseFormReturn<F>; lock: (key: string) => ReactNode };

/** Stores a firearm the API answered with: the editor and every section read it from the query cache. */
export function useStoreFirearm() {
  const queryClient = useQueryClient();
  return (saved: Firearm) => {
    queryClient.setQueryData(firearmKeys.one(saved.id), saved);
    void queryClient.invalidateQueries({ queryKey: firearmKeys.lists });
  };
}

/** One message for an API failure shown above a section; null for a 401 (the query client sends it to login). */
export function problemText(error: unknown): string | null {
  if (!(error instanceof ApiProblem)) {
    return "Could not reach the API.";
  }

  if (error.status === 401) {
    return null;
  }

  const fieldMessages = Object.values(error.errors).flat();
  return fieldMessages.length > 0 ? fieldMessages.join(" ") : error.message;
}

/**
 * One part of a firearm with its own form and Save (PLAN.md Phase 7b): the API saves parts separately, so each
 * error stays beside its part and a failed save never leaves a half-saved page. Saving elsewhere on the page
 * keeps this part's unsaved input; a stale version keeps it too, until the admin chooses Reload.
 */
export function PartSection<F extends FieldValues>({
  title,
  firearm,
  lockKey,
  schema,
  fromFirearm,
  write,
  fields: Fields,
  actions,
  saveLabel = "Save",
}: {
  title: ReactNode;
  firearm: Firearm;
  /** The lock key of the part as a whole, for a list ("designers"); fields lock on their own keys. */
  lockKey?: string;
  schema: z.ZodType<F, F>;
  fromFirearm: (firearm: Firearm) => F;
  write: (firearm: Firearm, values: F) => Promise<Firearm>;
  fields: ComponentType<PartFieldsProps<F>>;
  actions?: ReactNode;
  saveLabel?: string;
}) {
  const store = useStoreFirearm();
  const form = useForm<F>({ resolver: zodResolver(schema), defaultValues: fromFirearm(firearm) as DefaultValues<F> });
  const [notice, setNotice] = useState<{ conflict: boolean; text: string } | null>(null);
  useUnsavedWarning(form.formState.isDirty);

  const onError = (error: unknown) => {
    const text = isVersionConflict(error) ? "This firearm changed elsewhere since you opened it." : problemText(error);
    setNotice(text === null ? null : { conflict: isVersionConflict(error), text });
  };

  const save = useMutation({
    mutationFn: (values: F) => write(firearm, values),
    onMutate: () => setNotice(null),
    onSuccess: (saved) => {
      store(saved);
      form.reset(fromFirearm(saved));
    },
    onError,
  });

  const unlock = useMutation({
    mutationFn: (key: string) => unlockField(firearm.id, firearm.version, key),
    onSuccess: store,
    onError,
  });

  // After a conflict: the stored firearm replaces this part's form, discarding its unsaved input.
  const reload = useMutation({
    mutationFn: () => loadFirearm(firearm.id),
    onSuccess: (fresh) => {
      store(fresh);
      form.reset(fromFirearm(fresh));
      setNotice(null);
    },
    onError,
  });

  const locked = new Set(firearm.lockedFields);
  const lock = (key: string) =>
    locked.has(key) ? <LockToggle field={key} onUnlock={(k) => unlock.mutate(k)} pending={unlock.isPending} /> : null;

  return (
    <section className="space-y-3 rounded border border-border p-4">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="font-serif text-xl">{title}</h2>
        {lockKey && lock(lockKey)}
        {actions && <div className="ml-auto flex gap-2">{actions}</div>}
      </div>
      {notice && (
        <Notice>
          {notice.text}{" "}
          {notice.conflict && (
            <>
              Your changes here are still below. Reload to see the stored version (your changes here will be lost).{" "}
              <button type="button" className={buttonClass} disabled={reload.isPending} onClick={() => reload.mutate()}>
                Reload
              </button>
            </>
          )}
        </Notice>
      )}
      <form onSubmit={form.handleSubmit((values) => save.mutate(values))} noValidate className="space-y-3">
        <Fields form={form} lock={lock} />
        <div className="flex items-center gap-3">
          <button type="submit" className={primaryButtonClass} disabled={save.isPending || !form.formState.isDirty}>
            {save.isPending ? "Saving…" : saveLabel}
          </button>
          {form.formState.isDirty && <span className="text-sm text-amber-800 dark:text-amber-300">Unsaved changes</span>}
        </div>
      </form>
    </section>
  );
}

/** The schema message for one field of one row, if any. */
export function rowError(form: UseFormReturn<FieldValues>, index: number, field: string): string | undefined {
  const rows = form.formState.errors.rows as unknown as Record<string, { message?: string }>[] | undefined;
  return rows?.[index]?.[field]?.message;
}
