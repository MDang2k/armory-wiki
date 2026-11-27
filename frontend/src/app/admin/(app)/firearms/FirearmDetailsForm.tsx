"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { EntityPicker, FuzzyDateInput, LockToggle, MarkdownInput } from "@/components/admin/fields";
import { Field, Notice, buttonClass, inputClass, primaryButtonClass, useUnsavedWarning } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import { firearmFormSchema, firearmFormValues, toFirearmInput, type FirearmFormValues } from "@/lib/admin/firearmForm";
import { ApiProblem, fieldErrors, isVersionConflict, problemField, unwrap } from "@/lib/admin/problems";
import type { Dto } from "@/lib/api/types";
import { FEED_SYSTEMS } from "@/lib/labels";
import { firearmKeys, loadFirearm, unlockField } from "./firearmApi";

type Firearm = Dto<"AdminFirearmDto">;

/**
 * A firearm's own fields (PLAN.md Phase 7a), for a new firearm (`firearm` null) or an existing one. Field checks
 * that need the database come back from the API onto the same fields; a stale version keeps the admin's input on
 * screen until they choose to reload.
 */
export function FirearmDetailsForm({ firearm }: { firearm: Firearm | null }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<FirearmFormValues>({
    resolver: zodResolver(firearmFormSchema),
    defaultValues: firearmFormValues(firearm?.details ?? null),
  });
  const { register, control, setError, reset, formState } = form;
  const values = useWatch({ control });
  const [notice, setNotice] = useState<{ conflict: boolean; text: string } | null>(null);
  useUnsavedWarning(formState.isDirty);

  const showProblem = (error: unknown) => {
    const onField = error instanceof ApiProblem ? problemField(error) : null;
    if (!(error instanceof ApiProblem)) {
      setNotice({ conflict: false, text: "Could not reach the API." });
    } else if (error.status === 401) {
      // The query client is taking the admin to login.
    } else if (isVersionConflict(error)) {
      setNotice({ conflict: true, text: "This firearm changed elsewhere since you opened it." });
    } else if (Object.keys(error.errors).length > 0) {
      for (const [field, message] of Object.entries(fieldErrors(error.errors))) {
        setError(field as keyof FirearmFormValues, { message });
      }
    } else if (onField && onField in form.getValues()) {
      setError(onField as keyof FirearmFormValues, { message: error.detail ?? error.title });
    } else {
      setNotice({ conflict: false, text: error.message });
    }
  };

  const save = useMutation({
    mutationFn: async (input: FirearmFormValues) => {
      const body = toFirearmInput(input, firearm?.details.wikipediaRevisionId ?? null);
      return firearm
        ? unwrap(await adminApi.PUT("/api/admin/firearms/{id}", { params: { path: { id: firearm.id }, query: { version: firearm.version } }, body }))
        : unwrap(await adminApi.POST("/api/admin/firearms", { body }));
    },
    onMutate: () => setNotice(null),
    onSuccess: (saved) => {
      queryClient.setQueryData(firearmKeys.one(saved.id), saved);
      void queryClient.invalidateQueries({ queryKey: firearmKeys.lists });
      reset(firearmFormValues(saved.details));
      if (!firearm) {
        router.replace(`/admin/firearms/${saved.id}`);
      }
    },
    onError: showProblem,
  });

  const unlock = useMutation({
    mutationFn: (key: string) => unlockField(firearm!.id, firearm!.version, key),
    onSuccess: (saved) => queryClient.setQueryData(firearmKeys.one(saved.id), saved),
    onError: showProblem,
  });

  // Reload after a conflict: the stored firearm replaces the form, discarding the admin's unsaved input.
  const reload = useMutation({
    mutationFn: () => loadFirearm(firearm!.id),
    onSuccess: (fresh) => {
      queryClient.setQueryData(firearmKeys.one(fresh.id), fresh);
      reset(firearmFormValues(fresh.details));
      setNotice(null);
    },
    onError: showProblem,
  });

  const locked = new Set(firearm?.lockedFields ?? []);
  const field = (name: keyof FirearmFormValues, label: string) => ({
    label,
    htmlFor: `firearm-${name}`,
    error: formState.errors[name]?.message,
    aside: locked.has(name) && <LockToggle field={name} onUnlock={(key) => unlock.mutate(key)} pending={unlock.isPending} />,
  });
  const input = (name: keyof FirearmFormValues) => ({
    id: `firearm-${name}`,
    registration: register(name),
    invalid: formState.errors[name] !== undefined,
    value: values[name] ?? "",
  });
  const plain = (name: keyof FirearmFormValues) => {
    const { id, registration, invalid } = input(name);
    return { id, ...registration, className: inputClass, ...(invalid ? { "aria-invalid": true, "aria-describedby": `${id}-error` } : {}) };
  };

  return (
    <form onSubmit={form.handleSubmit((v) => save.mutate(v))} className="space-y-5" noValidate>
      {notice && (
        <Notice>
          {notice.text}{" "}
          {notice.conflict && (
            <>
              Your changes are still below. Reload to see the stored version (your changes will be lost), or copy what you need first.{" "}
              <button type="button" className={buttonClass} disabled={reload.isPending} onClick={() => reload.mutate()}>
                Reload
              </button>
            </>
          )}
        </Notice>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <Field {...field("name", "Name")}>
          <input {...plain("name")} required />
        </Field>
        <Field {...field("slug", "Slug")} hint={firearm ? "The address; the old one keeps working." : "Leave empty to make one from the name."}>
          <input {...plain("slug")} />
        </Field>
        <Field {...field("alternativeNames", "Alternative names")} hint="One per line.">
          <textarea rows={3} {...plain("alternativeNames")} />
        </Field>
        <Field {...field("wikidataId", "Wikidata id")} hint="Like Q12345. Imports match firearms by it.">
          <input {...plain("wikidataId")} />
        </Field>
        <Field {...field("country", "Country of origin")} hint="A country slug.">
          <EntityPicker {...input("country")} resource="countries" />
        </Field>
        <Field {...field("mechanism", "Operating mechanism")} hint="A mechanism slug.">
          <EntityPicker {...input("mechanism")} resource="mechanisms" />
        </Field>
        <Field {...field("feedSystem", "Feed system")}>
          <select {...plain("feedSystem")}>
            <option value="">Not known</option>
            {Object.entries(FEED_SYSTEMS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </Field>
        <Field {...field("designDate", "Design date")}>
          <FuzzyDateInput {...input("designDate")} />
        </Field>
        <Field {...field("productionStart", "Production start")}>
          <FuzzyDateInput {...input("productionStart")} />
        </Field>
        <Field {...field("productionEnd", "Production end")}>
          <FuzzyDateInput {...input("productionEnd")} />
        </Field>
        <Field {...field("wikipediaTitle", "Wikipedia article")} hint="The English article's title, for imported prose and attribution.">
          <input {...plain("wikipediaTitle")} />
        </Field>
      </div>

      <Field {...field("summary", "Summary")}>
        <MarkdownInput {...input("summary")} rows={4} />
      </Field>
      <Field {...field("developmentHistory", "Development history")}>
        <MarkdownInput {...input("developmentHistory")} rows={8} />
      </Field>
      <Field {...field("serviceHistory", "Service history")}>
        <MarkdownInput {...input("serviceHistory")} rows={8} />
      </Field>

      <div className="flex items-center gap-3">
        <button type="submit" className={primaryButtonClass} disabled={save.isPending || (firearm !== null && !formState.isDirty)}>
          {save.isPending ? "Saving…" : firearm ? "Save details" : "Create firearm"}
        </button>
        {formState.isDirty && <span className="text-sm text-amber-800 dark:text-amber-300">Unsaved changes</span>}
      </div>
    </form>
  );
}
