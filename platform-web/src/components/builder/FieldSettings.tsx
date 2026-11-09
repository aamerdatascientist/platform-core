import { useEffect, useId, useState } from 'react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { useErrorMessage } from '../../hooks/useErrorMessage';
import { parseAllowedValues, parseOptions, parseRules } from '../../forms/fieldValues';
import { useLocalized } from '../../forms/localized';
import type { FieldDefinitionDto, FieldType, FormSummaryDto } from '../../types';
import { Button, Pill, Switch } from '../ui';

const FIELD_TYPES: FieldType[] = ['ShortText', 'LongText', 'Number', 'Decimal', 'Boolean', 'DateTime', 'Dropdown', 'Lookup', 'Attachment'];

/** 44px builder input (21): 1px --border, radius 10, 15px. */
export const builderInput =
  'h-[44px] w-full min-w-0 rounded-ui-md border border-border bg-panel px-3 text-[15px] text-ink focus:border-accent focus:shadow-focus-ring focus:outline-none disabled:opacity-60';
const smallButton = 'h-[34px] rounded-[8px] border border-border bg-panel px-[10px] text-[13px] text-ink hover:border-ink-soft disabled:opacity-50';

export function SettingsCard({ title, children, aside }: { title?: ReactNode; children: ReactNode; aside?: ReactNode }) {
  return (
    <div className="flex flex-col gap-3 rounded-ui-2xl border border-border bg-panel p-[18px]">
      {title && (
        <div className="flex items-center justify-between gap-3">
          <h3 className="text-[16px] font-extrabold text-ink">{title}</h3>
          {aside}
        </div>
      )}
      {children}
    </div>
  );
}

/** The values a condition can match: Yes/No for a yes/no field, the options of a dropdown. */
function controllerValues(field: FieldDefinitionDto, t: (k: string) => string, optionLabel: (o: { value: string; label: string; labelEn?: string | null }) => string) {
  if (field.fieldType === 'Boolean')
    return [
      { value: 'true', label: t('common.yes') },
      { value: 'false', label: t('common.no') },
    ];
  return parseOptions(field.optionsJson).map((o) => ({ value: o.value, label: optionLabel(o) }));
}

type Risky = 'none' | 'code' | 'type' | 'archive' | 'delete';

/**
 * Pane 2 of the builder (screen 21): the selected field's settings as stacked cards. Safe edits
 * (names, condition, filter, option labels, rules) save on their own; the risky ones (rename
 * code, change type, archive, delete) keep their existing confirmations word for word, each
 * stating its real consequence - and Delete still needs the field's code typed on a live form.
 */
export function FieldSettings({
  token,
  formId,
  field,
  isLive,
  stepLabel,
  lookupTargets,
  controllableFields,
  filterableFields,
  onChanged,
}: {
  token: string;
  formId: string;
  field: FieldDefinitionDto;
  isLive: boolean;
  /** "Step 2" - for the meta line. */
  stepLabel: string | null;
  lookupTargets: FormSummaryDto[];
  controllableFields: FieldDefinitionDto[];
  filterableFields: FieldDefinitionDto[];
  onChanged: () => Promise<void> | void;
}) {
  const { t } = useTranslation();
  const { fieldLabel, optionLabel } = useLocalized();
  const [error, setError] = useErrorMessage();
  const [busy, setBusy] = useState(false);
  const [savedNote, setSavedNote] = useState<string | null>(null);
  const [risky, setRisky] = useState<Risky>('none');
  const ids = { ar: useId(), en: useId(), req: useId() };

  // Drafts of every editable value, reset whenever a different field is selected or reloaded.
  const [labelAr, setLabelAr] = useState(field.label);
  const [labelEn, setLabelEn] = useState(field.labelEn ?? '');
  const [codeDraft, setCodeDraft] = useState(field.code);
  const [typeDraft, setTypeDraft] = useState<FieldType>(field.fieldType);
  const [lookupTargetDraft, setLookupTargetDraft] = useState(field.lookupFormDefinitionId ?? '');
  const [deleteConfirm, setDeleteConfirm] = useState('');
  const [conditionField, setConditionField] = useState(field.visibleWhenFieldCode ?? '');
  const [conditionValues, setConditionValues] = useState<string[]>(() => parseAllowedValues(field.visibleWhenValuesJson));
  const [filterBy, setFilterBy] = useState(field.filterByFieldCode ?? '');
  const [options, setOptions] = useState(() => parseOptions(field.optionsJson).map((o) => ({ value: o.value, label: o.label, labelEn: o.labelEn ?? '' })));
  const rules0 = parseRules(field.validationRulesJson);
  const [required, setRequired] = useState(field.isRequired);
  const [min, setMin] = useState(rules0.min?.toString() ?? '');
  const [max, setMax] = useState(rules0.max?.toString() ?? '');
  const [minExclusive, setMinExclusive] = useState(rules0.minExclusive);
  const [regex, setRegex] = useState(rules0.regex ?? '');
  const [noFuture, setNoFuture] = useState(rules0.noFutureDates);
  const [unique, setUnique] = useState(rules0.unique);
  const [uniqueWithin, setUniqueWithin] = useState(rules0.uniqueWithin ?? '');

  useEffect(() => {
    const r = parseRules(field.validationRulesJson);
    setLabelAr(field.label);
    setLabelEn(field.labelEn ?? '');
    setCodeDraft(field.code);
    setTypeDraft(field.fieldType);
    setLookupTargetDraft(field.lookupFormDefinitionId ?? '');
    setDeleteConfirm('');
    setConditionField(field.visibleWhenFieldCode ?? '');
    setConditionValues(parseAllowedValues(field.visibleWhenValuesJson));
    setFilterBy(field.filterByFieldCode ?? '');
    setOptions(parseOptions(field.optionsJson).map((o) => ({ value: o.value, label: o.label, labelEn: o.labelEn ?? '' })));
    setRequired(field.isRequired);
    setMin(r.min?.toString() ?? '');
    setMax(r.max?.toString() ?? '');
    setMinExclusive(r.minExclusive);
    setRegex(r.regex ?? '');
    setNoFuture(r.noFutureDates);
    setUnique(r.unique);
    setUniqueWithin(r.uniqueWithin ?? '');
    setRisky('none');
    setError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [field]);

  async function run(action: () => Promise<void>, fallbackKey: string, note = true) {
    setBusy(true);
    setError(null);
    try {
      await action();
      await onChanged();
      setRisky('none');
      if (note) {
        setSavedNote(t('builderV2.saved'));
        window.setTimeout(() => setSavedNote(null), 2000);
      }
    } catch (err) {
      setError({ err, fallbackKey });
    } finally {
      setBusy(false);
    }
  }

  const archived = !field.isActive;
  const isNumeric = field.fieldType === 'Number' || field.fieldType === 'Decimal';
  const isText = field.fieldType === 'ShortText' || field.fieldType === 'LongText';
  const toNumber = (text: string) => (text.trim() === '' || Number.isNaN(Number(text)) ? null : Number(text));
  const rulesInvalid =
    (min.trim() !== '' && toNumber(min) === null) ||
    (max.trim() !== '' && toNumber(max) === null) ||
    (toNumber(min) !== null && toNumber(max) !== null && toNumber(min)! > toNumber(max)!);

  // ---- Basics: names save when the box is left (Safe, no confirmation).
  function saveNames() {
    const ar = labelAr.trim();
    const en = labelEn.trim();
    if (!ar) {
      setLabelAr(field.label);
      return;
    }
    if (ar === field.label && en === (field.labelEn ?? '')) return;
    void run(() => api.forms.updateFieldLabel(token, formId, field.id, ar, en), 'fieldEditor.relabelError');
  }

  const controller = controllableFields.find((f) => f.code === conditionField);
  const controllerChoices = controller ? controllerValues(controller, t, optionLabel) : [];
  const conditionChanged =
    conditionField !== (field.visibleWhenFieldCode ?? '') ||
    JSON.stringify([...conditionValues].sort()) !== JSON.stringify([...parseAllowedValues(field.visibleWhenValuesJson)].sort());
  const staticDropdown = field.fieldType === 'Dropdown' && !field.dynamicOptionsSourceFormDefinitionId && !!field.optionsJson;
  const optionsChanged = JSON.stringify(options) !== JSON.stringify(parseOptions(field.optionsJson).map((o) => ({ value: o.value, label: o.label, labelEn: o.labelEn ?? '' })));
  const rulesChanged =
    required !== field.isRequired ||
    JSON.stringify({ min, max, minExclusive, regex, noFuture, unique, uniqueWithin }) !==
      JSON.stringify({
        min: rules0.min?.toString() ?? '',
        max: rules0.max?.toString() ?? '',
        minExclusive: rules0.minExclusive,
        regex: rules0.regex ?? '',
        noFuture: rules0.noFutureDates,
        unique: rules0.unique,
        uniqueWithin: rules0.uniqueWithin ?? '',
      });

  return (
    <section aria-label={t('builderV2.settingsTitle')} className="flex min-w-0 flex-[999_1_380px] flex-col gap-3">
      {/* 1. Basics */}
      <SettingsCard
        title={
          <span className="flex items-center gap-2 text-[18px] font-extrabold">
            <bdi>{fieldLabel(field)}</bdi>
            {archived && <Pill tone="neutral">{t('fieldEditor.archivedBadge')}</Pill>}
          </span>
        }
        aside={
          <span className="shrink-0 text-[13px] text-ink-soft">
            {t(`fieldType.${field.fieldType}`)}
            {stepLabel ? ` · ${stepLabel}` : ''}
          </span>
        }
      >
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <label htmlFor={ids.ar} className="text-[14px] font-bold text-ink">
              {t('builderV2.nameAr')}
            </label>
            <input id={ids.ar} dir="rtl" className={builderInput} value={labelAr} disabled={busy || archived} onChange={(e) => setLabelAr(e.target.value)} onBlur={saveNames} onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()} />
          </div>
          <div className="flex flex-col gap-1.5">
            <label htmlFor={ids.en} className="flex items-center gap-2 text-[14px] font-bold text-ink">
              {t('builderV2.nameEn')}
              {!field.labelEn && <span className="rounded-ui-pill bg-warning-soft px-[7px] py-px text-[11px] font-bold text-warning">{t('builderV2.newBadge')}</span>}
            </label>
            <input id={ids.en} dir="ltr" className={builderInput} value={labelEn} disabled={busy || archived} onChange={(e) => setLabelEn(e.target.value)} onBlur={saveNames} onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()} />
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-[10px] text-[14px]">
          <span className="text-ink-soft">{t('builderV2.code')}</span>
          <span dir="ltr" className="rounded-[8px] bg-field-muted px-[10px] py-1 font-mono text-ink">
            {field.code}
          </span>
          {!archived && (
            <>
              <button type="button" className={smallButton} disabled={busy} onClick={() => setRisky(risky === 'code' ? 'none' : 'code')}>
                {t('fieldEditor.renameCode')}
              </button>
              <button type="button" className={smallButton} disabled={busy} onClick={() => setRisky(risky === 'type' ? 'none' : 'type')}>
                {t('fieldEditor.changeType')}
              </button>
            </>
          )}
          {savedNote && <span className="text-[13px] text-success" role="status">{savedNote}</span>}
        </div>

        {/* Risky 1: rename the code / physical column - the existing confirmation. */}
        {risky === 'code' && (
          <div className="flex flex-col gap-2 border-t border-divider pt-3">
            <p className="text-[14px] text-ink-soft">{isLive ? t('fieldEditor.renameCodeConsequenceLive') : t('fieldEditor.renameCodeConsequenceDraft')}</p>
            <div className="flex flex-wrap items-center gap-2">
              <input dir="ltr" className={`${builderInput} max-w-xs font-mono`} value={codeDraft} disabled={busy} onChange={(e) => setCodeDraft(e.target.value)} />
              <Button size="desktop" disabled={busy || !codeDraft.trim() || codeDraft.trim() === field.code} onClick={() => run(() => api.forms.renameFieldCode(token, formId, field.id, codeDraft.trim()), 'fieldEditor.renameCodeError')}>
                {t('fieldEditor.confirmRenameCode', { from: field.code, to: codeDraft.trim() || '…' })}
              </Button>
              <Button size="desktop" variant="link" onClick={() => setRisky('none')}>
                {t('common.cancel')}
              </Button>
            </div>
          </div>
        )}

        {/* Risky 2: change the type / column type - the existing confirmation. */}
        {risky === 'type' && (
          <div className="flex flex-col gap-2 border-t border-divider pt-3">
            <p className="text-[14px] text-ink-soft">{isLive ? t('fieldEditor.changeTypeConsequenceLive') : t('fieldEditor.changeTypeConsequenceDraft')}</p>
            <div className="flex flex-wrap items-center gap-2">
              <select className={`${builderInput} max-w-xs`} value={typeDraft} disabled={busy} onChange={(e) => setTypeDraft(e.target.value as FieldType)}>
                {FIELD_TYPES.map((type) => (
                  <option key={type} value={type}>
                    {t(`fieldType.${type}`)}
                  </option>
                ))}
              </select>
              {typeDraft === 'Lookup' && (
                <select className={`${builderInput} max-w-xs`} value={lookupTargetDraft} disabled={busy} onChange={(e) => setLookupTargetDraft(e.target.value)}>
                  <option value="">{t('fieldEditor.chooseLookupTarget')}</option>
                  {lookupTargets.map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.name}
                    </option>
                  ))}
                </select>
              )}
              <Button
                size="desktop"
                disabled={busy || typeDraft === field.fieldType || (typeDraft === 'Lookup' && !lookupTargetDraft) || typeDraft === 'Dropdown'}
                onClick={() =>
                  run(
                    () => api.forms.changeFieldType(token, formId, field.id, { newFieldType: typeDraft, optionsJson: null, lookupFormDefinitionId: typeDraft === 'Lookup' ? lookupTargetDraft : null }),
                    'fieldEditor.changeTypeError',
                  )
                }
              >
                {t('fieldEditor.confirmChangeType', { from: t(`fieldType.${field.fieldType}`), to: t(`fieldType.${typeDraft}`) })}
              </Button>
              <Button size="desktop" variant="link" onClick={() => setRisky('none')}>
                {t('common.cancel')}
              </Button>
            </div>
            {typeDraft === 'Dropdown' && <p className="text-[13px] text-ink-soft">{t('fieldEditor.dropdownTypeUnsupported')}</p>}
          </div>
        )}
      </SettingsCard>

      {!archived && (
        <>
          {/* 2. When is it shown? - "Shown only when [field] = [value]", or "Always show it". */}
          {controllableFields.length > 0 && (
            <SettingsCard title={t('builderV2.whenShown')}>
              <div className="flex flex-wrap items-center gap-2 text-[15px] text-ink">
                <span>{t('builderV2.shownOnlyWhen')}</span>
                <select
                  aria-label={t('builderV2.shownOnlyWhen')}
                  className={`h-[40px] max-w-full rounded-ui-md border px-3 text-[15px] font-bold ${conditionField ? 'border-accent bg-accent-soft text-accent-strong' : 'border-border bg-panel text-ink'}`}
                  value={conditionField}
                  disabled={busy}
                  onChange={(e) => {
                    setConditionField(e.target.value);
                    setConditionValues([]);
                  }}
                >
                  <option value="">…</option>
                  {controllableFields.map((f) => (
                    <option key={f.code} value={f.code}>
                      {fieldLabel(f)}
                    </option>
                  ))}
                </select>
                {controller && (
                  <>
                    <span>=</span>
                    {controllerChoices.map((c) => {
                      const on = conditionValues.includes(c.value);
                      return (
                        <button
                          key={c.value}
                          type="button"
                          aria-pressed={on}
                          disabled={busy}
                          onClick={() => setConditionValues((prev) => (on ? prev.filter((v) => v !== c.value) : [...prev, c.value]))}
                          className={`h-[40px] rounded-ui-md border px-3 text-[15px] ${on ? 'border-accent bg-accent-soft font-bold text-accent-strong' : 'border-border bg-panel text-ink'}`}
                        >
                          <bdi>{c.label}</bdi>
                        </button>
                      );
                    })}
                  </>
                )}
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  size="desktop"
                  disabled={busy || !conditionChanged || (!!conditionField && conditionValues.length === 0)}
                  onClick={() =>
                    run(
                      () =>
                        api.forms.updateFieldVisibility(token, formId, field.id, {
                          visibleWhenFieldCode: conditionField || null,
                          visibleWhenValuesJson: conditionField ? JSON.stringify(conditionValues) : null,
                        }),
                      'fieldEditor.visibilityError',
                    )
                  }
                >
                  {t('common.save')}
                </Button>
                {(field.visibleWhenFieldCode || conditionField) && (
                  <Button
                    size="desktop"
                    variant="link"
                    disabled={busy}
                    onClick={() =>
                      field.visibleWhenFieldCode
                        ? run(() => api.forms.updateFieldVisibility(token, formId, field.id, { visibleWhenFieldCode: null, visibleWhenValuesJson: null }), 'fieldEditor.visibilityError')
                        : (setConditionField(''), setConditionValues([]))
                    }
                  >
                    {t('builderV2.alwaysShow')}
                  </Button>
                )}
              </div>
            </SettingsCard>
          )}

          {/* Lookup: the existing "Filter by…" setting. */}
          {field.fieldType === 'Lookup' && filterableFields.length > 0 && (
            <SettingsCard title={t('fieldEditor.lookupFilter')}>
              <p className="text-[14px] text-ink-soft">{t('fieldEditor.lookupFilterDescription')}</p>
              <div className="flex flex-wrap items-center gap-2">
                <select className={`${builderInput} max-w-xs`} value={filterBy} disabled={busy} onChange={(e) => setFilterBy(e.target.value)}>
                  <option value="">{t('fieldEditor.noLookupFilter')}</option>
                  {filterableFields.map((f) => (
                    <option key={f.code} value={f.code}>
                      {fieldLabel(f)}
                    </option>
                  ))}
                </select>
                <Button size="desktop" disabled={busy || filterBy === (field.filterByFieldCode ?? '')} onClick={() => run(() => api.forms.updateFieldLookupFilter(token, formId, field.id, filterBy || null), 'fieldEditor.lookupFilterError')}>
                  {t('common.save')}
                </Button>
              </div>
            </SettingsCard>
          )}

          {/* Dropdown: value + Arabic label + English label rows (values can't change here). */}
          {staticDropdown && (
            <SettingsCard title={t('builderV2.optionsTitle')}>
              <div className="grid grid-cols-[minmax(0,0.8fr)_minmax(0,1fr)_minmax(0,1fr)] gap-2 text-[13px] font-bold text-ink-soft">
                <span>{t('builderV2.optionValue')}</span>
                <span>{t('builderV2.optionLabelAr')}</span>
                <span>{t('builderV2.optionLabelEn')}</span>
              </div>
              {options.map((o, i) => (
                <div key={o.value} className="grid grid-cols-[minmax(0,0.8fr)_minmax(0,1fr)_minmax(0,1fr)] items-center gap-2">
                  <span dir="ltr" className="truncate rounded-[8px] bg-field-muted px-[10px] py-2 font-mono text-[13px] text-ink">
                    {o.value}
                  </span>
                  <input dir="rtl" aria-label={`${t('builderV2.optionLabelAr')} ${o.value}`} className={builderInput} value={o.label} disabled={busy} onChange={(e) => setOptions((prev) => prev.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)))} />
                  <input dir="ltr" aria-label={`${t('builderV2.optionLabelEn')} ${o.value}`} className={builderInput} value={o.labelEn} disabled={busy} onChange={(e) => setOptions((prev) => prev.map((x, j) => (j === i ? { ...x, labelEn: e.target.value } : x)))} />
                </div>
              ))}
              <div>
                <Button
                  size="desktop"
                  disabled={busy || !optionsChanged || options.some((o) => !o.label.trim())}
                  onClick={() => run(() => api.forms.updateOptionLabels(token, formId, field.id, options.map((o) => ({ value: o.value, label: o.label.trim(), labelEn: o.labelEn.trim() || null }))), 'fieldEditor.relabelError')}
                >
                  {t('common.save')}
                </Button>
              </div>
            </SettingsCard>
          )}

          {/* 3. Rules - Required switch plus the type's own rules, inline. */}
          {/* As before, attachments have no Rules (no column; required-ness is set when added). */}
          {field.fieldType !== 'Attachment' ? (
            <SettingsCard title={t('builderV2.rulesTitle')}>
              <div className="flex items-center justify-between gap-3 text-[15px] text-ink">
                <span id={ids.req}>
                  {t('fieldEditor.requiredLabel')} {field.visibleWhenFieldCode && <span className="text-[13px] text-ink-soft">{t('builderV2.requiredOnlyWhenShown')}</span>}
                </span>
                <Switch checked={required} onChange={setRequired} labelledBy={ids.req} disabled={busy} />
              </div>
              {isNumeric && (
                <div className="flex flex-wrap items-end gap-3">
                  <label className="flex flex-col gap-1 text-[14px] text-ink">
                    {t('fieldEditor.min')}
                    <input dir="ltr" inputMode="decimal" className={`${builderInput} !w-28 text-center`} value={min} disabled={busy} onChange={(e) => setMin(e.target.value)} />
                  </label>
                  <label className="flex flex-col gap-1 text-[14px] text-ink">
                    {t('fieldEditor.max')}
                    <input dir="ltr" inputMode="decimal" className={`${builderInput} !w-28 text-center`} value={max} disabled={busy} onChange={(e) => setMax(e.target.value)} />
                  </label>
                  <label className="flex min-h-[44px] items-center gap-2 text-[14px] text-ink">
                    <input type="checkbox" checked={minExclusive} disabled={busy || min.trim() === ''} onChange={(e) => setMinExclusive(e.target.checked)} />
                    {t('fieldEditor.minExclusive')}
                  </label>
                </div>
              )}
              {isText && (
                <label className="flex flex-col gap-1 text-[14px] text-ink">
                  {t('fieldEditor.pattern')}
                  <input dir="ltr" className={`${builderInput} max-w-sm font-mono`} value={regex} disabled={busy} onChange={(e) => setRegex(e.target.value)} />
                </label>
              )}
              {field.fieldType === 'DateTime' && (
                <label className="flex min-h-[44px] items-center gap-2 text-[14px] text-ink">
                  <input type="checkbox" checked={noFuture} disabled={busy} onChange={(e) => setNoFuture(e.target.checked)} />
                  {t('fieldEditor.noFutureDates')}
                </label>
              )}
              {field.fieldType === 'ShortText' && (
                <div className="flex flex-col gap-2">
                  <label className="flex min-h-[44px] items-center gap-2 text-[14px] text-ink">
                    <input type="checkbox" checked={unique} disabled={busy} onChange={(e) => setUnique(e.target.checked)} />
                    {t('fieldEditor.unique')}
                  </label>
                  {unique && filterableFields.length > 0 && (
                    <label className="flex flex-col gap-1 text-[14px] text-ink">
                      {t('fieldEditor.uniqueWithin')}
                      <select className={`${builderInput} max-w-sm`} value={uniqueWithin} disabled={busy} onChange={(e) => setUniqueWithin(e.target.value)}>
                        <option value="">{t('fieldEditor.uniqueWithinAll')}</option>
                        {filterableFields.map((f) => (
                          <option key={f.id} value={f.code}>
                            {fieldLabel(f)}
                          </option>
                        ))}
                      </select>
                    </label>
                  )}
                </div>
              )}
              <div>
                <Button
                  size="desktop"
                  disabled={busy || rulesInvalid || !rulesChanged}
                  onClick={() =>
                    run(
                      () =>
                        api.forms.updateFieldRules(token, formId, field.id, {
                          isRequired: required,
                          min: isNumeric ? toNumber(min) : null,
                          max: isNumeric ? toNumber(max) : null,
                          minExclusive: isNumeric && toNumber(min) !== null && minExclusive,
                          regex: isText && regex.trim() ? regex.trim() : null,
                          noFutureDates: field.fieldType === 'DateTime' && noFuture,
                          unique: field.fieldType === 'ShortText' && unique,
                          uniqueWithin: field.fieldType === 'ShortText' && unique && uniqueWithin ? uniqueWithin : null,
                        }),
                      'fieldEditor.rulesError',
                    )
                  }
                >
                  {t('common.save')}
                </Button>
              </div>
            </SettingsCard>
          ) : null}
        </>
      )}

      {/* 4. Actions - Archive (keeps the data) and Delete permanently, each with its existing consequence text. */}
      <div className="flex flex-wrap gap-2">
        {archived ? (
          <Button size="desktop" variant="secondary" disabled={busy} onClick={() => run(() => api.forms.restoreField(token, formId, field.id), 'fieldEditor.restoreError')}>
            {t('fieldEditor.restore')}
          </Button>
        ) : (
          <Button size="desktop" variant="secondary" disabled={busy} onClick={() => setRisky(risky === 'archive' ? 'none' : 'archive')} className="!h-[40px] !min-h-[40px] !text-[14px] !font-normal">
            {t('builderV2.archiveKeepsData')}
          </Button>
        )}
        <Button size="desktop" variant="danger" disabled={busy} onClick={() => setRisky(risky === 'delete' ? 'none' : 'delete')} className="!h-[40px] !min-h-[40px] !text-[14px] !font-normal">
          {t('fieldEditor.deleteAction')}
        </Button>
      </div>
      {risky === 'archive' && (
        <SettingsCard title={t('fieldEditor.archiveHeading')}>
          <p className="text-[14px] text-ink-soft">{isLive ? t('fieldEditor.archiveConsequenceLive') : t('fieldEditor.archiveConsequenceDraft')}</p>
          <div className="flex gap-2">
            <Button size="desktop" disabled={busy} onClick={() => run(() => api.forms.archiveField(token, formId, field.id), 'fieldEditor.archiveError')}>
              {t('fieldEditor.archiveAction')}
            </Button>
            <Button size="desktop" variant="link" onClick={() => setRisky('none')}>
              {t('common.cancel')}
            </Button>
          </div>
        </SettingsCard>
      )}
      {risky === 'delete' && (
        <div className="flex flex-col gap-3 rounded-ui-2xl border border-danger-border bg-panel p-[18px]">
          <h3 className="text-[16px] font-extrabold text-danger">{t('fieldEditor.deleteHeading')}</h3>
          <p className="text-[14px] text-ink-soft">{isLive ? t('fieldEditor.deleteConsequenceLive', { code: field.code }) : t('fieldEditor.deleteConsequenceDraft')}</p>
          {isLive && (
            <input dir="ltr" className={`${builderInput} max-w-xs font-mono`} placeholder={field.code} value={deleteConfirm} disabled={busy} onChange={(e) => setDeleteConfirm(e.target.value)} aria-label={t('fieldEditor.deleteConfirmLabel', { code: field.code })} />
          )}
          <div className="flex gap-2">
            <Button
              size="desktop"
              variant="danger"
              disabled={busy || (isLive && deleteConfirm.trim() !== field.code)}
              onClick={() => run(() => api.forms.deleteField(token, formId, field.id, isLive ? deleteConfirm.trim() : undefined), 'fieldEditor.deleteError', false)}
            >
              {t('fieldEditor.deleteAction')}
            </Button>
            <Button size="desktop" variant="link" onClick={() => setRisky('none')}>
              {t('common.cancel')}
            </Button>
          </div>
        </div>
      )}
      {error && (
        <p className="text-[14px] text-danger" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
