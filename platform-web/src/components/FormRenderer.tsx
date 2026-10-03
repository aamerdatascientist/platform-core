import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { DropdownOption, FieldDefinitionDto, FormDefinitionDto } from '../types';

interface FormRendererProps {
  token: string;
  formDefinition: FormDefinitionDto;
  onSubmitted?: () => void;
}

interface LookupChoice {
  id: string;
  label: string;
}

// A sentinel (rather than the resolved text) for the one per-field message this component
// itself generates, so FieldInput can re-translate it live on a language switch - unlike
// the other values fieldErrors can hold, which come from the backend's SubmissionValueValidator
// already resolved to English text and (per FormRenderer's own submit handler) intentionally
// left untranslated.
const FIELD_REQUIRED_SENTINEL = '__FIELD_REQUIRED__';

/**
 * Renders a submission form for ANY published form, driven entirely by its field
 * metadata - this is the actual "low-code" part of the platform. No per-form code exists
 * or should ever need to exist here.
 *
 * Attachments are picked here, inline with everything else, but physically uploaded in a
 * second step immediately after the record is created - a record's Id has to exist before
 * a file can be associated with it (see FileMetadata.RecordId). That ordering is invisible
 * to the person filling the form: one "Submit" click does both, in sequence. If the record
 * saves but a file fails to upload, that's surfaced honestly as a partial success, not a
 * generic failure - the data isn't lost, just that one file needs retrying afterward via
 * the Attachments panel.
 *
 * Known simplification: Lookup fields need a human-readable label for their dropdown,
 * but FieldDefinitionDto has no designated "display field" for the target form yet - the
 * backend doesn't expose one. This convention-guesses the first ShortText field on the
 * target form's published version. Fine for now; formalizing a real DisplayFieldCode on
 * FormDefinition (backend change) would remove the guesswork.
 */
export function FormRenderer({ token, formDefinition, onSubmitted }: FormRendererProps) {
  const { t } = useTranslation();
  const activeFields = useMemo(
    () => formDefinition.publishedVersion?.fields.filter((f) => f.isActive) ?? [],
    [formDefinition],
  );

  const [values, setValues] = useState<Record<string, string>>({});
  const [attachmentFiles, setAttachmentFiles] = useState<Record<string, File>>({});
  const [lookupChoices, setLookupChoices] = useState<Record<string, LookupChoice[]>>({});
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useErrorMessage();
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    setValues({});
    setAttachmentFiles({});
    setError(null);
    setFieldErrors({});

    // Filtered (cascading) Lookups are excluded here - they have no single fixed choice list
    // to fetch once at load, since their candidates depend on another field's current value.
    // The effect below handles those, reactively, keyed off that value instead of form load.
    const lookupFields = activeFields.filter(
      (f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId && !f.filterByFieldCode,
    );
    const uniqueTargets = [...new Set(lookupFields.map((f) => f.lookupFormDefinitionId as string))];

    uniqueTargets.forEach(async (targetFormId) => {
      try {
        const targetDef = await api.forms.get(token, targetFormId);
        const displayField = targetDef.publishedVersion?.fields.find(
          (f) => f.isActive && f.fieldType === 'ShortText',
        );
        const submissions = await api.submissions.list(token, targetFormId, 1, 200);
        const choices: LookupChoice[] = submissions.items.map((row) => ({
          id: row.id,
          label: displayField ? String(row.values[displayField.code] ?? row.id) : row.id,
        }));

        setLookupChoices((prev) => {
          const next = { ...prev };
          lookupFields
            .filter((f) => f.lookupFormDefinitionId === targetFormId)
            .forEach((f) => {
              next[f.code] = choices;
            });
          return next;
        });
      } catch {
        // A failed lookup fetch shouldn't block the rest of the form from rendering -
        // that field just won't have options, and the required-field check below will
        // catch it if the user tries to submit without picking one.
      }
    });
    // activeFields is derived from formDefinition each render via useMemo, not a stable
    // reference - depend on the form's id/version instead to avoid re-fetching on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formDefinition.id, formDefinition.publishedVersion?.id, token]);

  const filteredLookupFields = useMemo(
    () => activeFields.filter((f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId && f.filterByFieldCode),
    [activeFields],
  );

  // Keyed by a plain string, not the fields/values objects themselves, so this only re-runs
  // when a FILTER SOURCE field's value actually changes - not on every keystroke in an
  // unrelated field on the form.
  const filterSourceValuesKey = filteredLookupFields
    .map((f) => `${f.code}=${values[f.filterByFieldCode as string] ?? ''}`)
    .join('&');

  // Re-fetches each filtered (cascading) Lookup's choices whenever ITS OWN filter-source
  // field's current value changes - e.g. "zone" filtered by "project" re-fetches the moment
  // a project is picked or changed, so it only ever offers zones belonging to whichever
  // project is currently selected, never every zone from every project.
  useEffect(() => {
    filteredLookupFields.forEach(async (field) => {
      const sourceValue = values[field.filterByFieldCode as string];
      if (!sourceValue) {
        // No filter value chosen yet (e.g. no project picked) - show nothing rather than
        // every row from every project, which is exactly what this feature exists to avoid.
        setLookupChoices((prev) => ({ ...prev, [field.code]: [] }));
        return;
      }
      try {
        const targetFormId = field.lookupFormDefinitionId as string;
        const targetDef = await api.forms.get(token, targetFormId);
        const displayField = targetDef.publishedVersion?.fields.find(
          (f) => f.isActive && f.fieldType === 'ShortText',
        );
        const submissions = await api.submissions.list(token, targetFormId, 1, 200, {
          fieldCode: field.filterByFieldCode as string,
          value: sourceValue,
        });
        const choices: LookupChoice[] = submissions.items.map((row) => ({
          id: row.id,
          label: displayField ? String(row.values[displayField.code] ?? row.id) : row.id,
        }));
        setLookupChoices((prev) => ({ ...prev, [field.code]: choices }));
      } catch {
        // Same philosophy as the unfiltered fetch above - a failed fetch just leaves this
        // field without options rather than blocking the rest of the form.
      }
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterSourceValuesKey, token]);

  function setValue(code: string, value: string) {
    setValues((prev) => ({ ...prev, [code]: value }));
  }

  function setAttachmentFile(code: string, file: File | null) {
    setAttachmentFiles((prev) => {
      const next = { ...prev };
      if (file) next[code] = file;
      else delete next[code];
      return next;
    });
  }

  function parseOptions(optionsJson: string | null): DropdownOption[] {
    if (!optionsJson) return [];
    try {
      return JSON.parse(optionsJson) as DropdownOption[];
    } catch {
      return [];
    }
  }

  // Mirrors the backend's SubmissionValueValidator.IsFieldVisible exactly - a field with no
  // condition is always visible; otherwise it's visible only when its controlling field's
  // CURRENT value (from this component's own `values` state, not the backend) is one of
  // visibleWhenValuesJson. Keeping these two implementations in sync is what makes the "same
  // 3-question shape per phase" branching form actually work: this is what hides/shows the
  // right block as the person picks a Phase, and the backend copy is what still enforces it
  // if a submission ever bypasses this UI (direct API use, a stale cached form, etc.).
  function isFieldVisible(field: FieldDefinitionDto): boolean {
    if (!field.visibleWhenFieldCode) return true;
    const controllingValue = values[field.visibleWhenFieldCode];
    if (controllingValue === undefined) return false;
    try {
      const allowed = JSON.parse(field.visibleWhenValuesJson ?? '[]') as string[];
      return allowed.includes(controllingValue);
    } catch {
      return false;
    }
  }

  const visibleFields = useMemo(() => activeFields.filter(isFieldVisible), [activeFields, values]);

  // When a controlling field's value changes and that hides a field that previously had a
  // value, drop the stale value from state too - otherwise switching Phase from Excavation to
  // Foundation and back would silently resubmit a leftover zone value alongside the new
  // footing one, even though the zone input is no longer shown. Comparing against
  // visibleFields (not activeFields) is what limits this to fields that just became hidden.
  useEffect(() => {
    const hiddenCodesWithValues = activeFields
      .filter((f) => !isFieldVisible(f) && values[f.code] !== undefined)
      .map((f) => f.code);
    if (hiddenCodesWithValues.length === 0) return;
    setValues((prev) => {
      const next = { ...prev };
      hiddenCodesWithValues.forEach((code) => delete next[code]);
      return next;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [values]);

  // Same idea, for a filtered Lookup whose filter SOURCE field just changed value: its
  // previously-picked value almost certainly isn't one of the new filtered candidates (e.g.
  // a zone that belonged to the OLD project), so it's cleared too - rather than left silently
  // referencing a row the new filter wouldn't have offered. Deliberately keyed only on
  // filterSourceValuesKey (not `values`), with the mount-skip below, so this never fires just
  // because the user picked a zone itself (which also changes `values`, but isn't a source
  // change) - only an actual change in a SOURCE field's value should clear its dependents.
  const previousFilterSourceValuesKey = useRef(filterSourceValuesKey);
  useEffect(() => {
    if (previousFilterSourceValuesKey.current === filterSourceValuesKey) return;
    previousFilterSourceValuesKey.current = filterSourceValuesKey;

    const staleCodes = filteredLookupFields.filter((f) => values[f.code] !== undefined).map((f) => f.code);
    if (staleCodes.length === 0) return;
    setValues((prev) => {
      const next = { ...prev };
      staleCodes.forEach((code) => delete next[code]);
      return next;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filterSourceValuesKey]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const missing: Record<string, string> = {};
    visibleFields.forEach((f) => {
      if (!f.isRequired) return;
      const isMissing = f.fieldType === 'Attachment' ? !attachmentFiles[f.code] : !values[f.code];
      if (isMissing) missing[f.code] = FIELD_REQUIRED_SENTINEL;
    });
    if (Object.keys(missing).length > 0) {
      setFieldErrors(missing);
      return;
    }
    setFieldErrors({});

    setSubmitting(true);
    try {
      const payload: Record<string, unknown> = {};
      visibleFields.forEach((f) => {
        if (f.fieldType === 'Attachment') return; // never part of the JSON body - no physical column
        const raw = values[f.code];
        if (raw === undefined || raw === '') {
          payload[f.code] = null;
        } else if (f.fieldType === 'Number' || f.fieldType === 'Decimal') {
          payload[f.code] = Number(raw);
        } else if (f.fieldType === 'Boolean') {
          payload[f.code] = raw === 'true';
        } else {
          payload[f.code] = raw;
        }
      });

      const { id: recordId } = await api.submissions.submit(token, formDefinition.id, payload);

      const attachmentFieldsWithFiles = visibleFields.filter(
        (f) => f.fieldType === 'Attachment' && attachmentFiles[f.code],
      );

      if (attachmentFieldsWithFiles.length > 0) {
        const results = await Promise.allSettled(
          attachmentFieldsWithFiles.map((f) =>
            api.files.upload(token, formDefinition.id, recordId, f.code, attachmentFiles[f.code]),
          ),
        );
        const failedLabels = results
          .map((r, i) => (r.status === 'rejected' ? attachmentFieldsWithFiles[i].label : null))
          .filter((label): label is string => label !== null);

        if (failedLabels.length > 0) {
          // The record itself saved fine - only the file(s) failed. Say so plainly rather
          // than implying the whole submission failed, since the data wasn't lost.
          setError({
            key: 'formRenderer.partialUploadFailure',
            params: { count: failedLabels.length, files: failedLabels.join(', ') },
          });
        }
      }

      setValues({});
      setAttachmentFiles({});
      onSubmitted?.();
    } catch (err) {
      if (err instanceof ApiError && err.errors) {
        // Backend keys these by field.code exactly (see SubmissionValueValidator) - no
        // translation needed, just take the first message per field for display.
        const perField: Record<string, string> = {};
        Object.entries(err.errors).forEach(([code, messages]) => {
          if (messages.length > 0) perField[code] = messages[0];
        });
        setFieldErrors(perField);
        setError(null);
      } else {
        setError({ err, fallbackKey: 'formRenderer.submitFailed' });
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (!formDefinition.publishedVersion) {
    return <p className="text-sm text-ink-soft">{t('formRenderer.noPublishedVersion')}</p>;
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {visibleFields.map((field) => (
        <FieldInput
          key={field.id}
          field={field}
          value={values[field.code] ?? ''}
          onChange={(v) => setValue(field.code, v)}
          selectedFileName={attachmentFiles[field.code]?.name}
          onFileChange={(file) => setAttachmentFile(field.code, file)}
          error={fieldErrors[field.code]}
          options={parseOptions(field.optionsJson)}
          lookupChoices={lookupChoices[field.code]}
          // Only set for a filtered Lookup whose filter-source field has no value yet - lets
          // FieldInput show "pick X first" instead of a plain, unexplained empty dropdown.
          filterWaitingOnLabel={
            field.filterByFieldCode && !values[field.filterByFieldCode]
              ? activeFields.find((f) => f.code === field.filterByFieldCode)?.label ?? field.filterByFieldCode
              : undefined
          }
        />
      ))}

      {error && <p className="text-sm text-danger">{error}</p>}

      <button
        type="submit"
        disabled={submitting}
        className="w-full bg-accent rounded px-4 py-2 text-sm font-medium text-accent-ink transition-opacity hover:opacity-90 disabled:opacity-50"
      >
        {submitting ? t('formRenderer.submitting') : t('formRenderer.submit')}
      </button>
    </form>
  );
}

function FieldInput({
  field,
  value,
  onChange,
  selectedFileName,
  onFileChange,
  error,
  options,
  lookupChoices,
  filterWaitingOnLabel,
}: {
  field: FieldDefinitionDto;
  value: string;
  onChange: (v: string) => void;
  selectedFileName?: string;
  onFileChange: (file: File | null) => void;
  error?: string;
  options: DropdownOption[];
  lookupChoices?: LookupChoice[];
  filterWaitingOnLabel?: string;
}) {
  const { t } = useTranslation();
  const baseClass =
    'w-full border border-border rounded bg-bg px-3 py-2 text-sm focus:border-accent focus:outline-none';

  return (
    <div>
      <label className="mb-1 block text-sm font-medium text-ink">
        {field.label}
        {field.isRequired && <span className="text-danger"> *</span>}
      </label>

      {field.fieldType === 'Attachment' ? (
        <div>
          <input
            type="file"
            accept="image/jpeg,image/png,image/heic,image/webp,application/pdf"
            onChange={(e) => onFileChange(e.target.files?.[0] ?? null)}
            className="text-sm"
          />
          {selectedFileName && (
            <p className="mt-1 text-xs text-ink-soft">{t('formRenderer.selected', { name: selectedFileName })}</p>
          )}
        </div>
      ) : field.fieldType === 'LongText' ? (
        <textarea className={baseClass} rows={3} value={value} onChange={(e) => onChange(e.target.value)} />
      ) : field.fieldType === 'Boolean' ? (
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">{t('common.select')}</option>
          <option value="true">{t('common.yes')}</option>
          <option value="false">{t('common.no')}</option>
        </select>
      ) : field.fieldType === 'Dropdown' ? (
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">{t('common.select')}</option>
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      ) : field.fieldType === 'Lookup' ? (
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)} disabled={!!filterWaitingOnLabel}>
          <option value="">
            {filterWaitingOnLabel
              ? t('formRenderer.chooseFilterSourceFirst', { label: filterWaitingOnLabel })
              : lookupChoices
                ? t('common.select')
                : t('formRenderer.loadingOptions')}
          </option>
          {(lookupChoices ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.label}
            </option>
          ))}
        </select>
      ) : field.fieldType === 'DateTime' ? (
        <input type="date" className={baseClass} value={value} onChange={(e) => onChange(e.target.value)} />
      ) : field.fieldType === 'Number' || field.fieldType === 'Decimal' ? (
        <input
          type="number"
          step={field.fieldType === 'Decimal' ? '0.01' : '1'}
          className={baseClass}
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
      ) : (
        <input type="text" className={baseClass} value={value} onChange={(e) => onChange(e.target.value)} />
      )}

      {error && (
        <p className="mt-1 text-xs text-danger">
          {error === FIELD_REQUIRED_SENTINEL ? t('formRenderer.fieldRequired') : error}
        </p>
      )}
    </div>
  );
}
