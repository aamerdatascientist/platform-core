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

const PAGE_SIZE = 25;

export function FormView({ token }: FormViewProps) {
  const { t } = useTranslation();
  const { formId } = useParams<{ formId: string }>();

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [submissions, setSubmissions] = useState<DynamicRow[]>([]);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [selectedRecordId, setSelectedRecordId] = useState<string | null>(null);
  // Administrators can correct or remove a record: editingRecord swaps the form at the top
  // for one holding that record's values; confirmingDelete asks once before removing.
  const [editingRecord, setEditingRecord] = useState<DynamicRow | null>(null);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [recordNotice, setRecordNotice] = useState<string | null>(null);
  const [recordError, setRecordError] = useErrorMessage();
  const [error, setError] = useErrorMessage();

  // The first active Lookup field on a form is, by this platform's own convention (see
  // every daily-report form seeded via scripts/seed-*-forms.ps1), the record's scoping
  // reference - most commonly Project. Filtering submissions by it is a generic mechanism
  // driven by field metadata, not a hardcoded "Project" special case, so it works for any
  // form built the same way.
  const [scopeChoices, setScopeChoices] = useState<ScopeChoice[]>([]);
  const [scopeValue, setScopeValue] = useState<string>('');

  // Only administrators see what has already been submitted (the records list, and the
  // workflow/attachments panel that opens from it); everyone else just fills the form in.
  // Null until known, so nothing record-related is fetched or flashed for a non-admin. The
  // server enforces the same rule - this only keeps the page from asking.
  const [isAdmin, setIsAdmin] = useState<boolean | null>(null);
  useEffect(() => {
    api.auth
      .me(token)
      .then((me) => setIsAdmin(me.roles.includes('Administrator')))
      .catch(() => setIsAdmin(false));
  }, [token]);

  const scopeField = formDefinition?.publishedVersion?.fields.find(
    (f) => f.isActive && f.fieldType === 'Lookup' && f.lookupFormDefinitionId,
  );

  useEffect(() => {
    if (!formId) return;
    setSelectedRecordId(null);
    setEditingRecord(null);
    setPage(1);
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId, token]);

  useEffect(() => {
    if (!isAdmin || !scopeField?.lookupFormDefinitionId) {
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
  }, [scopeField?.lookupFormDefinitionId, token, isAdmin]);

  useEffect(() => {
    if (!formId || !isAdmin) return;
    loadSubmissions(formId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId, scopeValue, isAdmin, page]);

  // A different project filter starts again from the first page.
  useEffect(() => setPage(1), [scopeValue]);

  // Picking another record closes any open delete question. (Old notices are cleared in the
  // row click itself - clearing them here would also wipe "Record deleted.", since deleting
  // clears the selection.)
  useEffect(() => {
    setConfirmingDelete(false);
    setRecordError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedRecordId]);

  async function loadSubmissions(id: string) {
    if (!isAdmin) return;
    const filter = scopeField && scopeValue ? { fieldCode: scopeField.code, value: scopeValue } : undefined;
    try {
      const result = await api.submissions.list(token, id, page, PAGE_SIZE, filter);
      // Deleting the last record on a page leaves it empty - step back one.
      if (result.items.length === 0 && page > 1) {
        setPage(page - 1);
        return;
      }
      setSubmissions(result.items);
      setTotalCount(result.totalCount);
    } catch {
      setSubmissions([]);
      setTotalCount(0);
    }
  }

  async function deleteSelectedRecord() {
    if (!formId || !selectedRecordId) return;
    setDeleting(true);
    setRecordError(null);
    try {
      await api.submissions.delete(token, formId, selectedRecordId);
      setSelectedRecordId(null);
      setEditingRecord(null);
      setRecordNotice(t('formView.recordDeleted'));
      await loadSubmissions(formId);
    } catch (err) {
      setRecordError({ err, fallbackKey: 'formView.deleteFailed' });
    } finally {
      setDeleting(false);
      setConfirmingDelete(false);
    }
  }

  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!formDefinition || isAdmin === null)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="text-xs uppercase tracking-wide text-ink-soft">{t('common.loading')}</span>
      </div>
    );

  const attachmentFields = formDefinition.publishedVersion?.fields.filter((f) => f.isActive && f.fieldType === 'Attachment') ?? [];

  if (!isAdmin) {
    return (
      <div className="max-w-3xl space-y-8">
        <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{formDefinition.name}</h2>
        <FormRenderer token={token} formDefinition={formDefinition} onSubmitted={() => {}} />
      </div>
    );
  }

  return (
    <div className="grid max-w-6xl grid-cols-1 gap-6 lg:grid-cols-[1fr_320px]">
      <div className="space-y-8">
        <h2 className="font-display text-xl font-semibold uppercase tracking-[0.07em] text-ink">{formDefinition.name}</h2>

        {editingRecord ? (
          <section className="rounded border border-accent p-4">
            <h3 className="mb-4 text-label font-medium uppercase tracking-wider text-accent">{t('formView.editingRecord')}</h3>
            <FormRenderer
              key={`edit-${editingRecord.id}`}
              token={token}
              formDefinition={formDefinition}
              editRecord={editingRecord}
              onCancelEdit={() => setEditingRecord(null)}
              onSubmitted={() => {
                setEditingRecord(null);
                setRecordNotice(t('formView.recordSaved'));
                if (formId) loadSubmissions(formId);
              }}
            />
          </section>
        ) : (
          <FormRenderer
            token={token}
            formDefinition={formDefinition}
            onSubmitted={() => formId && loadSubmissions(formId)}
          />
        )}

        <div>
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-label font-medium uppercase tracking-wider text-ink-soft">
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
            onRowClick={(id) => {
              setSelectedRecordId(id);
              setRecordNotice(null);
            }}
            selectedRecordId={selectedRecordId}
            page={page}
            pageSize={PAGE_SIZE}
            totalCount={totalCount}
            onPageChange={setPage}
          />
        </div>
      </div>

      <div className="space-y-4 lg:sticky lg:top-8 lg:self-start">
        {recordNotice && (
          <p role="status" className="rounded border border-success p-3 text-sm text-ink">
            {recordNotice}
          </p>
        )}
        {selectedRecordId ? (
          <>
            <div className="rounded border border-border p-4">
              <h3 className="mb-3 text-label font-medium uppercase tracking-wider text-ink-soft">{t('formView.recordActions')}</h3>
              {confirmingDelete ? (
                <div className="space-y-3">
                  <p className="text-sm text-ink">{t('formView.confirmDelete')}</p>
                  <div className="flex gap-2">
                    <button
                      type="button"
                      onClick={deleteSelectedRecord}
                      disabled={deleting}
                      className="rounded bg-danger px-3 py-1.5 text-sm font-medium text-panel hover:opacity-90 disabled:opacity-50"
                    >
                      {deleting ? t('formView.deleting') : t('formView.deleteRecord')}
                    </button>
                    <button
                      type="button"
                      onClick={() => setConfirmingDelete(false)}
                      disabled={deleting}
                      className="rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:bg-bg"
                    >
                      {t('common.cancel')}
                    </button>
                  </div>
                </div>
              ) : (
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={() => {
                      const record = submissions.find((r) => r.id === selectedRecordId);
                      if (record) {
                        setEditingRecord(record);
                        setRecordNotice(null);
                        window.scrollTo({ top: 0, behavior: 'smooth' });
                        document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' });
                      }
                    }}
                    className="rounded border border-border bg-panel px-3 py-1.5 text-sm text-ink hover:bg-bg"
                  >
                    {t('formView.editRecord')}
                  </button>
                  <button
                    type="button"
                    onClick={() => setConfirmingDelete(true)}
                    className="rounded border border-danger bg-panel px-3 py-1.5 text-sm text-danger hover:bg-bg"
                  >
                    {t('formView.deleteRecord')}
                  </button>
                </div>
              )}
              {recordError && <p className="mt-2 text-sm text-danger">{recordError}</p>}
            </div>
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
