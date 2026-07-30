import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { AttachmentsPanel } from '../components/AttachmentsPanel';
import { FormRenderer } from '../components/FormRenderer';
import { SubmissionsTable } from '../components/SubmissionsTable';
import { WorkflowPanel } from '../components/WorkflowPanel';
import type { DynamicRow, FormDefinitionDto } from '../types';

interface FormViewProps {
  token: string;
}

export function FormView({ token }: FormViewProps) {
  const { formId } = useParams<{ formId: string }>();

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [submissions, setSubmissions] = useState<DynamicRow[]>([]);
  const [selectedRecordId, setSelectedRecordId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!formId) return;
    setSelectedRecordId(null);
    setError(null);
    api.forms
      .get(token, formId)
      .then(setFormDefinition)
      .catch((err) => {
        setFormDefinition(null);
        setError(err instanceof ApiError ? err.message : 'Could not load that form.');
      });
    loadSubmissions(formId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId, token]);

  async function loadSubmissions(id: string) {
    const page = await api.submissions.list(token, id);
    setSubmissions(page.items);
  }

  if (error) return <p className="text-sm text-clay">{error}</p>;
  if (!formDefinition) return <p className="font-mono text-xs uppercase tracking-wide text-ink-muted">Loading…</p>;

  return (
    <div className="max-w-2xl space-y-8">
      <h2 className="font-display text-xl font-semibold text-ink">{formDefinition.name}</h2>

      <FormRenderer
        token={token}
        formDefinition={formDefinition}
        onSubmitted={() => formId && loadSubmissions(formId)}
      />

      <div>
        <h3 className="mb-3 font-mono text-[11px] font-medium uppercase tracking-wider text-ink-muted">
          Records - select one for workflow status
        </h3>
        <SubmissionsTable
          fields={formDefinition.publishedVersion?.fields ?? []}
          rows={submissions}
          onRowClick={setSelectedRecordId}
          selectedRecordId={selectedRecordId}
        />
      </div>

      {selectedRecordId && (
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
            attachmentFields={formDefinition.publishedVersion?.fields.filter((f) => f.isActive && f.fieldType === 'Attachment') ?? []}
          />
        </>
      )}
    </div>
  );
}
