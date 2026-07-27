import { useEffect, useMemo, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { DropdownOption, FieldDefinitionDto, FormDefinitionDto } from '../types';

interface FormRendererProps {
  token: string;
  formDefinition: FormDefinitionDto;
  onSubmitted?: () => void;
}

interface LookupChoice {
  id: string;
  label: string;
}

/**
 * Renders a submission form for ANY published form, driven entirely by its field
 * metadata - this is the actual "low-code" part of the platform. No per-form code exists
 * or should ever need to exist here.
 *
 * Known simplification: Lookup fields need a human-readable label for their dropdown,
 * but FieldDefinitionDto has no designated "display field" for the target form yet - the
 * backend doesn't expose one. This convention-guesses the first ShortText field on the
 * target form's published version. Fine for now; formalizing a real DisplayFieldCode on
 * FormDefinition (backend change) would remove the guesswork.
 */
export function FormRenderer({ token, formDefinition, onSubmitted }: FormRendererProps) {
  const activeFields = useMemo(
    () => formDefinition.publishedVersion?.fields.filter((f) => f.isActive) ?? [],
    [formDefinition],
  );

  const [values, setValues] = useState<Record<string, string>>({});
  const [lookupChoices, setLookupChoices] = useState<Record<string, LookupChoice[]>>({});
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    setValues({});
    setError(null);
    setFieldErrors({});

    const lookupFields = activeFields.filter((f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId);
    const uniqueTargets = [...new Set(lookupFields.map((f) => f.lookupFormDefinitionId as string))];

    uniqueTargets.forEach(async (targetFormId) => {
      try {
        const targetDef = await api.forms.get(token, targetFormId);
        const displayField = targetDef.publishedVersion?.fields.find(
          (f) => f.isActive && f.fieldType === 'ShortText',
        );
        const submissions = await api.submissions.list(token, targetFormId, 1, 200);
        const choices: LookupChoice[] = submissions.items.map((row) => ({
          id: row.id,
          label: displayField ? String(row.values[displayField.code] ?? row.id) : row.id,
        }));

        setLookupChoices((prev) => {
          const next = { ...prev };
          lookupFields
            .filter((f) => f.lookupFormDefinitionId === targetFormId)
            .forEach((f) => {
              next[f.code] = choices;
            });
          return next;
        });
      } catch {
        // A failed lookup fetch shouldn't block the rest of the form from rendering -
        // that field just won't have options, and the required-field check below will
        // catch it if the user tries to submit without picking one.
      }
    });
    // activeFields is derived from formDefinition each render via useMemo, not a stable
    // reference - depend on the form's id/version instead to avoid re-fetching on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formDefinition.id, formDefinition.publishedVersion?.id, token]);

  function setValue(code: string, value: string) {
    setValues((prev) => ({ ...prev, [code]: value }));
  }

  function parseOptions(optionsJson: string | null): DropdownOption[] {
    if (!optionsJson) return [];
    try {
      return JSON.parse(optionsJson) as DropdownOption[];
    } catch {
      return [];
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const missing: Record<string, string> = {};
    activeFields.forEach((f) => {
      if (f.isRequired && f.fieldType !== 'Attachment' && !values[f.code]) {
        missing[f.code] = 'Required';
      }
    });
    if (Object.keys(missing).length > 0) {
      setFieldErrors(missing);
      return;
    }
    setFieldErrors({});

    setSubmitting(true);
    try {
      const payload: Record<string, unknown> = {};
      activeFields.forEach((f) => {
        if (f.fieldType === 'Attachment') return;
        const raw = values[f.code];
        if (raw === undefined || raw === '') {
          payload[f.code] = null;
        } else if (f.fieldType === 'Number' || f.fieldType === 'Decimal') {
          payload[f.code] = Number(raw);
        } else if (f.fieldType === 'Boolean') {
          payload[f.code] = raw === 'true';
        } else {
          payload[f.code] = raw;
        }
      });

      await api.submissions.submit(token, formDefinition.id, payload);
      setValues({});
      onSubmitted?.();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Submission failed.');
    } finally {
      setSubmitting(false);
    }
  }

  if (!formDefinition.publishedVersion) {
    return <p className="text-sm text-gray-500">This form has no published version yet.</p>;
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {activeFields.map((field) => (
        <FieldInput
          key={field.id}
          field={field}
          value={values[field.code] ?? ''}
          onChange={(v) => setValue(field.code, v)}
          error={fieldErrors[field.code]}
          options={parseOptions(field.optionsJson)}
          lookupChoices={lookupChoices[field.code]}
        />
      ))}

      {error && <p className="text-sm text-red-600">{error}</p>}

      <button
        type="submit"
        disabled={submitting}
        className="w-full rounded-md bg-gray-900 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
      >
        {submitting ? 'Submitting…' : 'Submit'}
      </button>
    </form>
  );
}

function FieldInput({
  field,
  value,
  onChange,
  error,
  options,
  lookupChoices,
}: {
  field: FieldDefinitionDto;
  value: string;
  onChange: (v: string) => void;
  error?: string;
  options: DropdownOption[];
  lookupChoices?: LookupChoice[];
}) {
  const baseClass =
    'w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-gray-500 focus:outline-none';

  return (
    <div>
      <label className="mb-1 block text-sm font-medium text-gray-700">
        {field.label}
        {field.isRequired && <span className="text-red-500"> *</span>}
      </label>

      {field.fieldType === 'Attachment' ? (
        <p className="text-sm italic text-gray-400">File upload isn't wired up yet - no File Management module.</p>
      ) : field.fieldType === 'LongText' ? (
        <textarea className={baseClass} rows={3} value={value} onChange={(e) => onChange(e.target.value)} />
      ) : field.fieldType === 'Boolean' ? (
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">Select…</option>
          <option value="true">Yes</option>
          <option value="false">No</option>
        </select>
      ) : field.fieldType === 'Dropdown' ? (
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">Select…</option>
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      ) : field.fieldType === 'Lookup' ? (
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">{lookupChoices ? 'Select…' : 'Loading…'}</option>
          {(lookupChoices ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.label}
            </option>
          ))}
        </select>
      ) : field.fieldType === 'DateTime' ? (
        <input type="date" className={baseClass} value={value} onChange={(e) => onChange(e.target.value)} />
      ) : field.fieldType === 'Number' || field.fieldType === 'Decimal' ? (
        <input
          type="number"
          step={field.fieldType === 'Decimal' ? '0.01' : '1'}
          className={baseClass}
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
      ) : (
        <input type="text" className={baseClass} value={value} onChange={(e) => onChange(e.target.value)} />
      )}

      {error && <p className="mt-1 text-xs text-red-600">{error}</p>}
    </div>
  );
}
