import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { AddFieldForm } from '../components/AddFieldForm';
import type { FormDefinitionDto, FormSummaryDto } from '../types';

export function FormBuilder({ token }: { token: string }) {
  const { formId } = useParams<{ formId: string }>();
  const navigate = useNavigate();

  const [formDefinition, setFormDefinition] = useState<FormDefinitionDto | null>(null);
  const [allForms, setAllForms] = useState<FormSummaryDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);

  useEffect(() => {
    if (!formId) return;
    load();
    api.forms.list(token).then(setAllForms);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId]);

  async function load() {
    if (!formId) return;
    try {
      setFormDefinition(await api.forms.get(token, formId));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load this form.');
    }
  }

  async function handleRemoveField(fieldId: string) {
    if (!formId) return;
    try {
      await api.forms.removeField(token, formId, fieldId);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not remove that field.');
    }
  }

  async function handlePublish() {
    if (!formId) return;
    setPublishing(true);
    setError(null);
    try {
      await api.forms.publish(token, formId);
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not publish this form.');
    } finally {
      setPublishing(false);
    }
  }

  if (error && !formDefinition) return <p className="text-sm text-clay">{error}</p>;
  if (!formDefinition) return <p className="font-mono text-xs uppercase tracking-wide text-ink-muted">Loading…</p>;

  const isDraft = formDefinition.status === 'Draft';
  const fields = (isDraft ? formDefinition.draftVersion?.fields : formDefinition.publishedVersion?.fields) ?? [];

  return (
    <div className="max-w-2xl space-y-8">
      <div>
        <div className="flex items-center gap-3">
          <h2 className="font-display text-xl font-semibold text-ink">{formDefinition.name}</h2>
          <span
            className={`border px-2 py-0.5 font-mono text-[11px] uppercase tracking-wider ${
              isDraft ? 'border-signal-dark text-signal-dark' : 'border-moss text-moss'
            }`}
          >
            {formDefinition.status}
          </span>
        </div>
        <p className="mt-1 font-mono text-xs text-ink-muted">
          {formDefinition.code} · {formDefinition.moduleName}
        </p>
      </div>

      {!isDraft && (
        <p className="border border-line bg-white p-3 text-sm text-ink-muted">
          This form is published - editing published forms isn't supported yet. Fields below are read-only.
        </p>
      )}

      <div>
        <h3 className="mb-3 font-mono text-[11px] font-medium uppercase tracking-wider text-ink-muted">
          Fields ({fields.length})
        </h3>
        <div className="space-y-1">
          {fields.map((f) => (
            <div key={f.id} className="flex items-center justify-between border border-line bg-white px-3 py-2 text-sm">
              <span>
                <span className="font-medium text-ink">{f.label}</span>
                <span className="ml-2 font-mono text-xs text-ink-muted">
                  {f.code} · {f.fieldType}
                  {f.isRequired ? ' · required' : ''}
                </span>
              </span>
              {isDraft && (
                <button
                  onClick={() => handleRemoveField(f.id)}
                  className="font-mono text-[11px] uppercase tracking-wide text-clay hover:opacity-70"
                >
                  Remove
                </button>
              )}
            </div>
          ))}
          {fields.length === 0 && <p className="text-sm text-ink-muted">No fields yet - add one below.</p>}
        </div>
      </div>

      {isDraft && (
        <>
          <div>
            <h3 className="mb-3 font-mono text-[11px] font-medium uppercase tracking-wider text-ink-muted">Add a field</h3>
            <AddFieldForm
              token={token}
              formId={formId!}
              lookupTargets={allForms.filter((f) => f.id !== formId)}
              onAdded={load}
            />
          </div>

          <div className="border-t border-line pt-6">
            {error && <p className="mb-2 text-sm text-clay">{error}</p>}
            <button
              onClick={handlePublish}
              disabled={publishing || fields.length === 0}
              className="bg-ink px-4 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
            >
              {publishing ? 'Publishing…' : 'Publish form'}
            </button>
            <p className="mt-2 text-xs text-ink-muted">
              Publishing creates the real database table for this form. Fields can be removed freely until then -
              not after.
            </p>
          </div>
        </>
      )}

      {!isDraft && (
        <button
          onClick={() => navigate(`/forms/${formId}`)}
          className="font-mono text-xs uppercase tracking-wide text-ink-muted hover:text-ink"
        >
          Go fill out this form →
        </button>
      )}
    </div>
  );
}
