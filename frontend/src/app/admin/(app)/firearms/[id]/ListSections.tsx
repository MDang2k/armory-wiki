"use client";

import { useFieldArray, useWatch, type FieldValues, type UseFormReturn } from "react-hook-form";
import { EntityPicker, FuzzyDateInput } from "@/components/admin/fields";
import { buttonClass, compactInputClass, inputClass } from "@/components/admin/ui";
import { adminApi } from "@/lib/admin/client";
import {
  adoptionsForm,
  adoptionsSchema,
  designersForm,
  designersSchema,
  manufacturersForm,
  manufacturersSchema,
  relationsForm,
  relationsSchema,
  tagsForm,
  tagsSchema,
  toAdoptionsInput,
  toDesignersInput,
  toManufacturersInput,
  toRelationsInput,
  toTagsInput,
  type AdoptionsForm,
  type DesignersForm,
  type ManufacturersForm,
  type RelationsForm,
  type TagsForm,
} from "@/lib/admin/firearmParts";
import { unwrap } from "@/lib/admin/problems";
import { DESIGNER_ROLES, RELATION_TYPES } from "@/lib/labels";
import { PartSection, rowError, type Firearm, type PartFieldsProps } from "./PartSection";

/*
 * The lists hanging off a firearm (PLAN.md Phase 7b). Each is replaced whole by its own PUT and locked as a whole,
 * so each is one section: rows, Add, Remove, Save.
 */

const params = (f: Firearm) => ({ params: { path: { id: f.id }, query: { version: f.version } } });

/** A row's fields in a wrapping line, with its Remove button and its field errors underneath. */
function Row({ children, onRemove, errors }: { children: React.ReactNode; onRemove: () => void; errors: (string | undefined)[] }) {
  const shown = errors.filter(Boolean);
  return (
    <li className="space-y-1">
      <div className="flex flex-wrap items-center gap-2">
        {children}
        <button type="button" className={buttonClass} onClick={onRemove}>
          Remove
        </button>
      </div>
      {shown.length > 0 && (
        <p role="alert" className="text-sm text-red-700 dark:text-red-400">
          {shown.join(" ")}
        </p>
      )}
    </li>
  );
}

function AddButton({ label, onAdd }: { label: string; onAdd: () => void }) {
  return (
    <button type="button" className={buttonClass} onClick={onAdd}>
      {label}
    </button>
  );
}

const errorsOf = (form: UseFormReturn<FieldValues>, index: number, fields: string[]) => fields.map((f) => rowError(form, index, f));
const asAny = <F extends FieldValues>(form: UseFormReturn<F>) => form as unknown as UseFormReturn<FieldValues>;

// Tags ---------------------------------------------------------------------------------------------------------

function TagsFields({ form }: PartFieldsProps<TagsForm>) {
  const { fields, append, remove } = useFieldArray({ control: form.control, name: "rows" });
  const rows = useWatch({ control: form.control, name: "rows" });
  return (
    <>
      <ul className="space-y-2">
        {fields.map((field, i) => (
          <Row key={field.id} onRemove={() => remove(i)} errors={errorsOf(asAny(form), i, ["tag"])}>
            <div className="w-64">
              <EntityPicker id={`tag-${i}`} registration={form.register(`rows.${i}.tag`)} value={rows?.[i]?.tag ?? ""} resource="tags" label="Tag" />
            </div>
          </Row>
        ))}
      </ul>
      <AddButton label="Add tag" onAdd={() => append({ tag: "" })} />
    </>
  );
}

export function TagsSection({ firearm }: { firearm: Firearm }) {
  return (
    <PartSection
      title="Tags"
      firearm={firearm}
      lockKey="tags"
      schema={tagsSchema}
      fromFirearm={(f) => tagsForm(f.tags)}
      write={async (f, v) => unwrap(await adminApi.PUT("/api/admin/firearms/{id}/tags", { ...params(f), body: toTagsInput(v) }))}
      fields={TagsFields}
    />
  );
}

// Designers ----------------------------------------------------------------------------------------------------

function DesignersFields({ form }: PartFieldsProps<DesignersForm>) {
  const { fields, append, remove } = useFieldArray({ control: form.control, name: "rows" });
  const rows = useWatch({ control: form.control, name: "rows" });
  return (
    <>
      <ul className="space-y-2">
        {fields.map((field, i) => {
          const kind = rows?.[i]?.kind ?? "person";
          return (
            <Row key={field.id} onRemove={() => remove(i)} errors={errorsOf(asAny(form), i, ["slug"])}>
              <select aria-label="Person or organization" {...form.register(`rows.${i}.kind`)} className={compactInputClass}>
                <option value="person">Person</option>
                <option value="organization">Organization</option>
              </select>
              <div className="w-64">
                <EntityPicker
                  id={`designer-${i}`}
                  registration={form.register(`rows.${i}.slug`)}
                  value={rows?.[i]?.slug ?? ""}
                  resource={kind === "person" ? "people" : "organizations"}
                  label={kind === "person" ? "Person" : "Organization"}
                />
              </div>
              <select aria-label="Role" {...form.register(`rows.${i}.role`)} className={compactInputClass}>
                {Object.entries(DESIGNER_ROLES).map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </Row>
          );
        })}
      </ul>
      <AddButton label="Add designer" onAdd={() => append({ kind: "person", slug: "", role: "LeadDesigner" })} />
    </>
  );
}

export function DesignersSection({ firearm }: { firearm: Firearm }) {
  return (
    <PartSection
      title="Designers"
      firearm={firearm}
      lockKey="designers"
      schema={designersSchema}
      fromFirearm={(f) => designersForm(f.designers)}
      write={async (f, v) => unwrap(await adminApi.PUT("/api/admin/firearms/{id}/designers", { ...params(f), body: toDesignersInput(v) }))}
      fields={DesignersFields}
    />
  );
}

// Manufacturers ------------------------------------------------------------------------------------------------

function ManufacturersFields({ form }: PartFieldsProps<ManufacturersForm>) {
  const { fields, append, remove } = useFieldArray({ control: form.control, name: "rows" });
  const rows = useWatch({ control: form.control, name: "rows" });
  return (
    <>
      <ul className="space-y-2">
        {fields.map((field, i) => (
          <Row key={field.id} onRemove={() => remove(i)} errors={errorsOf(asAny(form), i, ["organization", "from", "to"])}>
            <div className="w-64">
              <EntityPicker
                id={`manufacturer-${i}`}
                registration={form.register(`rows.${i}.organization`)}
                value={rows?.[i]?.organization ?? ""}
                resource="organizations"
                label="Manufacturer"
              />
            </div>
            <span className="text-sm text-muted">from</span>
            <FuzzyDateInput id={`manufacturer-${i}-from`} registration={form.register(`rows.${i}.from`)} value={rows?.[i]?.from ?? ""} />
            <span className="text-sm text-muted">to</span>
            <FuzzyDateInput id={`manufacturer-${i}-to`} registration={form.register(`rows.${i}.to`)} value={rows?.[i]?.to ?? ""} />
            <label className="flex items-center gap-1 text-sm">
              <input type="checkbox" {...form.register(`rows.${i}.isLicensed`)} /> under licence
            </label>
          </Row>
        ))}
      </ul>
      <AddButton label="Add manufacturer" onAdd={() => append({ organization: "", from: "", to: "", isLicensed: false })} />
    </>
  );
}

export function ManufacturersSection({ firearm }: { firearm: Firearm }) {
  return (
    <PartSection
      title="Manufacturers"
      firearm={firearm}
      lockKey="manufacturers"
      schema={manufacturersSchema}
      fromFirearm={(f) => manufacturersForm(f.manufacturers)}
      write={async (f, v) =>
        unwrap(await adminApi.PUT("/api/admin/firearms/{id}/manufacturers", { ...params(f), body: toManufacturersInput(v) }))
      }
      fields={ManufacturersFields}
    />
  );
}

// Adoptions ----------------------------------------------------------------------------------------------------

function AdoptionsFields({ form }: PartFieldsProps<AdoptionsForm>) {
  const { fields, append, remove } = useFieldArray({ control: form.control, name: "rows" });
  const rows = useWatch({ control: form.control, name: "rows" });
  return (
    <>
      <ul className="space-y-2">
        {fields.map((field, i) => (
          <Row key={field.id} onRemove={() => remove(i)} errors={errorsOf(asAny(form), i, ["country", "from", "to"])}>
            <div className="w-56">
              <EntityPicker id={`adoption-${i}`} registration={form.register(`rows.${i}.country`)} value={rows?.[i]?.country ?? ""} resource="countries" label="Country" />
            </div>
            <span className="text-sm text-muted">from</span>
            <FuzzyDateInput id={`adoption-${i}-from`} registration={form.register(`rows.${i}.from`)} value={rows?.[i]?.from ?? ""} />
            <span className="text-sm text-muted">to</span>
            <FuzzyDateInput id={`adoption-${i}-to`} registration={form.register(`rows.${i}.to`)} value={rows?.[i]?.to ?? ""} />
            <input aria-label="Note" placeholder="Note" {...form.register(`rows.${i}.note`)} className={`${inputClass} min-w-48 flex-1`} />
          </Row>
        ))}
      </ul>
      <AddButton label="Add adoption" onAdd={() => append({ country: "", from: "", to: "", note: "" })} />
    </>
  );
}

export function AdoptionsSection({ firearm }: { firearm: Firearm }) {
  return (
    <PartSection
      title="Adoptions"
      firearm={firearm}
      lockKey="adoptions"
      schema={adoptionsSchema}
      fromFirearm={(f) => adoptionsForm(f.adoptions)}
      write={async (f, v) => unwrap(await adminApi.PUT("/api/admin/firearms/{id}/adoptions", { ...params(f), body: toAdoptionsInput(v) }))}
      fields={AdoptionsFields}
    />
  );
}

// Relations ----------------------------------------------------------------------------------------------------

function RelationsFields({ form }: PartFieldsProps<RelationsForm>) {
  const { fields, append, remove } = useFieldArray({ control: form.control, name: "rows" });
  const rows = useWatch({ control: form.control, name: "rows" });
  return (
    <>
      <p className="text-sm text-muted">Relations stored on this firearm, read as “this firearm … the other one”.</p>
      <ul className="space-y-2">
        {fields.map((field, i) => (
          <Row key={field.id} onRemove={() => remove(i)} errors={errorsOf(asAny(form), i, ["firearm"])}>
            <select aria-label="Relation" {...form.register(`rows.${i}.type`)} className={compactInputClass}>
              {Object.entries(RELATION_TYPES).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
            <div className="w-64">
              <EntityPicker id={`relation-${i}`} registration={form.register(`rows.${i}.firearm`)} value={rows?.[i]?.firearm ?? ""} resource="firearms" label="Other firearm" />
            </div>
            <input aria-label="Note" placeholder="Note" {...form.register(`rows.${i}.note`)} className={`${inputClass} min-w-48 flex-1`} />
          </Row>
        ))}
      </ul>
      <AddButton label="Add relation" onAdd={() => append({ type: "DerivedFrom", firearm: "", note: "" })} />
    </>
  );
}

export function RelationsSection({ firearm }: { firearm: Firearm }) {
  return (
    <PartSection
      title="Relations"
      firearm={firearm}
      lockKey="relations"
      schema={relationsSchema}
      fromFirearm={(f) => relationsForm(f.relations)}
      write={async (f, v) => unwrap(await adminApi.PUT("/api/admin/firearms/{id}/relations", { ...params(f), body: toRelationsInput(v) }))}
      fields={RelationsFields}
    />
  );
}
