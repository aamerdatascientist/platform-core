import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FieldDefinitionDto, FieldType, FormSummaryDto } from '../types';

interface AddFieldFormProps {
  token: string;
  formId: string;
  lookupTargets: FormSummaryDto[];
  /** Existing fields this new one could branch on - Dropdown/Boolean only (the only types
   *  with a fixed, enumerable set of values a condition can match against). */
  controllableFields: FieldDefinitionDto[];
  onAdded: () => void;
}

function parseFieldOptions(
  field: FieldDefinitionDto,
  t: (key: string) => string,
): { value: string; label: string }[] {
  if (field.fieldType === 'Boolean') {
    return [
      { value: 'true', label: t('common.yes') },
      { value: 'false', label: t('common.no') },
    ];
  }
  try {
    return field.optionsJson ? (JSON.parse(field.optionsJson) as { value: string; label: string }[]) : [];
  } catch {
    return [];
  }
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

export function AddFieldForm({ token, formId, lookupTargets, controllableFields, onAdded }: AddFieldFormProps) {
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

  // Conditional visibility ("only show this field when..."), set at creation time.
  const [hasCondition, setHasCondition] = useState(false);
  const [conditionFieldCode, setConditionFieldCode] = useState('');
  const [conditionValues, setConditionValues] = useState<Set<string>>(new Set());

  function toggleConditionValue(value: string) {
    setConditionValues((prev) => {
      const next = new Set(prev);
      if (next.has(value)) next.delete(value);
      else next.add(value);
      return next;
    });
  }

  const conditionField = controllableFields.find((f) => f.code === conditionFieldCode);
  const conditionFieldOptions = conditionField ? parseFieldOptions(conditionField, t) : [];

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
    if (hasCondition && (!conditionFieldCode || conditionValues.size === 0)) {
      setError({ key: 'addField.conditionError' });
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
        visibleWhenFieldCode: hasCondition ? conditionFieldCode : null,
        visibleWhenValuesJson: hasCondition ? JSON.stringify([...conditionValues]) : null,
      });
      setLabel('');
      setCode('');
      setCodeTouched(false);
      setFieldType('ShortText');
      setIsRequired(false);
      setOptions([{ value: '', label: '' }]);
      setLookupTargetId('');
      setHasCondition(false);
      setConditionFieldCode('');
      setConditionValues(new Set());
      onAdded();
    } catch (err) {
      setError({ err, fallbackKey: 'addField.addFieldError' });
    } finally {
      setSubmitting(false);
    }
  }

  const inputClass = 'w-full border border-border rounded bg-bg px-2 py-1.5 text-sm focus:border-accent focus:outline-none';

  return (
    <form onSubmit={handleSubmit} className="space-y-3 border border-border rounded bg-panel p-4 shadow-recessed">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div>
          <label className="mb-1 block text-xs text-ink-soft">{t('addField.fieldName')}</label>
          <input
            className={inputClass}
            value={label}
            onChange={(e) => handleLabelChange(e.target.value)}
            placeholder={t('addField.fieldNamePlaceholder')}
          />
        </div>
        <div>
          <label className="mb-1 block text-xs text-ink-soft">{t('addField.code')}</label>
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
          <label className="mb-1 block text-xs text-ink-soft">{t('addField.type')}</label>
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
          <label className="mb-1 block text-xs text-ink-soft">{t('addField.options')}</label>
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
            className="mt-2 text-[11px] uppercase tracking-wide text-ink-soft hover:text-ink"
          >
            {t('addField.addOption')}
          </button>
        </div>
      )}

      {fieldType === 'Lookup' && (
        <div>
          <label className="mb-1 block text-xs text-ink-soft">{t('addField.looksUpFrom')}</label>
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

      {controllableFields.length > 0 && (
        <div className="border-t border-border pt-3">
          <label className="flex items-center gap-2 text-sm text-ink">
            <input
              type="checkbox"
              checked={hasCondition}
              onChange={(e) => {
                setHasCondition(e.target.checked);
                if (!e.target.checked) {
                  setConditionFieldCode('');
                  setConditionValues(new Set());
                }
              }}
            />
            {t('addField.onlyShowWhen')}
          </label>

          {hasCondition && (
            <div className="mt-2 space-y-2 ps-6">
              <select
                className={inputClass}
                value={conditionFieldCode}
                onChange={(e) => {
                  setConditionFieldCode(e.target.value);
                  setConditionValues(new Set());
                }}
              >
                <option value="">{t('addField.chooseConditionField')}</option>
                {controllableFields.map((f) => (
                  <option key={f.code} value={f.code}>
                    {f.label}
                  </option>
                ))}
              </select>

              {conditionField && (
                <div>
                  <p className="mb-1 text-xs text-ink-soft">{t('addField.conditionValuesHint')}</p>
                  <div className="flex flex-wrap gap-3">
                    {conditionFieldOptions.map((o) => (
                      <label key={o.value} className="flex items-center gap-1.5 text-sm text-ink">
                        <input
                          type="checkbox"
                          checked={conditionValues.has(o.value)}
                          onChange={() => toggleConditionValue(o.value)}
                        />
                        {o.label}
                      </label>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {error && <p className="text-sm text-danger">{error}</p>}

      <button
        type="submit"
        disabled={submitting}
        className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-50"
      >
        {submitting ? t('addField.adding') : t('addField.addField')}
      </button>
    </form>
  );
}
