import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
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
  const { t } = useTranslation();
  const [label, setLabel] = useState('');
  const [code, setCode] = useState('');
  const [codeTouched, setCodeTouched] = useState(false);
  const [fieldType, setFieldType] = useState<FieldType>('ShortText');
  const [isRequired, setIsRequired] = useState(false);
  const [options, setOptions] = useState<{ value: string; label: string }[]>([{ value: '', label: '' }]);
  const [lookupTargetId, setLookupTargetId] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useErrorMessage();

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
      setError({ key: 'addField.requiredError' });
      return;
    }
    if (fieldType === 'Dropdown' && !options.some((o) => o.value.trim() && o.label.trim())) {
      setError({ key: 'addField.dropdownOptionError' });
      return;
    }
    if (fieldType === 'Lookup' && !lookupTargetId) {
      setError({ key: 'addField.lookupTargetError' });
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
      setError({ err, fallbackKey: 'addField.addFieldError' });
    } finally {
      setSubmitting(false);
    }
  }

  const inputClass = 'w-full border border-line px-2 py-1.5 text-sm focus:border-ink focus:outline-none';

  return (
    <form onSubmit={handleSubmit} className="space-y-3 border border-line bg-white p-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div>
          <label className="mb-1 block text-xs text-ink-muted">{t('addField.fieldName')}</label>
          <input
            className={inputClass}
            value={label}
            onChange={(e) => handleLabelChange(e.target.value)}
            placeholder={t('addField.fieldNamePlaceholder')}
          />
        </div>
        <div>
          <label className="mb-1 block text-xs text-ink-muted">{t('addField.code')}</label>
          <input
            className={`${inputClass} font-mono`}
            value={code}
            onChange={(e) => {
              setCode(e.target.value);
              setCodeTouched(true);
            }}
            placeholder={t('addField.codePlaceholder')}
          />
        </div>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div>
          <label className="mb-1 block text-xs text-ink-muted">{t('addField.type')}</label>
          <select className={inputClass} value={fieldType} onChange={(e) => setFieldType(e.target.value as FieldType)}>
            {FIELD_TYPES.map((type) => (
              <option key={type} value={type}>
                {t(`fieldType.${type}`)}
              </option>
            ))}
          </select>
        </div>
        <label className="flex items-end gap-2 pb-2 text-sm text-ink">
          <input type="checkbox" checked={isRequired} onChange={(e) => setIsRequired(e.target.checked)} />
          {t('addField.required')}
        </label>
      </div>

      {fieldType === 'Dropdown' && (
        <div>
          <label className="mb-1 block text-xs text-ink-muted">{t('addField.options')}</label>
          <div className="space-y-1.5">
            {options.map((opt, i) => (
              <div key={i} className="flex gap-2">
                <input
                  className={inputClass}
                  placeholder={t('addField.optionValuePlaceholder')}
                  value={opt.value}
                  onChange={(e) => updateOption(i, 'value', e.target.value)}
                />
                <input
                  className={inputClass}
                  placeholder={t('addField.optionLabelPlaceholder')}
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
            {t('addField.addOption')}
          </button>
        </div>
      )}

      {fieldType === 'Lookup' && (
        <div>
          <label className="mb-1 block text-xs text-ink-muted">{t('addField.looksUpFrom')}</label>
          <select className={inputClass} value={lookupTargetId} onChange={(e) => setLookupTargetId(e.target.value)}>
            <option value="">{t('addField.selectForm')}</option>
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
        {submitting ? t('addField.adding') : t('addField.addField')}
      </button>
    </form>
  );
}
