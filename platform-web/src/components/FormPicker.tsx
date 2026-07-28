import { useEffect, useState } from 'react';
import { api, ApiError } from '../api/client';
import type { FormSummaryDto } from '../types';

interface FormPickerProps {
  token: string;
  onSelect: (formId: string) => void;
  selectedFormId?: string;
}

export function FormPicker({ token, onSelect, selectedFormId }: FormPickerProps) {
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.forms
      .list(token)
      .then(setForms)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'Could not load forms.'));
  }, [token]);

  if (error) return <p className="text-sm text-red-600">{error}</p>;
  if (!forms) return <p className="text-sm text-gray-400">Loading forms…</p>;
  if (forms.length === 0) return <p className="text-sm text-gray-400">No forms exist yet.</p>;

  const byModule = forms.reduce<Record<string, FormSummaryDto[]>>((acc, form) => {
    (acc[form.moduleName] ??= []).push(form);
    return acc;
  }, {});

  return (
    <nav className="space-y-6">
      {Object.entries(byModule).map(([moduleName, moduleForms]) => (
        <div key={moduleName}>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-400">{moduleName}</h3>
          <ul className="space-y-1">
            {moduleForms.map((form) => (
              <li key={form.id}>
                <button
                  onClick={() => onSelect(form.id)}
                  disabled={form.status !== 'Published'}
                  className={`w-full rounded-md px-3 py-2 text-left text-sm ${
                    form.id === selectedFormId
                      ? 'bg-gray-900 text-white'
                      : form.status === 'Published'
                        ? 'text-gray-700 hover:bg-gray-100'
                        : 'cursor-not-allowed text-gray-300'
                  }`}
                  title={form.status !== 'Published' ? 'Not published yet' : undefined}
                >
                  {form.name}
                  {form.status !== 'Published' && <span className="ml-2 text-xs">(draft)</span>}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </nav>
  );
}
