import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { FormSummaryDto } from '../types';

interface FormPickerProps {
  token: string;
}

export function FormPicker({ token }: FormPickerProps) {
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const { formId: selectedFormId } = useParams<{ formId: string }>();

  useEffect(() => {
    api.forms
      .list(token)
      .then(setForms)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'Could not load forms.'));
  }, [token]);

  if (error) return <p className="text-sm text-clay">{error}</p>;
  if (!forms) return <p className="font-mono text-xs uppercase tracking-wide text-ink-muted">Loading…</p>;
  if (forms.length === 0) return <p className="text-sm text-ink-muted">No forms exist yet.</p>;

  const byModule = forms.reduce<Record<string, FormSummaryDto[]>>((acc, form) => {
    (acc[form.moduleName] ??= []).push(form);
    return acc;
  }, {});

  return (
    <nav className="space-y-6">
      {Object.entries(byModule).map(([moduleName, moduleForms]) => (
        <div key={moduleName}>
          <h3 className="mb-2 font-mono text-[11px] font-medium uppercase tracking-wider text-ink-muted">{moduleName}</h3>
          <ul className="space-y-0.5">
            {moduleForms.map((form) => {
              const isSelected = form.id === selectedFormId;
              const isPublished = form.status === 'Published';
              return (
                <li key={form.id}>
                  <button
                    onClick={() => navigate(`/forms/${form.id}`)}
                    disabled={!isPublished}
                    title={!isPublished ? 'Not published yet' : undefined}
                    className={`w-full border-l-2 px-3 py-1.5 text-left text-sm transition-colors ${
                      isSelected
                        ? 'border-signal bg-white font-medium text-ink'
                        : isPublished
                          ? 'border-transparent text-ink-muted hover:border-line hover:text-ink'
                          : 'cursor-not-allowed border-transparent text-line'
                    }`}
                  >
                    {form.name}
                    {!isPublished && <span className="ml-2 font-mono text-[10px] uppercase">draft</span>}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}
