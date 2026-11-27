"use client";

import { useMutation } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useFieldArray, useWatch } from "react-hook-form";
import { CitationsSection } from "@/components/admin/CitationsSection";
import { EntityPicker, FuzzyDateInput } from "@/components/admin/fields";
import { ConfirmButton, Field, Notice, buttonClass, inputClass, primaryButtonClass } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import {
  VARIANT_NUMBERS,
  chamberingsForm,
  chamberingsSchema,
  toChamberingsInput,
  toVariantInput,
  variantForm,
  variantSchema,
  type ChamberingsForm,
  type VariantForm,
} from "@/lib/admin/firearmParts";
import { unwrap } from "@/lib/admin/problems";
import { FIRING_MODES } from "@/lib/labels";
import { PartSection, problemText, rowError, useStoreFirearm, type Firearm, type PartFieldsProps } from "./PartSection";

/*
 * Variants (PLAN.md Phase 7b): each has its details and its ammunition as two sections, plus make-base and delete.
 * The base variant is the one the firearm's name refers to (GLOSSARY.md); its primary cartridge is a publish
 * requirement.
 */

const variantOf = (f: Firearm, variantId: number) => f.variants.find((v) => v.id === variantId);

function VariantFields({ form, lock, variantId }: PartFieldsProps<VariantForm> & { variantId: number }) {
  const values = useWatch({ control: form.control });
  const errors = form.formState.errors;
  const key = (field: string) => `variants/${variantId}/${field}`;
  const id = (field: string) => `variant-${variantId}-${field}`;
  const invalid = (field: keyof VariantForm) =>
    errors[field] ? { "aria-invalid": true as const, "aria-describedby": `${id(field)}-error` } : {};

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-3">
        <Field label="Name" htmlFor={id("name")} error={errors.name?.message} aside={lock(key("name"))}>
          <input id={id("name")} {...form.register("name")} {...invalid("name")} className={inputClass} />
        </Field>
        <Field label="Slug" htmlFor={id("slug")} error={errors.slug?.message} aside={lock(key("slug"))} hint="Unique within this firearm.">
          <input id={id("slug")} {...form.register("slug")} {...invalid("slug")} className={inputClass} />
        </Field>
        <Field label="Introduced" htmlFor={id("introducedDate")} error={errors.introducedDate?.message} aside={lock(key("introducedDate"))}>
          <FuzzyDateInput
            id={id("introducedDate")}
            registration={form.register("introducedDate")}
            invalid={errors.introducedDate !== undefined}
            value={values.introducedDate ?? ""}
          />
        </Field>
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        {VARIANT_NUMBERS.map(([field, label]) => (
          <Field key={field} label={label} htmlFor={id(field)} error={errors[field]?.message} aside={lock(key(field))}>
            <input id={id(field)} inputMode="numeric" {...form.register(field)} {...invalid(field)} className={inputClass} />
          </Field>
        ))}
      </div>
      <fieldset className="space-y-1">
        <legend className="flex items-center gap-2 text-sm font-medium">Firing modes {lock(key("firingModes"))}</legend>
        <div className="flex flex-wrap gap-4 text-sm">
          {Object.entries(FIRING_MODES).map(([value, label]) => (
            <label key={value} className="flex items-center gap-1">
              <input type="checkbox" value={value} {...form.register("firingModes")} /> {label}
            </label>
          ))}
        </div>
      </fieldset>
      <Field
        label="Other specifications"
        htmlFor={id("extraSpecs")}
        error={errors.extraSpecs?.message}
        aside={lock(key("extraSpecs"))}
        hint={'One per line, as "Name: value" (e.g. "Sight radius: 378 mm").'}
      >
        <textarea id={id("extraSpecs")} rows={3} {...form.register("extraSpecs")} {...invalid("extraSpecs")} className={inputClass} />
      </Field>
    </div>
  );
}

function ChamberingFields({ form }: PartFieldsProps<ChamberingsForm>) {
  const { fields, append, remove } = useFieldArray({ control: form.control, name: "rows" });
  const rows = useWatch({ control: form.control, name: "rows" });
  const primary = useWatch({ control: form.control, name: "primary" });
  const removeRow = (i: number) => {
    // Keep the primary mark on the same cartridge when a row above it goes.
    const p = Number(primary);
    form.setValue("primary", String(p === i ? -1 : p > i ? p - 1 : p), { shouldDirty: true });
    remove(i);
  };

  return (
    <>
      <ul className="space-y-2">
        {fields.map((field, i) => (
          <li key={field.id} className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <div className="w-64">
                <EntityPicker
                  id={`${field.id}-cartridge`}
                  registration={form.register(`rows.${i}.cartridge`)}
                  value={rows?.[i]?.cartridge ?? ""}
                  resource="cartridges"
                  label="Cartridge"
                />
              </div>
              <label className="flex items-center gap-1 text-sm">
                <input type="radio" value={String(i)} {...form.register("primary")} /> primary
              </label>
              <button type="button" className={buttonClass} onClick={() => removeRow(i)}>
                Remove
              </button>
            </div>
            {rowError(form as never, i, "cartridge") && (
              <p role="alert" className="text-sm text-red-700 dark:text-red-400">
                {rowError(form as never, i, "cartridge")}
              </p>
            )}
          </li>
        ))}
      </ul>
      <button type="button" className={buttonClass} onClick={() => append({ cartridge: "" })}>
        Add cartridge
      </button>
    </>
  );
}

/** Make base and delete: lifecycle actions on one variant, each answering with the firearm. */
function VariantActions({ firearm, variantId, isBase, name }: { firearm: Firearm; variantId: number; isBase: boolean; name: string }) {
  const store = useStoreFirearm();
  const [error, setError] = useState<string | null>(null);
  const path = { params: { path: { id: firearm.id, variantId }, query: { version: firearm.version } } };
  const action = useMutation({
    mutationFn: async (kind: "make-base" | "delete") =>
      kind === "make-base"
        ? unwrap(await adminApi.POST("/api/admin/firearms/{id}/variants/{variantId}/make-base", path))
        : unwrap(await adminApi.DELETE("/api/admin/firearms/{id}/variants/{variantId}", path)),
    onMutate: () => setError(null),
    onSuccess: store,
    onError: (e) => setError(problemText(e)),
  });

  if (isBase) {
    return null;
  }

  return (
    <>
      {error && <span role="alert" className="self-center text-sm text-red-700 dark:text-red-400">{error}</span>}
      <button type="button" className={buttonClass} disabled={action.isPending} onClick={() => action.mutate("make-base")}>
        Make base
      </button>
      <ConfirmButton
        label="Delete"
        question={<>Delete the variant <strong>{name}</strong> with its specifications and ammunition?</>}
        confirmLabel="Delete"
        disabled={action.isPending}
        onConfirm={() => action.mutate("delete")}
      />
    </>
  );
}

function VariantCard({ firearm, variant }: { firearm: Firearm; variant: Firearm["variants"][number] }) {
  const variantId = variant.id;
  // One component per variant, made once: a new one each render would remount the fields.
  const Fields = useMemo(() => {
    const VariantFieldsFor = (props: PartFieldsProps<VariantForm>) => <VariantFields {...props} variantId={variantId} />;
    return VariantFieldsFor;
  }, [variantId]);
  return (
    <div className="space-y-3">
      <PartSection
        title={
          <>
            {variant.details.name}
            {variant.isBaseVariant && <span className="ml-2 text-sm font-normal text-muted">base variant</span>}
          </>
        }
        firearm={firearm}
        schema={variantSchema}
        fromFirearm={(f) => variantForm(variantOf(f, variantId)?.details ?? null)}
        write={async (f, v) =>
          unwrap(
            await adminApi.PUT("/api/admin/firearms/{id}/variants/{variantId}", {
              params: { path: { id: f.id, variantId }, query: { version: f.version } },
              body: toVariantInput(v),
            }),
          )
        }
        fields={Fields}
        actions={<VariantActions firearm={firearm} variantId={variantId} isBase={variant.isBaseVariant} name={variant.details.name} />}
        saveLabel="Save variant"
      />
      <div className="ml-4">
        <PartSection
          title={<span className="text-base">Ammunition of {variant.details.name}</span>}
          firearm={firearm}
          lockKey={`chamberings/${variantId}`}
          schema={chamberingsSchema}
          fromFirearm={(f) => chamberingsForm(variantOf(f, variantId)?.chamberings ?? [])}
          write={async (f, v) =>
            unwrap(
              await adminApi.PUT("/api/admin/firearms/{id}/variants/{variantId}/chamberings", {
                params: { path: { id: f.id, variantId }, query: { version: f.version } },
                body: toChamberingsInput(v),
              }),
            )
          }
          fields={ChamberingFields}
          saveLabel="Save ammunition"
        />
        <CitationsSection
          subject="variants"
          id={variantId}
          fields={Object.keys(variant.details)}
          title={`Sources for ${variant.details.name}`}
        />
      </div>
    </div>
  );
}

/** A new variant starts with a name; its details and ammunition are filled in its own sections afterwards. */
function AddVariant({ firearm }: { firearm: Firearm }) {
  const store = useStoreFirearm();
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: async () =>
      unwrap(
        await adminApi.POST("/api/admin/firearms/{id}/variants", {
          params: { path: { id: firearm.id }, query: { version: firearm.version } },
          body: toVariantInput({ ...variantForm(null), name }),
        }),
      ),
    onMutate: () => setError(null),
    onSuccess: (saved) => {
      store(saved);
      setName("");
    },
    onError: (e) => setError(problemText(e)),
  });

  return (
    <form
      className="flex flex-wrap items-end gap-2"
      onSubmit={(e) => {
        e.preventDefault();
        if (name.trim()) {
          add.mutate();
        }
      }}
    >
      <Field label="New variant" htmlFor="new-variant-name">
        <input id="new-variant-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. AKMS" className={inputClass} />
      </Field>
      <button type="submit" className={primaryButtonClass} disabled={add.isPending || !name.trim()}>
        Add variant
      </button>
      {error && <Notice>{error}</Notice>}
    </form>
  );
}

export function VariantSections({ firearm }: { firearm: Firearm }) {
  const variants = [...firearm.variants].sort((a, b) => Number(b.isBaseVariant) - Number(a.isBaseVariant));
  return (
    <section className="space-y-4">
      <h2 className="font-serif text-2xl">Variants</h2>
      {variants.map((v) => (
        <VariantCard key={v.id} firearm={firearm} variant={v} />
      ))}
      <AddVariant firearm={firearm} />
    </section>
  );
}
