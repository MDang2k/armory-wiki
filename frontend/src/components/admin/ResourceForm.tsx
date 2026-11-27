"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useId, useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { EntityPicker, FuzzyDateInput, LockToggle, MarkdownInput } from "@/components/admin/fields";
import { Field, Notice, buttonClass, inputClass, primaryButtonClass, useUnsavedWarning } from "@/components/admin/ui";
import { ApiProblem, fieldErrors, isVersionConflict, problemField } from "@/lib/admin/problems";
import {
  createResource,
  loadResource,
  resourceKeys,
  unlockResourceField,
  updateResource,
  type AdminEntity,
  type ResourceApiRoute,
} from "@/lib/admin/resourceApi";
import { resourceFormValues, resourceSchema, toResourceInput, type AnyResourceSpec, type FieldSpec, type PickerResource, type ResourceFormValues } from "@/lib/admin/resources";

/** The picker resource for each kind an API "Unknown <kind>" problem names. */
const PICKER_OF_KIND: Record<string, PickerResource> = {
  country: "countries",
  cartridge: "cartridges",
  mechanism: "mechanisms",
  person: "people",
  organization: "organizations",
  tag: "tags",
  firearm: "firearms",
};

/**
 * The details form of a generic resource (PLAN.md Phase 7c), drawn from its spec: one control per input field.
 * Behaves like the firearm details form: API messages on their fields, locks with Unlock, and a stale version
 * kept on screen until the admin chooses Reload.
 */
export function ResourceForm({
  route,
  spec,
  entity,
}: {
  route: ResourceApiRoute;
  spec: AnyResourceSpec;
  /** Null for a new entry. */
  entity: AdminEntity | null;
}) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<ResourceFormValues>({
    resolver: zodResolver(resourceSchema(spec)),
    defaultValues: resourceFormValues(spec, entity?.details ?? null),
  });
  const { register, control, formState, setError, reset } = form;
  const values = useWatch({ control });
  const [notice, setNotice] = useState<{ conflict: boolean; text: string } | null>(null);
  // Unique per form: Next keeps the previous page (the create form) hidden in the DOM, with the same field names.
  const uid = useId();
  useUnsavedWarning(formState.isDirty);

  const store = (saved: AdminEntity) => {
    queryClient.setQueryData(resourceKeys.one(route, saved.id), saved);
    void queryClient.invalidateQueries({ queryKey: resourceKeys.lists(route) });
  };

  const entries = Object.entries(spec.fields) as [string, FieldSpec][];
  /**
   * The field an "Unknown <kind>" problem is about: the field of that name ("country"), else the one picker of
   * that kind ("Unknown country" on a person is its nationality).
   */
  const fieldOf = (named: string | null) => {
    if (named === null || named in spec.fields) {
      return named;
    }

    const pickers = entries.filter(([, f]) => f.kind === "pick" && f.resource === PICKER_OF_KIND[named]);
    return pickers.length === 1 ? pickers[0][0] : null;
  };

  const showProblem = (error: unknown) => {
    const onField = error instanceof ApiProblem ? fieldOf(problemField(error)) : null;
    if (!(error instanceof ApiProblem)) {
      setNotice({ conflict: false, text: "Could not reach the API." });
    } else if (error.status === 401) {
      // The query client is taking the admin to login.
    } else if (isVersionConflict(error)) {
      setNotice({ conflict: true, text: `This ${spec.title.toLowerCase()} changed elsewhere since you opened it.` });
    } else if (Object.keys(error.errors).length > 0) {
      for (const [field, message] of Object.entries(fieldErrors(error.errors))) {
        if (field in spec.fields) {
          setError(field, { message });
        } else {
          setNotice({ conflict: false, text: message });
        }
      }
    } else if (onField && onField in spec.fields) {
      setError(onField, { message: error.detail ?? error.title });
    } else {
      setNotice({ conflict: false, text: error.message });
    }
  };

  const save = useMutation({
    mutationFn: (v: ResourceFormValues) => {
      const body = toResourceInput(spec, v);
      return entity ? updateResource(route, entity.id, entity.version, body) : createResource(route, body);
    },
    onMutate: () => setNotice(null),
    onSuccess: (saved) => {
      store(saved);
      reset(resourceFormValues(spec, saved.details));
      if (!entity) {
        router.replace(`/admin/${route}/${saved.id}`);
      }
    },
    onError: showProblem,
  });

  const unlock = useMutation({
    mutationFn: (key: string) => unlockResourceField(route, entity!.id, entity!.version, key),
    onSuccess: store,
    onError: showProblem,
  });

  const reload = useMutation({
    mutationFn: () => loadResource(route, entity!.id),
    onSuccess: (fresh) => {
      store(fresh);
      reset(resourceFormValues(spec, fresh.details));
      setNotice(null);
    },
    onError: showProblem,
  });

  const locked = new Set(entity?.lockedFields ?? []);
  const wide = (f: FieldSpec) => f.kind === "markdown";

  const control_ = (name: string, f: FieldSpec) => {
    const id = `${uid}-${name}`;
    const invalid = formState.errors[name] !== undefined;
    const aria = invalid ? { "aria-invalid": true as const, "aria-describedby": `${id}-error` } : {};
    const text = typeof values[name] === "string" ? (values[name] as string) : "";
    switch (f.kind) {
      case "checkbox":
        return <input id={id} type="checkbox" {...register(name)} {...aria} />;
      case "date":
        return <FuzzyDateInput id={id} registration={register(name)} invalid={invalid} value={text} />;
      case "markdown":
        return <MarkdownInput id={id} registration={register(name)} invalid={invalid} value={text} rows={8} />;
      case "pick":
        return <EntityPicker id={id} registration={register(name)} invalid={invalid} value={text} resource={f.resource} />;
      case "lines":
        return <textarea id={id} rows={3} {...register(name)} {...aria} className={inputClass} />;
      case "select":
        return (
          <select id={id} {...register(name)} {...aria} className={inputClass}>
            {!f.required && <option value="">Not known</option>}
            {f.required && <option value="">Choose…</option>}
            {Object.entries(f.options).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        );
      default:
        return (
          <input
            id={id}
            inputMode={f.kind === "whole" || f.kind === "decimal" ? "decimal" : undefined}
            {...register(name)}
            {...aria}
            className={inputClass}
          />
        );
    }
  };

  const field = ([name, f]: [string, FieldSpec]) => (
    <Field
      key={name}
      label={f.label}
      htmlFor={`${uid}-${name}`}
      error={formState.errors[name]?.message}
      hint={f.hint}
      aside={locked.has(name) && <LockToggle field={name} onUnlock={(key) => unlock.mutate(key)} pending={unlock.isPending} />}
    >
      {control_(name, f)}
    </Field>
  );

  return (
    <form onSubmit={form.handleSubmit((v) => save.mutate(v))} className="space-y-5" noValidate>
      {notice && (
        <Notice>
          {notice.text}{" "}
          {notice.conflict && (
            <>
              Your changes are still below. Reload to see the stored version (your changes will be lost).{" "}
              <button type="button" className={buttonClass} disabled={reload.isPending} onClick={() => reload.mutate()}>
                Reload
              </button>
            </>
          )}
        </Notice>
      )}
      <div className="grid gap-4 sm:grid-cols-2">{entries.filter(([, f]) => !wide(f)).map(field)}</div>
      {entries.filter(([, f]) => wide(f)).map(field)}
      <div className="flex items-center gap-3">
        <button type="submit" className={primaryButtonClass} disabled={save.isPending || (entity !== null && !formState.isDirty)}>
          {save.isPending ? "Saving…" : entity ? "Save" : `Create ${spec.title.toLowerCase()}`}
        </button>
        {formState.isDirty && <span className="text-sm text-amber-800 dark:text-amber-300">Unsaved changes</span>}
      </div>
    </form>
  );
}
