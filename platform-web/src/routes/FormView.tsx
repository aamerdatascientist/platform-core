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
  if (!formDefinition) return <p className="text-xs uppercase tracking-wide text-ink-muted">Loading…</p>;

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
            Records - select one for workflow status
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
            Select a record to see its workflow status{attachmentFields.length > 0 ? ' and attachments' : ''}.
          </div>
        )}
      </div>
    </div>
  );
}
