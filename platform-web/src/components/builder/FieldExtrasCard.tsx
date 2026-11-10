import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { Button } from '../ui';
import type { FieldDefinitionDto } from '../../types';
import { builderInput, SettingsCard } from './FieldSettings';

/**
 * The field settings added with stage progress (2026-11-10), one card: a number field's
 * formula (calculated, read-only), a lookup's row filter, which attribute of the controlling
 * field's choice decides visibility, and a choice field's options provider.
 */
export function FieldExtrasCard({
  token,
  formId,
  field,
  busy,
  run,
}: {
  token: string;
  formId: string;
  field: FieldDefinitionDto;
  busy: boolean;
  run: (action: () => Promise<void>, fallbackKey: string) => Promise<void> | void;
}) {
  const { t } = useTranslation();
  const isNumber = field.fieldType === 'Number' || field.fieldType === 'Decimal';
  const isLookup = field.fieldType === 'Lookup' || field.fieldType === 'MultiLookup';
  const isChoice = isLookup || field.fieldType === 'Dropdown' || field.fieldType === 'MultiSelect';
  const parsedFilter = (() => {
    try {
      const f = field.rowFilterJson ? (JSON.parse(field.rowFilterJson) as { field?: string; values?: string[] }) : null;
      return { field: f?.field ?? '', values: (f?.values ?? []).join(', ') };
    } catch {
      return { field: '', values: '' };
    }
  })();
  const [formula, setFormula] = useState(field.formula ?? '');
  const [filterField, setFilterField] = useState(parsedFilter.field);
  const [filterValues, setFilterValues] = useState(parsedFilter.values);
  const [attribute, setAttribute] = useState(field.visibleWhenAttribute ?? '');
  const [provider, setProvider] = useState(field.optionsProvider ?? '');
  const [providers, setProviders] = useState<{ key: string; description: string }[]>([]);

  useEffect(() => {
    if (isChoice) api.forms.optionsProviders(token).then(setProviders).catch(() => setProviders([]));
  }, [token, isChoice]);

  if (!isNumber && !isChoice && !field.visibleWhenFieldCode) return null;

  const save = () => {
    const values = filterValues.split(',').map((v) => v.trim()).filter(Boolean);
    return run(
      () =>
        api.forms.updateFieldSettings(token, formId, field.id, {
          formula: isNumber ? formula.trim() || null : null,
          rowFilterJson: isLookup && filterField.trim() && values.length > 0 ? JSON.stringify({ field: filterField.trim(), values }) : null,
          visibleWhenAttribute: field.visibleWhenFieldCode ? attribute.trim() || null : null,
          optionsProvider: isChoice ? provider || null : null,
        }),
      'fieldExtras.saveError',
    );
  };

  return (
    <SettingsCard title={t('fieldExtras.title')}>
      {isNumber && (
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          {t('fieldExtras.formula')}
          <input dir="ltr" className={builderInput} value={formula} placeholder="plot_area_m2 * buildable_pct / 100" onChange={(e) => setFormula(e.target.value)} />
          <span className="text-[13px] font-normal text-ink-soft">{t('fieldExtras.formulaHint')}</span>
        </label>
      )}
      {isLookup && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('fieldExtras.rowFilterField')}
            <input dir="ltr" className={builderInput} value={filterField} placeholder="status" onChange={(e) => setFilterField(e.target.value)} />
          </label>
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('fieldExtras.rowFilterValues')}
            <input dir="ltr" className={builderInput} value={filterValues} placeholder="planning, active" onChange={(e) => setFilterValues(e.target.value)} />
          </label>
        </div>
      )}
      {field.visibleWhenFieldCode && (
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          {t('fieldExtras.attribute')}
          <input dir="ltr" className={builderInput} value={attribute} placeholder="unit_pick" onChange={(e) => setAttribute(e.target.value)} />
          <span className="text-[13px] font-normal text-ink-soft">{t('fieldExtras.attributeHint')}</span>
        </label>
      )}
      {isChoice && (
        <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
          {t('fieldExtras.provider')}
          <select className={builderInput} value={provider} onChange={(e) => setProvider(e.target.value)}>
            <option value="">{t('fieldExtras.noProvider')}</option>
            {providers.map((p) => (
              <option key={p.key} value={p.key}>
                {p.key} - {p.description}
              </option>
            ))}
          </select>
        </label>
      )}
      <div>
        <Button size="desktop" disabled={busy} onClick={() => void save()}>
          {t('common.save')}
        </Button>
      </div>
    </SettingsCard>
  );
}
