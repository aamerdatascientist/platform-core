import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { todayIso } from '../DateField';
import { ChipGroup, DateQuickPick, FieldShell, LookupRow, Stepper, TextArea, TextInput, YesNo } from '../ui';
import { parseRules } from '../../forms/fieldValues';
import { useLocalized } from '../../forms/localized';
import type { DropdownOption, FieldDefinitionDto } from '../../types';
import { AttachmentTiles } from './AttachmentTiles';
import { LookupSheet } from './LookupSheet';
import type { Choice } from './LookupSheet';

/** A short dropdown becomes chips: at most 6 options, every label at most 24 characters (HANDOFF §7). */
export function chipsFit(options: { label: string }[]) {
  return options.length > 0 && options.length <= 6 && options.every((o) => o.label.length <= 24);
}

/**
 * One form field, with the control HANDOFF §7 gives its type:
 * date -> Today / Yesterday / Other date; whole number -> stepper; decimal -> decimal keypad;
 * yes/no -> two buttons; short dropdown -> chips; long dropdown or Lookup -> a row that opens the
 * search sheet; short/long text -> input/textarea; attachment -> photo tiles.
 */
export function FieldControl({
  field,
  value,
  onChange,
  files,
  onFiles,
  error,
  options,
  dynamicOptions,
  lookupChoices,
  filterWaitingOnLabel,
  isFiltered,
  filteredByValue,
  recent,
  isLastChoice,
  onLookupPicked,
  inCard = false,
}: {
  field: FieldDefinitionDto;
  value: string;
  onChange: (v: string) => void;
  files: File[];
  onFiles: (files: File[]) => void;
  error: string | null;
  options: DropdownOption[];
  /** Live values for a dynamic-options dropdown; undefined while loading. */
  dynamicOptions?: string[];
  lookupChoices?: { id: string; label: string }[];
  /** Set when this Lookup depends on another field that has no value yet. */
  filterWaitingOnLabel?: string;
  isFiltered?: boolean;
  /** The chosen value this Lookup's list is narrowed by (05: "Only choices for Demo project"). */
  filteredByValue?: string;
  recent: string[];
  /** The value was pre-selected from the person's last choice ("Your last choice"). */
  isLastChoice?: boolean;
  onLookupPicked?: (value: string) => void;
  /** Inside a conditional card (05): the lookup row is the muted 52px variant. */
  inCard?: boolean;
}) {
  const { t } = useTranslation();
  const [sheetOpen, setSheetOpen] = useState(false);
  const { fieldLabel, optionLabel } = useLocalized();
  const label = fieldLabel(field);
  const rules = parseRules(field.validationRulesJson);
  const type = field.fieldType;

  // Choices for the dropdown/lookup family, one shape for chips and the sheet alike.
  const choices: Choice[] | undefined =
    type === 'Lookup'
      ? lookupChoices?.map((c) => ({ value: c.id, label: c.label }))
      : type === 'Dropdown' && field.dynamicOptionsSourceFormDefinitionId
        ? dynamicOptions?.map((v) => ({ value: v, label: v }))
        : type === 'Dropdown'
          ? options.map((o) => ({ value: o.value, label: optionLabel(o) }))
          : undefined;
  const chips = type === 'Dropdown' && choices !== undefined && chipsFit(choices);
  const asGroup = type === 'DateTime' || type === 'Boolean' || chips || type === 'Attachment';
  const noChoices = type === 'Lookup' && !filterWaitingOnLabel && choices !== undefined && choices.length === 0;

  const hint =
    type === 'Attachment'
      ? t('steps.fileHint')
      : noChoices
        ? isFiltered
          ? t('formRenderer.noChoicesForFilter')
          : t('formRenderer.noChoicesHint')
        : isFiltered && filteredByValue && choices !== undefined
          ? t('steps.filteredBy', { value: filteredByValue })
          : null;

  return (
    <FieldShell label={<bdi>{label}</bdi>} required={field.isRequired} hint={hint} error={error} asGroup={asGroup}>
      {({ inputId, labelId, describedBy, invalid }) => {
        switch (type) {
          case 'DateTime':
            return (
              <div className="flex flex-col gap-1">
                <DateQuickPick value={value} onChange={onChange} maxDate={rules.noFutureDates ? todayIso() : undefined} invalid={invalid} />
                {!field.isRequired && value && (
                  <button type="button" onClick={() => onChange('')} className="min-h-[44px] self-start text-[15px] text-accent">
                    {t('formRenderer.clearDate')}
                  </button>
                )}
              </div>
            );
          case 'Number':
            return <Stepper id={inputId} value={value} onChange={onChange} invalid={invalid} describedBy={describedBy} min={rules.min ?? undefined} />;
          case 'Decimal':
            return (
              <TextInput
                id={inputId}
                type="text"
                inputMode="decimal"
                autoComplete="off"
                dir="ltr"
                className="text-start rtl:text-right"
                value={value}
                onChange={(e) => onChange(e.target.value)}
                invalid={invalid}
                aria-describedby={describedBy}
              />
            );
          case 'Boolean':
            return (
              <YesNo
                labelledBy={labelId}
                value={value === '' ? null : value === 'true'}
                onChange={(v) => onChange(String(v))}
                yesLabel={t('common.yes')}
                noLabel={t('common.no')}
                invalid={invalid}
                describedBy={describedBy}
              />
            );
          case 'LongText':
            return <TextArea id={inputId} value={value} onChange={(e) => onChange(e.target.value)} invalid={invalid} aria-describedby={describedBy} />;
          case 'Attachment':
            return <AttachmentTiles files={files} onChange={onFiles} invalid={invalid} describedBy={describedBy} labelledBy={labelId} />;
          case 'Dropdown':
          case 'Lookup': {
            if (chips && choices)
              return <ChipGroup labelledBy={labelId} value={value || null} onChange={onChange} options={choices} invalid={invalid} describedBy={describedBy} compact={choices.some((c) => c.label.length > 14)} />;
            const chosen = choices?.find((c) => c.value === value);
            const placeholder = filterWaitingOnLabel
              ? t('formRenderer.chooseFilterSourceFirst', { label: filterWaitingOnLabel })
              : choices === undefined
                ? t('formRenderer.loadingOptions')
                : noChoices
                  ? t('formRenderer.noChoices')
                  : t('lookup.choose', { label });
            return (
              <>
                <div className={inCard ? '[&>button]:!min-h-[52px] [&>button]:!rounded-ui-lg [&>button]:!bg-field-muted' : ''}>
                  <LookupRow
                    id={inputId}
                    value={chosen?.label ?? null}
                    sub={chosen && isLastChoice ? t('steps.lastChoice') : null}
                    placeholder={placeholder}
                    onOpen={() => setSheetOpen(true)}
                    disabled={!!filterWaitingOnLabel || noChoices || choices === undefined}
                    invalid={invalid}
                    describedBy={describedBy}
                    icon={type === 'Lookup' && !inCard ? 'pin' : null}
                  />
                </div>
                <LookupSheet
                  open={sheetOpen}
                  onClose={() => setSheetOpen(false)}
                  title={t('lookup.choose', { label })}
                  choices={choices ?? []}
                  value={value}
                  recent={recent}
                  onPick={(v) => {
                    onChange(v);
                    onLookupPicked?.(v);
                  }}
                />
              </>
            );
          }
          default:
            return <TextInput id={inputId} type="text" value={value} onChange={(e) => onChange(e.target.value)} invalid={invalid} aria-describedby={describedBy} />;
        }
      }}
    </FieldShell>
  );
}
