import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { FormSummaryDto } from '../types';
import { LoadingSpinner } from './LoadingSpinner';

interface FormPickerProps {
  token: string;
}

export function FormPicker({ token }: FormPickerProps) {
  const { t } = useTranslation();
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [indicator, setIndicator] = useState<{ top: number; height: number } | null>(null);
  const navigate = useNavigate();
  const { formId: selectedFormId } = useParams<{ formId: string }>();
  const navRef = useRef<HTMLElement>(null);
  const itemRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  useEffect(() => {
    api.forms
      .list(token)
      .then(setForms)
      .catch((err) => setError(err instanceof ApiError ? err.message : 'Could not load forms.'));
  }, [token]);

  // Measures the actual DOM position of the active item rather than computing it
  // arithmetically from list index - stays correct regardless of how many modules or
  // items are above it, and re-measures whenever the selection or the list itself changes.
  useLayoutEffect(() => {
    if (!selectedFormId || !navRef.current) {
      setIndicator(null);
      return;
    }
    const activeButton = itemRefs.current[selectedFormId];
    if (!activeButton) {
      setIndicator(null);
      return;
    }
    const navTop = navRef.current.getBoundingClientRect().top;
    const btnRect = activeButton.getBoundingClientRect();
    setIndicator({ top: btnRect.top - navTop, height: btnRect.height });
  }, [selectedFormId, forms]);

  if (error) return <p className="text-sm text-clay">{error}</p>;
  if (!forms)
    return (
      <div className="flex items-center gap-2">
        <LoadingSpinner size="sm" />
        <span className="font-mono text-xs uppercase tracking-wide text-sidebar-muted">{t('sidebar.loading')}</span>
      </div>
    );
  if (forms.length === 0) return <p className="text-sm text-sidebar-muted">{t('sidebar.noForms')}</p>;

  const byModule = forms.reduce<Record<string, FormSummaryDto[]>>((acc, form) => {
    (acc[form.moduleName] ??= []).push(form);
    return acc;
  }, {});

  return (
    <nav ref={navRef} className="relative space-y-5">
      {indicator && (
        <div
          className="absolute start-0 w-[3px] bg-signal transition-all duration-200 ease-out"
          style={{ top: indicator.top, height: indicator.height }}
        />
      )}
      {Object.entries(byModule).map(([moduleName, moduleForms]) => (
        <div key={moduleName}>
          <h3 className="mb-1.5 text-[10px] font-medium uppercase tracking-wider text-sidebar-muted">
            {moduleName}
          </h3>
          <ul className="space-y-0.5">
            {moduleForms.map((form) => {
              const isSelected = form.id === selectedFormId;
              const isPublished = form.status === 'Published';
              return (
                <li key={form.id}>
                  <button
                    ref={(el) => {
                      itemRefs.current[form.id] = el;
                    }}
                    onClick={() => navigate(`/forms/${form.id}`)}
                    disabled={!isPublished}
                    title={!isPublished ? t('sidebar.notPublished') : undefined}
                    className={`w-full px-3 py-1.5 text-start text-sm transition-colors ${
                      isSelected
                        ? 'font-medium text-white'
                        : isPublished
                          ? 'text-sidebar-muted hover:text-white'
                          : 'cursor-not-allowed text-sidebar-border'
                    }`}
                  >
                    {form.name}
                    {!isPublished && <span className="ms-2 text-[10px] uppercase">{t('sidebar.draft')}</span>}
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
