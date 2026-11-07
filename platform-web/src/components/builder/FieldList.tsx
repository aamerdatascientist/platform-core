import { useState } from 'react';
import type { DragEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { parseOptions, parseRules } from '../../forms/fieldValues';
import { useLocalized } from '../../forms/localized';
import type { FieldDefinitionDto, FormSummaryDto } from '../../types';
import { Icon } from '../icons';

export interface BuilderStep {
  /** null for the single default step of a form that has none. */
  id: string | null;
  title: string;
  titleAr: string;
  titleEn: string | null;
  fields: FieldDefinitionDto[];
}

/**
 * Pane 1 of the builder (screen 21): fields grouped under their steps ("Step 1 · Basics" with a
 * field count). Rows can be dragged within or between steps; the keyboard alternative is the
 * move up / move down buttons (and "Move to step") on the selected row. The selected row has an
 * --accent border on --accent-row. A dashed "+ Add field or step" button sits at the bottom.
 */
export function FieldList({
  steps,
  archived,
  selectedId,
  lookupTargets,
  busy,
  onSelect,
  onMove,
  onEditStep,
  onMoveStep,
  onAdd,
}: {
  steps: BuilderStep[];
  archived: FieldDefinitionDto[];
  selectedId: string | null;
  lookupTargets: FormSummaryDto[];
  busy: boolean;
  onSelect: (fieldId: string) => void;
  /** Put the field in this step, before `beforeId` (or at the end of the step). */
  onMove: (fieldId: string, stepId: string | null, beforeId: string | null) => void;
  onEditStep: (step: BuilderStep) => void;
  onMoveStep: (stepId: string, direction: -1 | 1) => void;
  onAdd: () => void;
}) {
  const { t } = useTranslation();
  const { fieldLabel, formName } = useLocalized();
  const [dragging, setDragging] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<string | null>(null);
  const realSteps = steps.filter((s) => s.id !== null);

  /** Type plus its key rule (21): "Number · 0 or more", "Lookup · Projects", "Dropdown · 5 options". */
  const typeLine = (f: FieldDefinitionDto) => {
    const type = t(`fieldType.${f.fieldType}`);
    const rules = parseRules(f.validationRulesJson);
    if (f.fieldType === 'Lookup') {
      const target = lookupTargets.find((x) => x.id === f.lookupFormDefinitionId);
      if (target) return `${type} · ${formName(target)}`;
    }
    if (f.fieldType === 'Dropdown' && f.optionsJson && !f.dynamicOptionsSourceFormDefinitionId)
      return `${type} · ${t('builderV2.optionsCount', { count: parseOptions(f.optionsJson).length })}`;
    if ((f.fieldType === 'Number' || f.fieldType === 'Decimal') && rules.min !== null && rules.min !== undefined) return `${type} · ${t('builderV2.atLeast', { value: rules.min })}`;
    if ((f.fieldType === 'Number' || f.fieldType === 'Decimal') && rules.max !== null && rules.max !== undefined) return `${type} · ${t('builderV2.atMost', { value: rules.max })}`;
    return f.isRequired ? `${type} · ${t('fieldEditor.requiredLabel')}` : type;
  };

  const dropOn = (e: DragEvent, stepId: string | null, beforeId: string | null) => {
    e.preventDefault();
    const id = e.dataTransfer.getData('text/plain') || dragging;
    setDragging(null);
    setDropTarget(null);
    if (id && id !== beforeId) onMove(id, stepId, beforeId);
  };

  return (
    <section aria-label={t('builderV2.fieldsTitle')} className="flex max-w-[360px] flex-[1_1_300px] flex-col gap-1 rounded-ui-2xl border border-border bg-panel p-[10px]">
      {steps.map((step, si) => (
        <div
          key={step.id ?? 'default'}
          className="flex flex-col gap-0.5 pb-1.5"
          onDragOver={(e) => {
            e.preventDefault();
            setDropTarget(`end-${step.id}`);
          }}
          onDrop={(e) => dropOn(e, step.id, null)}
        >
          <div className="group/step relative flex items-center justify-between gap-2 px-2 pb-1.5 pt-2">
            <span className="min-w-0 truncate text-[13px] font-extrabold text-ink-soft">
              <bdi>{t('builderV2.stepHeading', { n: si + 1, title: step.title })}</bdi>
            </span>
            <span className="flex shrink-0 items-center gap-0.5">
              <span className="pe-1 text-[12px] text-ink-soft">{t('builderV2.fieldsCount', { count: step.fields.length })}</span>
              {step.id && (
                // Step actions sit over the count, shown on hover or keyboard focus, so the heading keeps its width.
                <span className="absolute end-1 top-1 flex items-center gap-0.5 rounded-ui-sm bg-panel opacity-0 focus-within:opacity-100 group-hover/step:opacity-100">
                  <button type="button" disabled={busy || si === 0} onClick={() => onMoveStep(step.id!, -1)} aria-label={t('builderV2.moveStepUp')} className="flex h-8 w-8 items-center justify-center rounded-ui-sm text-ink-soft hover:bg-field-muted disabled:opacity-30">
                    <Icon name="chevronDown" size={16} className="rotate-180" />
                  </button>
                  <button type="button" disabled={busy || si === realSteps.length - 1} onClick={() => onMoveStep(step.id!, 1)} aria-label={t('builderV2.moveStepDown')} className="flex h-8 w-8 items-center justify-center rounded-ui-sm text-ink-soft hover:bg-field-muted disabled:opacity-30">
                    <Icon name="chevronDown" size={16} />
                  </button>
                  <button type="button" disabled={busy} onClick={() => onEditStep(step)} aria-label={t('builderV2.renameStep')} className="flex h-8 w-8 items-center justify-center rounded-ui-sm text-ink-soft hover:bg-field-muted">
                    <Icon name="pencil" size={15} />
                  </button>
                </span>
              )}
            </span>
          </div>
          {step.fields.map((f, fi) => {
            const on = f.id === selectedId;
            return (
              <div
                key={f.id}
                draggable={!busy}
                onDragStart={(e) => {
                  e.dataTransfer.setData('text/plain', f.id);
                  e.dataTransfer.effectAllowed = 'move';
                  setDragging(f.id);
                }}
                onDragEnd={() => {
                  setDragging(null);
                  setDropTarget(null);
                }}
                onDragOver={(e) => {
                  e.preventDefault();
                  e.stopPropagation();
                  setDropTarget(f.id);
                }}
                onDrop={(e) => {
                  e.stopPropagation();
                  dropOn(e, step.id, f.id);
                }}
                data-field-row={f.id}
                className={`flex min-h-[48px] items-center gap-2 rounded-[10px] border px-2 py-1 ${on ? 'border-accent bg-accent-row' : 'border-transparent hover:bg-field-muted'} ${
                  dropTarget === f.id && dragging && dragging !== f.id ? 'shadow-[inset_0_2px_0_var(--accent)]' : ''
                } ${dragging === f.id ? 'opacity-50' : ''}`}
              >
                <span aria-hidden="true" title={t('builderV2.dragHandle')} className="cursor-grab text-ink-soft">
                  <Icon name="grip" size={16} />
                </span>
                <button type="button" onClick={() => onSelect(f.id)} aria-current={on || undefined} className="flex min-w-0 flex-1 flex-col gap-px py-1 text-start">
                  <span className="truncate text-[14px] font-bold text-ink">
                    <bdi>{fieldLabel(f)}</bdi>
                  </span>
                  <span className="truncate text-[12px] text-ink-soft">
                    <bdi>{typeLine(f)}</bdi>
                  </span>
                </button>
                {f.visibleWhenFieldCode && <span className="shrink-0 rounded-ui-pill bg-accent-soft px-[7px] py-0.5 text-[11px] font-bold text-accent-strong">{t('builderV2.conditional')}</span>}
                {on && (
                  <span className="flex shrink-0 items-center">
                    <button type="button" disabled={busy || (si === 0 && fi === 0)} onClick={() => (fi > 0 ? onMove(f.id, step.id, step.fields[fi - 1].id) : onMove(f.id, steps[si - 1].id, null))} aria-label={t('fieldEditor.moveUp')} className="flex h-8 w-8 items-center justify-center rounded-ui-sm text-ink-soft hover:bg-panel disabled:opacity-30">
                      <Icon name="chevronDown" size={16} className="rotate-180" />
                    </button>
                    <button
                      type="button"
                      disabled={busy || (si === steps.length - 1 && fi === step.fields.length - 1)}
                      onClick={() =>
                        fi < step.fields.length - 1
                          ? onMove(f.id, step.id, step.fields[fi + 2]?.id ?? null)
                          : onMove(f.id, steps[si + 1].id, steps[si + 1].fields[0]?.id ?? null)
                      }
                      aria-label={t('fieldEditor.moveDown')}
                      className="flex h-8 w-8 items-center justify-center rounded-ui-sm text-ink-soft hover:bg-panel disabled:opacity-30"
                    >
                      <Icon name="chevronDown" size={16} />
                    </button>
                  </span>
                )}
              </div>
            );
          })}
          {realSteps.length > 1 && steps.some((s) => s.fields.some((f) => f.id === selectedId)) && step.fields.some((f) => f.id === selectedId) && (
            <label className="flex items-center gap-2 px-2 pt-1 text-[12px] text-ink-soft">
              {t('builderV2.moveToStep')}
              <select
                className="h-8 rounded-ui-sm border border-border bg-panel px-2 text-[12px] text-ink"
                value={step.id ?? ''}
                disabled={busy}
                onChange={(e) => onMove(selectedId!, e.target.value || null, null)}
              >
                {realSteps.map((s, i) => (
                  <option key={s.id} value={s.id!}>
                    {t('builderV2.stepHeading', { n: i + 1, title: s.title })}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      ))}

      {archived.length > 0 && (
        <details className="px-2 pb-1">
          <summary className="cursor-pointer py-2 text-[13px] font-extrabold text-ink-soft">{t('builderV2.archivedHeading', { count: archived.length })}</summary>
          {archived.map((f) => (
            <button key={f.id} type="button" onClick={() => onSelect(f.id)} aria-current={f.id === selectedId || undefined} className={`flex min-h-[44px] w-full items-center rounded-[10px] border px-2 text-start text-[14px] text-ink-soft ${f.id === selectedId ? 'border-accent bg-accent-row' : 'border-transparent hover:bg-field-muted'}`}>
              <bdi className="truncate">{fieldLabel(f)}</bdi>
            </button>
          ))}
        </details>
      )}

      <button type="button" onClick={onAdd} disabled={busy} className="h-[42px] rounded-[10px] border-[1.5px] border-dashed border-sheet-handle bg-panel text-[14px] font-bold text-accent hover:border-accent">
        {t('builderV2.addFieldOrStep')}
      </button>
    </section>
  );
}
