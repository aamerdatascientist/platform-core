import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FieldDefinitionDto, FieldType, FormSummaryDto } from '../types';

const FIELD_TYPES: FieldType[] = [
  'ShortText', 'LongText', 'Number', 'Decimal', 'Boolean', 'DateTime', 'Dropdown', 'Lookup', 'Attachment',
];

const inputClass = 'w-full border border-border rounded bg-bg px-2 py-1.5 text-sm focus:border-accent focus:outline-none';

/** Same shape AddFieldForm uses to turn a Dropdown's OptionsJson (or a Boolean's implicit
 *  Yes/No) into checkable values for a visibility condition. Kept in sync by hand - both
 *  read the same FieldDefinitionDto shape, there's just no shared module between the two
 *  components yet. */
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

interface FieldEditorRowProps {
  token: string;
  formId: string;
  field: FieldDefinitionDto;
  /** True once the form has been published: edits hit live data and DDL runs immediately,
   *  which is what every confirmation below has to say out loud. */
  isLive: boolean;
  isFirst: boolean;
  isLast: boolean;
  lookupTargets: FormSummaryDto[];
  /** Other fields on this version this one could branch on - Dropdown/Boolean only. */
  controllableFields: FieldDefinitionDto[];
  /** Other fields on this version this field (if it's a Lookup) could filter its candidates
   *  by - any type, since the common case is filtering by another Lookup. */
  filterableFields: FieldDefinitionDto[];
  onChanged: () => Promise<void> | void;
  onMove: (fieldId: string, direction: -1 | 1) => Promise<void> | void;
}

/** Which risky operation has its confirmation open. Only one at a time - these each state a
 *  different consequence, and stacking them would make it ambiguous what's being confirmed. */
type OpenPanel = 'none' | 'code' | 'type' | 'remove' | 'visibility' | 'lookup-filter';

/**
 * One field in the builder, with all six edit operations split by risk exactly the way the
 * backend splits them:
 *
 *   Safe (relabel, reorder) apply on the spot with no dialog, because they only move
 *   metadata - no column is touched and no value can be lost, published form or not.
 *
 *   Risky (rename code, change type, remove) each open their own panel that states the
 *   actual consequence in plain language. Never a generic "are you sure?", and never a
 *   single "delete" that silently picks between archiving and destroying data.
 */
export function FieldEditorRow({
  token, formId, field, isLive, isFirst, isLast, lookupTargets, controllableFields, filterableFields, onChanged, onMove,
}: FieldEditorRowProps) {
  const { t } = useTranslation();
  const [error, setError] = useErrorMessage();
  const [busy, setBusy] = useState(false);
  const [panel, setPanel] = useState<OpenPanel>('none');

  const [editingLabel, setEditingLabel] = useState(false);
  const [labelDraft, setLabelDraft] = useState(field.label);
  const [codeDraft, setCodeDraft] = useState(field.code);
  const [typeDraft, setTypeDraft] = useState<FieldType>(field.fieldType);
  const [lookupTargetDraft, setLookupTargetDraft] = useState(field.lookupFormDefinitionId ?? '');
  const [deleteConfirmDraft, setDeleteConfirmDraft] = useState('');

  const [conditionFieldDraft, setConditionFieldDraft] = useState(field.visibleWhenFieldCode ?? '');
  const [conditionValuesDraft, setConditionValuesDraft] = useState<Set<string>>(() => {
    try {
      return new Set(field.visibleWhenValuesJson ? (JSON.parse(field.visibleWhenValuesJson) as string[]) : []);
    } catch {
      return new Set();
    }
  });

  function toggleConditionValueDraft(value: string) {
    setConditionValuesDraft((prev) => {
      const next = new Set(prev);
      if (next.has(value)) next.delete(value);
      else next.add(value);
      return next;
    });
  }

  const conditionFieldDraftDef = controllableFields.find((f) => f.code === conditionFieldDraft);
  const conditionFieldDraftOptions = conditionFieldDraftDef ? parseFieldOptions(conditionFieldDraftDef, t) : [];

  const [filterByFieldDraft, setFilterByFieldDraft] = useState(field.filterByFieldCode ?? '');

  function closePanel() {
    setPanel('none');
    setError(null);
    setCodeDraft(field.code);
    setTypeDraft(field.fieldType);
    setDeleteConfirmDraft('');
    setConditionFieldDraft(field.visibleWhenFieldCode ?? '');
    try {
      setConditionValuesDraft(
        new Set(field.visibleWhenValuesJson ? (JSON.parse(field.visibleWhenValuesJson) as string[]) : []),
      );
    } catch {
      setConditionValuesDraft(new Set());
    }
    setFilterByFieldDraft(field.filterByFieldCode ?? '');
  }

  async function run(action: () => Promise<void>, fallbackKey: string) {
    setBusy(true);
    setError(null);
    try {
      await action();
      await onChanged();
      setPanel('none');
      setEditingLabel(false);
      setDeleteConfirmDraft('');
    } catch (err) {
      setError({ err, fallbackKey });
    } finally {
      setBusy(false);
    }
  }

  // --- Safe: relabel. Saves on blur/Enter, no confirmation, no dialog. ---
  async function saveLabel() {
    const next = labelDraft.trim();
    if (!next || next === field.label) {
      setLabelDraft(field.label);
      setEditingLabel(false);
      return;
    }
    await run(() => api.forms.updateFieldLabel(token, formId, field.id, next), 'fieldEditor.relabelError');
  }

  const archived = !field.isActive;

  return (
    <div className={`border border-border rounded bg-panel px-3 py-2 text-sm ${archived ? 'opacity-60' : ''}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="min-w-0 flex-1">
          {editingLabel ? (
            <input
              autoFocus
              className={`${inputClass} max-w-xs`}
              value={labelDraft}
              disabled={busy}
              onChange={(e) => setLabelDraft(e.target.value)}
              onBlur={saveLabel}
              onKeyDown={(e) => {
                if (e.key === 'Enter') saveLabel();
                if (e.key === 'Escape') {
                  setLabelDraft(field.label);
                  setEditingLabel(false);
                }
              }}
            />
          ) : (
            <button
              type="button"
              onClick={() => !archived && setEditingLabel(true)}
              disabled={archived}
              className="text-start font-medium text-ink hover:underline disabled:no-underline"
              title={archived ? undefined : t('fieldEditor.clickToRelabel')}
            >
              {field.label}
            </button>
          )}
          <span className="ms-2 font-mono text-xs text-ink-soft">
            {field.code} · {t(`fieldType.${field.fieldType}`)}
            {field.isRequired ? ` · ${t('formBuilder.required')}` : ''}
            {archived ? ` · ${t('fieldEditor.archivedBadge')}` : ''}
          </span>
        </span>

        <span className="flex shrink-0 items-center gap-2">
          {/* Safe: reorder. Instant, metadata only. */}
          {!archived && (
            <>
              <button
                type="button"
                onClick={() => onMove(field.id, -1)}
                disabled={busy || isFirst}
                aria-label={t('fieldEditor.moveUp')}
                className="border border-border rounded px-1.5 text-xs text-ink-soft disabled:opacity-30"
              >
                ↑
              </button>
              <button
                type="button"
                onClick={() => onMove(field.id, 1)}
                disabled={busy || isLast}
                aria-label={t('fieldEditor.moveDown')}
                className="border border-border rounded px-1.5 text-xs text-ink-soft disabled:opacity-30"
              >
                ↓
              </button>
            </>
          )}

          {archived ? (
            <button
              type="button"
              onClick={() => run(() => api.forms.restoreField(token, formId, field.id), 'fieldEditor.restoreError')}
              disabled={busy}
              className="text-label uppercase tracking-wide text-accent hover:opacity-70"
            >
              {t('fieldEditor.restore')}
            </button>
          ) : (
            <>
              {controllableFields.length > 0 && (
                <button
                  type="button"
                  onClick={() => setPanel(panel === 'visibility' ? 'none' : 'visibility')}
                  disabled={busy}
                  className="text-label uppercase tracking-wide text-ink-soft hover:opacity-70"
                >
                  {field.visibleWhenFieldCode ? t('fieldEditor.visibilitySet') : t('fieldEditor.visibility')}
                </button>
              )}
              {field.fieldType === 'Lookup' && filterableFields.length > 0 && (
                <button
                  type="button"
                  onClick={() => setPanel(panel === 'lookup-filter' ? 'none' : 'lookup-filter')}
                  disabled={busy}
                  className="text-label uppercase tracking-wide text-ink-soft hover:opacity-70"
                >
                  {field.filterByFieldCode ? t('fieldEditor.lookupFilterSet') : t('fieldEditor.lookupFilter')}
                </button>
              )}
              <button
                type="button"
                onClick={() => setPanel(panel === 'code' ? 'none' : 'code')}
                disabled={busy}
                className="text-label uppercase tracking-wide text-ink-soft hover:opacity-70"
              >
                {t('fieldEditor.renameCode')}
              </button>
              <button
                type="button"
                onClick={() => setPanel(panel === 'type' ? 'none' : 'type')}
                disabled={busy}
                className="text-label uppercase tracking-wide text-ink-soft hover:opacity-70"
              >
                {t('fieldEditor.changeType')}
              </button>
              <button
                type="button"
                onClick={() => setPanel(panel === 'remove' ? 'none' : 'remove')}
                disabled={busy}
                className="text-label uppercase tracking-wide text-danger hover:opacity-70"
              >
                {t('common.remove')}
              </button>
            </>
          )}
        </span>
      </div>

      {/* --- Safe: conditional visibility. Metadata only, no column touched - unlike the
          panels below this needs no "here's the consequence" copy, just Save/Cancel. --- */}
      {panel === 'visibility' && (
        <div className="mt-3 border-t border-border pt-3">
          <p className="mb-2 text-sm text-ink-soft">{t('fieldEditor.visibilityDescription')}</p>
          <div className="space-y-2">
            <select
              className={`${inputClass} max-w-xs`}
              value={conditionFieldDraft}
              disabled={busy}
              onChange={(e) => {
                setConditionFieldDraft(e.target.value);
                setConditionValuesDraft(new Set());
              }}
            >
              <option value="">{t('fieldEditor.alwaysVisible')}</option>
              {controllableFields.map((f) => (
                <option key={f.code} value={f.code}>
                  {f.label}
                </option>
              ))}
            </select>

            {conditionFieldDraftDef && (
              <div className="flex flex-wrap gap-3">
                {conditionFieldDraftOptions.map((o) => (
                  <label key={o.value} className="flex items-center gap-1.5 text-sm text-ink">
                    <input
                      type="checkbox"
                      checked={conditionValuesDraft.has(o.value)}
                      onChange={() => toggleConditionValueDraft(o.value)}
                    />
                    {o.label}
                  </label>
                ))}
              </div>
            )}

            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={busy || (!!conditionFieldDraft && conditionValuesDraft.size === 0)}
                onClick={() =>
                  run(
                    () =>
                      api.forms.updateFieldVisibility(token, formId, field.id, {
                        visibleWhenFieldCode: conditionFieldDraft || null,
                        visibleWhenValuesJson: conditionFieldDraft ? JSON.stringify([...conditionValuesDraft]) : null,
                      }),
                    'fieldEditor.visibilityError',
                  )
                }
                className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
              >
                {t('common.save')}
              </button>
              <button type="button" onClick={closePanel} disabled={busy} className="text-sm text-ink-soft hover:opacity-70">
                {t('common.cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* --- Safe: filtered/cascading Lookup. Metadata only, no column touched - same
          no-confirmation-copy treatment as the visibility panel above. Only ever shown for a
          Lookup field. --- */}
      {panel === 'lookup-filter' && (
        <div className="mt-3 border-t border-border pt-3">
          <p className="mb-2 text-sm text-ink-soft">{t('fieldEditor.lookupFilterDescription')}</p>
          <div className="space-y-2">
            <select
              className={`${inputClass} max-w-xs`}
              value={filterByFieldDraft}
              disabled={busy}
              onChange={(e) => setFilterByFieldDraft(e.target.value)}
            >
              <option value="">{t('fieldEditor.noLookupFilter')}</option>
              {filterableFields.map((f) => (
                <option key={f.code} value={f.code}>
                  {f.label}
                </option>
              ))}
            </select>

            <div className="flex items-center gap-2">
              <button
                type="button"
                disabled={busy}
                onClick={() =>
                  run(
                    () => api.forms.updateFieldLookupFilter(token, formId, field.id, filterByFieldDraft || null),
                    'fieldEditor.lookupFilterError',
                  )
                }
                className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
              >
                {t('common.save')}
              </button>
              <button type="button" onClick={closePanel} disabled={busy} className="text-sm text-ink-soft hover:opacity-70">
                {t('common.cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* --- Risky 1: rename the code / physical column --- */}
      {panel === 'code' && (
        <div className="mt-3 border-t border-border pt-3">
          <p className="mb-2 text-sm text-ink-soft">
            {isLive ? t('fieldEditor.renameCodeConsequenceLive') : t('fieldEditor.renameCodeConsequenceDraft')}
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <input
              className={`${inputClass} max-w-xs font-mono`}
              value={codeDraft}
              disabled={busy}
              onChange={(e) => setCodeDraft(e.target.value)}
            />
            <button
              type="button"
              disabled={busy || !codeDraft.trim() || codeDraft.trim() === field.code}
              onClick={() =>
                run(() => api.forms.renameFieldCode(token, formId, field.id, codeDraft.trim()), 'fieldEditor.renameCodeError')
              }
              className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
            >
              {t('fieldEditor.confirmRenameCode', { from: field.code, to: codeDraft.trim() || '…' })}
            </button>
            <button type="button" onClick={closePanel} disabled={busy} className="text-sm text-ink-soft hover:opacity-70">
              {t('common.cancel')}
            </button>
          </div>
        </div>
      )}

      {/* --- Risky 2: change the field's type / column type --- */}
      {panel === 'type' && (
        <div className="mt-3 border-t border-border pt-3">
          <p className="mb-2 text-sm text-ink-soft">
            {isLive ? t('fieldEditor.changeTypeConsequenceLive') : t('fieldEditor.changeTypeConsequenceDraft')}
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <select
              className={`${inputClass} max-w-xs`}
              value={typeDraft}
              disabled={busy}
              onChange={(e) => setTypeDraft(e.target.value as FieldType)}
            >
              {FIELD_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(`fieldType.${type}`)}
                </option>
              ))}
            </select>
            {typeDraft === 'Lookup' && (
              <select
                className={`${inputClass} max-w-xs`}
                value={lookupTargetDraft}
                disabled={busy}
                onChange={(e) => setLookupTargetDraft(e.target.value)}
              >
                <option value="">{t('fieldEditor.chooseLookupTarget')}</option>
                {lookupTargets.map((f) => (
                  <option key={f.id} value={f.id}>
                    {f.name}
                  </option>
                ))}
              </select>
            )}
            <button
              type="button"
              disabled={
                busy ||
                typeDraft === field.fieldType ||
                (typeDraft === 'Lookup' && !lookupTargetDraft) ||
                // Dropdown needs an options list this row has no editor for - changing INTO
                // Dropdown would have nothing to send, so it's steered to the add-field form
                // rather than failing at the API with a confusing "options required".
                typeDraft === 'Dropdown'
              }
              onClick={() =>
                run(
                  () =>
                    api.forms.changeFieldType(token, formId, field.id, {
                      newFieldType: typeDraft,
                      optionsJson: null,
                      lookupFormDefinitionId: typeDraft === 'Lookup' ? lookupTargetDraft : null,
                    }),
                  'fieldEditor.changeTypeError',
                )
              }
              className="bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
            >
              {t('fieldEditor.confirmChangeType', {
                from: t(`fieldType.${field.fieldType}`),
                to: t(`fieldType.${typeDraft}`),
              })}
            </button>
            <button type="button" onClick={closePanel} disabled={busy} className="text-sm text-ink-soft hover:opacity-70">
              {t('common.cancel')}
            </button>
          </div>
          {typeDraft === 'Dropdown' && <p className="mt-2 text-xs text-ink-soft">{t('fieldEditor.dropdownTypeUnsupported')}</p>}
        </div>
      )}

      {/* --- Risky 3: remove. Two labelled outcomes, never one silent "delete". --- */}
      {panel === 'remove' && (
        <div className="mt-3 space-y-4 border-t border-border pt-3">
          <div>
            <p className="text-sm font-medium text-ink">{t('fieldEditor.archiveHeading')}</p>
            <p className="mt-1 text-sm text-ink-soft">
              {isLive ? t('fieldEditor.archiveConsequenceLive') : t('fieldEditor.archiveConsequenceDraft')}
            </p>
            <button
              type="button"
              onClick={() => run(() => api.forms.archiveField(token, formId, field.id), 'fieldEditor.archiveError')}
              disabled={busy}
              className="mt-2 bg-accent rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
            >
              {t('fieldEditor.archiveAction')}
            </button>
          </div>

          <div className="border-t border-border pt-3">
            <p className="text-sm font-medium text-danger">{t('fieldEditor.deleteHeading')}</p>
            <p className="mt-1 text-sm text-ink-soft">
              {isLive
                ? t('fieldEditor.deleteConsequenceLive', { code: field.code })
                : t('fieldEditor.deleteConsequenceDraft')}
            </p>
            {isLive && (
              <input
                className={`${inputClass} mt-2 max-w-xs font-mono`}
                placeholder={field.code}
                value={deleteConfirmDraft}
                disabled={busy}
                onChange={(e) => setDeleteConfirmDraft(e.target.value)}
                aria-label={t('fieldEditor.deleteConfirmLabel', { code: field.code })}
              />
            )}
            <button
              type="button"
              disabled={busy || (isLive && deleteConfirmDraft.trim() !== field.code)}
              onClick={() =>
                run(
                  () => api.forms.deleteField(token, formId, field.id, isLive ? deleteConfirmDraft.trim() : undefined),
                  'fieldEditor.deleteError',
                )
              }
              className="bg-danger mt-2 block rounded px-3 py-1.5 text-sm font-medium text-accent-ink disabled:opacity-50"
            >
              {t('fieldEditor.deleteAction')}
            </button>
          </div>

          <button type="button" onClick={closePanel} disabled={busy} className="text-sm text-ink-soft hover:opacity-70">
            {t('common.cancel')}
          </button>
        </div>
      )}

      {error && <p className="mt-2 text-sm text-danger">{error}</p>}
    </div>
  );
}
