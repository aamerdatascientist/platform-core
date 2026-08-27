import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FormSummaryDto } from '../types';
import { LoadingSpinner } from './LoadingSpinner';
import { StatusLed } from './StatusLed';

interface FormPickerProps {
  token: string;
}

export function FormPicker({ token }: FormPickerProps) {
  const { t } = useTranslation();
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [error, setError] = useErrorMessage();
  const navigate = useNavigate();
  const { formId: selectedFormId } = useParams<{ formId: string }>();

  useEffect(() => {
    api.forms
      .list(token)
      .then(setForms)
      .catch((err) => setError({ err, fallbackKey: 'sidebar.loadFormsError' }));
  }, [token]);

  if (error) return <p className="text-sm text-danger">{error}</p>;
  if (!forms)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="font-mono text-xs uppercase tracking-wide text-sidebar-ink">{t('sidebar.loading')}</span>
      </div>
    );
  if (forms.length === 0) return <p className="text-sm text-sidebar-ink">{t('sidebar.noForms')}</p>;

  const byModule = forms.reduce<Record<string, FormSummaryDto[]>>((acc, form) => {
    (acc[form.moduleName] ??= []).push(form);
    return acc;
  }, {});

  return (
    <nav className="space-y-5">
      {Object.entries(byModule).map(([moduleName, moduleForms]) => (
        <div key={moduleName}>
          <h3 className="mb-1.5 text-[10px] font-medium uppercase tracking-wider text-sidebar-ink">
            {moduleName}
          </h3>
          <ul className="space-y-0.5">
            {moduleForms.map((form) => {
              const isSelected = form.id === selectedFormId;
              const isPublished = form.status === 'Published';
              return (
                <li key={form.id}>
                  {/* Filled box for the active item (bg-accent/text-accent-ink), not the
                      old measured thin accent-line indicator that used to float next to
                      it - that treatment predated the steel work and never got swept
                      over, same root cause as the white inputs. Quiet bg-border-toned
                      hover on non-active published items - the reference mockup was
                      static and didn't define a hover state explicitly. */}
                  <button
                    onClick={() => navigate(`/forms/${form.id}`)}
                    disabled={!isPublished}
                    title={!isPublished ? t('sidebar.notPublished') : undefined}
                    className={`w-full rounded px-3 py-1.5 text-start text-sm transition-colors ${
                      isSelected
                        ? 'bg-accent font-medium text-accent-ink'
                        : isPublished
                          ? 'text-sidebar-ink hover:bg-border/40 hover:text-sidebar-ink-strong'
                          : 'cursor-not-allowed text-sidebar-ink/40'
                    }`}
                  >
                    {form.name}
                    {!isPublished && (
                      <span className="ms-2 inline-block align-middle">
                        <StatusLed label={t('sidebar.draft')} tone="sidebar" />
                      </span>
                    )}
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
