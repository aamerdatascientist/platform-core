import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import { isFieldVisible, isMulti, parseOptions } from '../../forms/fieldValues';
import { pickLanguage } from '../../forms/localized';
import { Button, Card } from '../ui';
import { Icon } from '../icons';
import { FieldControl } from './FieldControl';
import type { ChoiceOptionDto, FieldDefinitionDto, FormSectionDto } from '../../types';

/** A choice field whose options come from the server (Lookup rows or an options provider). */
export const serverChoices = (f: FieldDefinitionDto) =>
  f.fieldType === 'Lookup' || f.fieldType === 'MultiLookup' || (!!f.optionsProvider && (f.fieldType === 'Dropdown' || f.fieldType === 'MultiSelect'));

/** The values a server choice field's options depend on: every chosen value of a choice field
 *  (header and block), never free text - so typing a description doesn't refetch anything. */
export function choiceContext(fields: FieldDefinitionDto[], values: Record<string, string>, except: string): Record<string, string> {
  const ctx: Record<string, string> = {};
  for (const f of fields) {
    if (f.code === except || isMulti(f)) continue;
    if (f.fieldType !== 'Lookup' && f.fieldType !== 'Dropdown') continue;
    const v = values[f.code];
    if (v) ctx[f.code] = v;
  }
  return ctx;
}

const contextKey = (fieldId: string, ctx: Record<string, string>) =>
  `${fieldId}|${Object.keys(ctx).sort().map((k) => `${k}=${ctx[k]}`).join('&')}`;

/**
 * Options for server choice fields, fetched once per (field, context) and kept for the life of
 * the form. Returns a reader; undefined while loading.
 */
export function useServerOptions(token: string, formId: string) {
  const [cache, setCache] = useState<Record<string, ChoiceOptionDto[]>>({});
  const asked = useRef(new Set<string>());
  const read = (field: FieldDefinitionDto, ctx: Record<string, string>): ChoiceOptionDto[] | undefined => {
    const key = contextKey(field.id, ctx);
    if (!(key in cache) && !asked.current.has(key)) {
      asked.current.add(key);
      void api.forms
        .lookupOptions(token, formId, field.id, field.filterByFieldCode ? ctx[field.filterByFieldCode] : undefined, ctx)
        .then((options) => setCache((c) => ({ ...c, [key]: options })))
        .catch(() => setCache((c) => ({ ...c, [key]: [] })));
    }
    return cache[key];
  };
  return read;
}

/**
 * A repeatable section (2026-11-10): one card per block, "Add another" below, Remove on each
 * card while there are more than the minimum. Every block shows its own fields by its own
 * answers (the server checks each block the same way), and a block's choices come from the
 * header's and the block's own values - e.g. the stages of the chosen project, the floors in
 * the chosen stage's scope.
 */
export function RepeatableSection({
  token,
  formId,
  section,
  fields,
  headerFields,
  header,
  blocks,
  onChange,
  errorText,
  onBlurField,
  readOptions,
  preview,
}: {
  token: string;
  formId: string;
  section: FormSectionDto;
  fields: FieldDefinitionDto[];
  headerFields: FieldDefinitionDto[];
  header: Record<string, string>;
  blocks: Record<string, string>[];
  onChange: (blocks: Record<string, string>[], changed?: { index: number; code: string }) => void;
  errorText: (key: string) => string | null;
  onBlurField?: (index: number, field: FieldDefinitionDto) => void;
  readOptions: ReturnType<typeof useServerOptions>;
  preview?: boolean;
}) {
  const { t, i18n } = useTranslation();
  const title = pickLanguage(section.titleAr, section.titleEn, i18n.language);
  const ordered = useMemo(() => [...fields].sort((a, b) => a.displayOrder - b.displayOrder), [fields]);
  void token;
  void formId;

  // A section with a minimum always shows at least that many blocks.
  useEffect(() => {
    if (blocks.length < Math.max(1, section.minBlocks) && !preview) {
      onChange([...blocks, ...Array.from({ length: Math.max(1, section.minBlocks) - blocks.length }, () => ({}))]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [blocks.length, section.minBlocks]);

  const shown = preview && blocks.length === 0 ? [{}] : blocks;

  const setBlockValue = (index: number, code: string, value: string) => {
    const next = blocks.map((b, i) => {
      if (i !== index) return b;
      const updated: Record<string, string> = { ...b, [code]: value };
      // A changed choice clears the later server choices in the block (their options
      // depend on it: another stage has other sub-steps and floors).
      const at = ordered.findIndex((f) => f.code === code);
      ordered.slice(at + 1).forEach((f) => {
        if (serverChoices(f)) delete updated[f.code];
      });
      return updated;
    });
    onChange(next, { index, code });
  };

  return (
    <div className="flex flex-col gap-4">
      {shown.map((block, index) => {
        const merged = { ...header, ...block };
        const all = [...headerFields, ...ordered];
        const optionsFor = (f: FieldDefinitionDto) => (serverChoices(f) ? readOptions(f, choiceContext(all, merged, f.code)) : undefined);
        const attributeOf = (controller: string, attribute: string) => {
          const f = all.find((x) => x.code === controller);
          const chosen = merged[controller];
          return f && chosen ? optionsFor(f)?.find((o) => o.id === chosen)?.attributes?.[attribute] : undefined;
        };
        const visible = ordered.filter((f) => isFieldVisible(f, merged, attributeOf));
        return (
          <Card key={index} kind="phone-secondary" className="flex flex-col gap-4 px-4 py-4" data-block={`${section.code}-${index}`}>
            <div className="flex items-center justify-between gap-3">
              <h3 className="text-[16px] font-bold text-ink">
                <bdi>{title}</bdi> {index + 1}
              </h3>
              {!preview && blocks.length > Math.max(section.minBlocks, 1) && (
                <Button
                  size="compact"
                  variant="link"
                  className="!text-danger"
                  onClick={() => onChange(blocks.filter((_, i) => i !== index))}
                  aria-label={t('sections.removeBlock', { title, n: index + 1 })}
                >
                  {t('common.remove')}
                </Button>
              )}
            </div>
            {visible.map((field) => {
              const key = `${section.code}[${index}].${field.code}`;
              const options = optionsFor(field);
              const filterSource = field.filterByFieldCode ? all.find((f) => f.code === field.filterByFieldCode) : undefined;
              const waiting = filterSource && !merged[filterSource.code] ? pickLanguage(filterSource.label, filterSource.labelEn, i18n.language) : undefined;
              return (
                <div key={field.id} id={`field-${field.id}-${index}`} onBlur={() => onBlurField?.(index, field)}>
                  <FieldControl
                    field={field}
                    value={block[field.code] ?? ''}
                    onChange={(v) => setBlockValue(index, field.code, v)}
                    files={[]}
                    onFiles={() => undefined}
                    error={errorText(key)}
                    options={parseOptions(field.optionsJson)}
                    lookupChoices={options}
                    filterWaitingOnLabel={waiting}
                    isFiltered={!!field.filterByFieldCode}
                    recent={[]}
                    inCard
                  />
                </div>
              );
            })}
          </Card>
        );
      })}
      {!preview && blocks.length < section.maxBlocks && (
        <Button variant="secondary" onClick={() => onChange([...blocks, {}])} className="self-start">
          <Icon name="plus" size={18} /> {t('sections.addAnother', { title: i18n.language.startsWith('ar') ? title : title.toLocaleLowerCase() })}
        </Button>
      )}
    </div>
  );
}
