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
  /** Existing fields a new Lookup field could filter its candidates by (see
   *  FieldDefinition.FilterByFieldCode) - any type is allowed here, since the common case is
   *  filtering by another Lookup (e.g. "zone" filtered by "project"). */
  filterableFields: FieldDefinitionDto[];
  onAdded: () => void;
  /** The form's steps (redesign §9.2) - the new field goes into the chosen one. */
  steps?: { id: string; title: string }[];
  defaultStepId?: string | null;
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

export function AddFieldForm({
  token,
  formId,
  lookupTargets,
  controllableFields,
  filterableFields,
  onAdded,
  steps = [],
  defaultStepId = null,
}: AddFieldFormProps) {
  const { t } = useTranslation();
  const [label, setLabel] = useState('');
  const [labelEn, setLabelEn] = useState('');
  const [stepId, setStepId] = useState<string>(defaultStepId ?? steps[0]?.id ?? '');
  const [code, setCode] = useState('');
  const [codeTouched, setCodeTouched] = useState(false);
  const [fieldType, setFieldType] = useState<FieldType>('ShortText');
  const [isRequired, setIsRequired] = useState(false);
  const [options, setOptions] = useState<{ value: string; label: string; labelEn?: string }[]>([{ value: '', label: '' }]);
  const [lookupTargetId, setLookupTargetId] = useState('');
  // Filtered/cascading Lookup ("only show candidates where..."), set at creation time.
  // Empty string = no filter, which is the default/original behaviour.
  const [filterByFieldCode, setFilterByFieldCode] = useState('');
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

  function updateOption(index: number, key: 'value' | 'label' | 'labelEn', value: string) {
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
            ? JSON.stringify(
                options
                  .filter((o) => o.value.trim() && o.label.trim())
                  .map((o) => (o.labelEn?.trim() ? { value: o.value, label: o.label, labelEn: o.labelEn.trim() } : { value: o.value, label: o.label })),
              )
            : null,
        lookupFormDefinitionId: fieldType === 'Lookup' ? lookupTargetId : null,
        visibleWhenFieldCode: hasCondition ? conditionFieldCode : null,
        visibleWhenValuesJson: hasCondition ? JSON.stringify([...conditionValues]) : null,
        filterByFieldCode: fieldType === 'Lookup' && filterByFieldCode ? filterByFieldCode : null,
        labelEn: labelEn.trim() || null,
        stepId: stepId || null,
      });
      setLabel('');
      setLabelEn('');
      setCode('');
      setCodeTouched(false);
      setFieldType('ShortText');
      setIsRequired(false);
      setOptions([{ value: '', label: '' }]);
      setLookupTargetId('');
      setFilterByFieldCode('');
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

  const inputClass = 'h-[44px] w-full rounded-ui-md border border-border bg-panel px-3 text-[15px] text-ink focus:border-accent focus:shadow-focus-ring focus:outline-none';

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
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
          <label className="mb-1 block text-xs text-ink-soft">{t('builderV2.nameEn')}</label>
          <input className={inputClass} dir="ltr" value={labelEn} onChange={(e) => setLabelEn(e.target.value)} />
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
        {steps.length > 0 && (
          <div>
            <label className="mb-1 block text-xs text-ink-soft">{t('builderV2.moveToStep')}</label>
            <select className={inputClass} value={stepId} onChange={(e) => setStepId(e.target.value)}>
              {steps.map((st, i) => (
                <option key={st.id} value={st.id}>
                  {t('builderV2.stepHeading', { n: i + 1, title: st.title })}
                </option>
              ))}
            </select>
          </div>
        )}
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
                <input
                  className={inputClass}
                  dir="ltr"
                  placeholder={t('builderV2.optionLabelEn')}
                  aria-label={t('builderV2.optionLabelEn')}
                  value={opt.labelEn ?? ''}
                  onChange={(e) => updateOption(i, 'labelEn', e.target.value)}
                />
              </div>
            ))}
          </div>
          <button
            type="button"
            onClick={() => setOptions((prev) => [...prev, { value: '', label: '' }])}
            className="mt-2 text-label text-ink-soft hover:text-ink"
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

          {filterableFields.length > 0 && (
            <div className="mt-2">
              <label className="mb-1 block text-xs text-ink-soft">{t('addField.filterByField')}</label>
              <select
                className={inputClass}
                value={filterByFieldCode}
                onChange={(e) => setFilterByFieldCode(e.target.value)}
              >
                <option value="">{t('addField.noFilter')}</option>
                {filterableFields.map((f) => (
                  <option key={f.code} value={f.code}>
                    {f.label}
                  </option>
                ))}
              </select>
              <p className="mt-1 text-xs text-ink-soft">{t('addField.filterByFieldHint')}</p>
            </div>
          )}
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
        className="h-[42px] rounded-ui-lg bg-accent px-4 text-[15px] font-bold text-accent-ink hover:brightness-110 disabled:opacity-50"
      >
        {submitting ? t('addField.adding') : t('addField.addField')}
      </button>
    </form>
  );
}
