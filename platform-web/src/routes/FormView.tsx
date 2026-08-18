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

export function FormView({ token }: FormViewProps) {
  const { t } = useTranslation();
  const { formId } = useParams<{ formId: string }>();

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [submissions, setSubmissions] = useState<DynamicRow[]>([]);
  const [selectedRecordId, setSelectedRecordId] = useState<string | null>(null);
  const [error, setError] = useErrorMessage();

  useEffect(() => {
    if (!formId) return;
    setSelectedRecordId(null);
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

  async function loadSubmissions(id: string) {
    const page = await api.submissions.list(token, id);
    setSubmissions(page.items);
  }

  if (error) return <p className="text-sm text-clay">{error}</p>;
  if (!formDefinition)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-muted">{t('common.loading')}</span>
      </div>
    );

  const attachmentFields = formDefinition.publishedVersion?.fields.filter((f) => f.isActive && f.fieldType === 'Attachment') ?? [];

  return (
    <div className="grid max-w-6xl grid-cols-1 gap-6 lg:grid-cols-[1fr_320px]">
      <div className="space-y-8">
        <h2 className="font-display text-xl font-semibold text-ink">{formDefinition.name}</h2>

        <FormRenderer
          token={token}
          formDefinition={formDefinition}
          onSubmitted={() => formId && loadSubmissions(formId)}
        />

        <div>
          <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">
            {t('formView.recordsHeading')}
          </h3>
          <SubmissionsTable
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
          <div className="border border-dashed border-line p-4 text-sm text-ink-muted">
            {t('formView.selectRecordPrompt')}
            {attachmentFields.length > 0 ? t('formView.andAttachments') : ''}.
          </div>
        )}
      </div>
    </div>
  );
}
