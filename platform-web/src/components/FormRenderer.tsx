import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';
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
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    setValues({});
    setAttachmentFiles({});
    setError(null);
    setFieldErrors({});

    const lookupFields = activeFields.filter((f) => f.fieldType === 'Lookup' && f.lookupFormDefinitionId);
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

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    const missing: Record<string, string> = {};
    activeFields.forEach((f) => {
      if (!f.isRequired) return;
      const isMissing = f.fieldType === 'Attachment' ? !attachmentFiles[f.code] : !values[f.code];
      if (isMissing) missing[f.code] = t('formRenderer.fieldRequired');
    });
    if (Object.keys(missing).length > 0) {
      setFieldErrors(missing);
      return;
    }
    setFieldErrors({});

    setSubmitting(true);
    try {
      const payload: Record<string, unknown> = {};
      activeFields.forEach((f) => {
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

      const attachmentFieldsWithFiles = activeFields.filter(
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
          setError(
            t('formRenderer.partialUploadFailure', {
              count: failedLabels.length,
              files: failedLabels.join(', '),
            }),
          );
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
        setError(err instanceof ApiError ? err.message : t('formRenderer.submitFailed'));
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (!formDefinition.publishedVersion) {
    return <p className="text-sm text-ink-muted">{t('formRenderer.noPublishedVersion')}</p>;
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {activeFields.map((field) => (
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
        />
      ))}

      {error && <p className="text-sm text-clay">{error}</p>}

      <button
        type="submit"
        disabled={submitting}
        className="w-full bg-ink px-4 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
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
}: {
  field: FieldDefinitionDto;
  value: string;
  onChange: (v: string) => void;
  selectedFileName?: string;
  onFileChange: (file: File | null) => void;
  error?: string;
  options: DropdownOption[];
  lookupChoices?: LookupChoice[];
}) {
  const { t } = useTranslation();
  const baseClass =
    'w-full border border-line px-3 py-2 text-sm focus:border-ink focus:outline-none';

  return (
    <div>
      <label className="mb-1 block text-sm font-medium text-ink">
        {field.label}
        {field.isRequired && <span className="text-clay"> *</span>}
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
            <p className="mt-1 text-xs text-ink-muted">{t('formRenderer.selected', { name: selectedFileName })}</p>
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
        <select className={baseClass} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">{lookupChoices ? t('common.select') : t('formRenderer.loadingOptions')}</option>
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

      {error && <p className="mt-1 text-xs text-clay">{error}</p>}
    </div>
  );
}
