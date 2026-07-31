import { useState } from 'react';
import { api, ApiError } from '../api/client';
import type { FieldType, FormSummaryDto } from '../types';

interface AddFieldFormProps {
  token: string;
  formId: string;
  lookupTargets: FormSummaryDto[];
  onAdded: () => void;
}

const FIELD_TYPES: FieldType[] = [
  'ShortText', 'LongText', 'Number', 'Decimal', 'Boolean', 'DateTime', 'Dropdown', 'Lookup', 'Attachment',
];

function slugify(input: string): string {
  return input
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

export function AddFieldForm({ token, formId, lookupTargets, onAdded }: AddFieldFormProps) {
  const [label, setLabel] = useState('');
  const [code, setCode] = useState('');
  const [codeTouched, setCodeTouched] = useState(false);
  const [fieldType, setFieldType] = useState<FieldType>('ShortText');
  const [isRequired, setIsRequired] = useState(false);
  const [options, setOptions] = useState<{ value: string; label: string }[]>([{ value: '', label: '' }]);
  const [lookupTargetId, setLookupTargetId] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function handleLabelChange(value: string) {
    setLabel(value);
    if (!codeTouched) setCode(slugify(value));
  }

  function updateOption(index: number, key: 'value' | 'label', value: string) {
    setOptions((prev) => prev.map((o, i) => (i === index ? { ...o, [key]: value } : o)));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!label.trim() || !code.trim()) {
      setError('Field name and code are both required.');
      return;
    }
    if (fieldType === 'Dropdown' && !options.some((o) => o.value.trim() && o.label.trim())) {
      setError('Add at least one option for a dropdown field.');
      return;
    }
    if (fieldType === 'Lookup' && !lookupTargetId) {
      setError('Pick which form this field looks up.');
      return;
    }

    setSubmitting(true);
    try {
      await api.forms.addField(token, formId, {
        code,
        label,
        fieldType,
        isRequired,
        optionsJson:
          fieldType === 'Dropdown'
            ? JSON.stringify(options.filter((o) => o.value.trim() && o.label.trim()))
            : null,
        lookupFormDefinitionId: fieldType === 'Lookup' ? lookupTargetId : null,
      });
      setLabel('');
      setCode('');
      setCodeTouched(false);
      setFieldType('ShortText');
      setIsRequired(false);
      setOptions([{ value: '', label: '' }]);
      setLookupTargetId('');
      onAdded();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not add that field.');
    } finally {
      setSubmitting(false);
    }
  }

  const inputClass = 'w-full border border-line px-2 py-1.5 text-sm focus:border-ink focus:outline-none';

  return (
    <form onSubmit={handleSubmit} className="space-y-3 border border-line bg-white p-4">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="mb-1 block text-xs text-ink-muted">Field name</label>
          <input className={inputClass} value={label} onChange={(e) => handleLabelChange(e.target.value)} placeholder="Quantity received" />
        </div>
        <div>
          <label className="mb-1 block text-xs text-ink-muted">Code</label>
          <input
            className={`${inputClass} font-mono`}
            value={code}
            onChange={(e) => {
              setCode(e.target.value);
              setCodeTouched(true);
            }}
            placeholder="quantity_received"
          />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="mb-1 block text-xs text-ink-muted">Type</label>
          <select className={inputClass} value={fieldType} onChange={(e) => setFieldType(e.target.value as FieldType)}>
            {FIELD_TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-end gap-2 pb-2 text-sm text-ink">
          <input type="checkbox" checked={isRequired} onChange={(e) => setIsRequired(e.target.checked)} />
          Required
        </label>
      </div>

      {fieldType === 'Dropdown' && (
        <div>
          <label className="mb-1 block text-xs text-ink-muted">Options</label>
          <div className="space-y-1.5">
            {options.map((opt, i) => (
              <div key={i} className="flex gap-2">
                <input
                  className={inputClass}
                  placeholder="Stored value (e.g. good)"
                  value={opt.value}
                  onChange={(e) => updateOption(i, 'value', e.target.value)}
                />
                <input
                  className={inputClass}
                  placeholder="Shown label (e.g. Good)"
                  value={opt.label}
                  onChange={(e) => updateOption(i, 'label', e.target.value)}
                />
              </div>
            ))}
          </div>
          <button
            type="button"
            onClick={() => setOptions((prev) => [...prev, { value: '', label: '' }])}
            className="mt-2 text-[11px] uppercase tracking-wide text-ink-muted hover:text-ink"
          >
            + Add option
          </button>
        </div>
      )}

      {fieldType === 'Lookup' && (
        <div>
          <label className="mb-1 block text-xs text-ink-muted">Looks up records from</label>
          <select className={inputClass} value={lookupTargetId} onChange={(e) => setLookupTargetId(e.target.value)}>
            <option value="">Select a form…</option>
            {lookupTargets.map((f) => (
              <option key={f.id} value={f.id}>
                {f.name} ({f.moduleName})
              </option>
            ))}
          </select>
        </div>
      )}

      {error && <p className="text-sm text-clay">{error}</p>}

      <button
        type="submit"
        disabled={submitting}
        className="bg-ink px-3 py-1.5 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
      >
        {submitting ? 'Adding…' : 'Add field'}
      </button>
    </form>
  );
}
