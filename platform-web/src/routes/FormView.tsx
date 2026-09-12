import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import { AttachmentsPanel } from '../components/AttachmentsPanel';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { FormRenderer } from '../components/FormRenderer';
import { SubmissionsTable } from '../components/SubmissionsTable';
import { WorkflowPanel } from '../components/WorkflowPanel';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { DynamicRow, FormDefinitionDto } from '../types';

interface FormViewProps {
  token: string;
}

interface ScopeChoice {
  id: string;
  label: string;
}

export function FormView({ token }: FormViewProps) {
  const { t } = useTranslation();
  const { formId } = useParams<{ formId: string }>();

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [submissions, setSubmissions] = useState<DynamicRow[]>([]);
  const [selectedRecordId, setSelectedRecordId] = useState<string | null>(null);
  const [error, setError] = useErrorMessage();

  // The first active Lookup field on a form is, by this platform's own convention (see
  // every daily-report form seeded via scripts/seed-*-forms.ps1), the record's scoping
  // reference - most commonly Project. Filtering submissions by it is a generic mechanism
  // driven by field metadata, not a hardcoded "Project" special case, so it works for any
  // form built the same way.
  const [scopeChoices, setScopeChoices] = useState<ScopeChoice[]>([]);
  const [scopeValue, setScopeValue] = useState<string>('');

  const scopeField = formDefinition?.publishedVersion?.fields.find(
    (f) => f.isActive && f.fieldType === 'Lookup' && f.lookupFormDefinitionId,
  );

  useEffect(() => {
    if (!formId) return;
    setSelectedRecordId(null);
    setScopeValue('');
    setScopeChoices([]);
    setError(null);
    api.forms
      .get(token, formId)
      .then(setFormDefinition)
      .catch((err) => {
        setFormDefinition(null);
        setError({ err, fallbackKey: 'formView.loadError' });
      });
    loadSubmissions(formId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId, token]);

  useEffect(() => {
    if (!scopeField?.lookupFormDefinitionId) {
      setScopeChoices([]);
      return;
    }
    const targetFormId = scopeField.lookupFormDefinitionId;
    (async () => {
      try {
        const targetDef = await api.forms.get(token, targetFormId);
        const displayField = targetDef.publishedVersion?.fields.find((f) => f.isActive && f.fieldType === 'ShortText');
        const page = await api.submissions.list(token, targetFormId, 1, 200);
        setScopeChoices(
          page.items.map((row) => ({
            id: row.id,
            label: displayField ? String(row.values[displayField.code] ?? row.id) : row.id,
          })),
        );
      } catch {
        // No scope choices just means the filter dropdown falls back to "All" only -
        // shouldn't block the rest of the form/table from rendering.
        setScopeChoices([]);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeField?.lookupFormDefinitionId, token]);

  useEffect(() => {
    if (!formId) return;
    loadSubmissions(formId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scopeValue]);

  async function loadSubmissions(id: string) {
    const filter = scopeField && scopeValue ? { fieldCode: scopeField.code, value: scopeValue } : undefined;
    const page = await api.submissions.list(token, id, 1, 25, filter);
    setSubmissions(page.items);
  }

  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!formDefinition)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );

  const attachmentFields = formDefinition.publishedVersion?.fields.filter((f) => f.isActive && f.fieldType === 'Attachment') ?? [];

  return (
    <div className="grid max-w-6xl grid-cols-1 gap-6 lg:grid-cols-[1fr_320px]">
      <div className="space-y-8">
        <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{formDefinition.name}</h2>

        <FormRenderer
          token={token}
          formDefinition={formDefinition}
          onSubmitted={() => formId && loadSubmissions(formId)}
        />

        <div>
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-[11px] font-medium uppercase tracking-wider text-ink-soft">
              {t('formView.recordsHeading')}
            </h3>
            {scopeField && (
              <label className="flex items-center gap-2 text-xs text-ink-soft">
                {scopeField.label}
                <select
                  className="rounded border border-border bg-bg px-2 py-1 text-ink"
                  value={scopeValue}
                  onChange={(e) => setScopeValue(e.target.value)}
                >
                  <option value="">{t('formView.allScopes')}</option>
                  {scopeChoices.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.label}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>
          <SubmissionsTable
            token={token}
            fields={formDefinition.publishedVersion?.fields ?? []}
            rows={submissions}
            onRowClick={setSelectedRecordId}
            selectedRecordId={selectedRecordId}
          />
        </div>
      </div>

      <div className="space-y-4 lg:sticky lg:top-8 lg:self-start">
        {selectedRecordId ? (
          <>
            <WorkflowPanel
              key={`workflow-${selectedRecordId}`}
              token={token}
              recordId={selectedRecordId}
              onChanged={() => formId && loadSubmissions(formId)}
            />
            <AttachmentsPanel
              key={`attachments-${selectedRecordId}`}
              token={token}
              formId={formId!}
              recordId={selectedRecordId}
              attachmentFields={attachmentFields}
            />
          </>
        ) : (
          <div className="border border-dashed border-border rounded p-4 text-sm text-ink-soft">
            {t('formView.selectRecordPrompt')}
            {attachmentFields.length > 0 ? t('formView.andAttachments') : ''}.
          </div>
        )}
      </div>
    </div>
  );
}
