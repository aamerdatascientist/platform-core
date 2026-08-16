import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import type { FormSummaryDto } from '../types';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { useErrorMessage } from '../hooks/useErrorMessage';

function slugify(input: string): string {
  return input.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

export function BuilderHome({ token }: { token: string }) {
  const { t } = useTranslation();
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [codeTouched, setCodeTouched] = useState(false);
  const [moduleName, setModuleName] = useState('');
  const [error, setError] = useErrorMessage();
  const [submitting, setSubmitting] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    load();
  }, []);

  async function load() {
    const result = await api.forms.list(token);
    setForms(result);
  }

  function handleNameChange(value: string) {
    setName(value);
    if (!codeTouched) setCode(slugify(value));
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim() || !code.trim() || !moduleName.trim()) {
      setError({ key: 'builderHome.requiredError' });
      return;
    }
    setSubmitting(true);
    try {
      const result = await api.forms.create(token, { code, name, moduleName });
      navigate(`/builder/${result.id}`);
    } catch (err) {
      setError({ err, fallbackKey: 'builderHome.createError' });
    } finally {
      setSubmitting(false);
    }
  }

  const existingModules = [...new Set((forms ?? []).map((f) => f.moduleName))];
  const inputClass = 'w-full border border-line px-2 py-1.5 text-sm focus:border-ink focus:outline-none';

  return (
    <div className="max-w-3xl space-y-8">
      <div>
        <h2 className="font-display text-xl font-semibold text-ink">{t('builderHome.title')}</h2>
        <p className="text-sm text-ink-muted">{t('builderHome.subtitle')}</p>
      </div>

      {!creating ? (
        <button
          onClick={() => setCreating(true)}
          className="border border-line bg-white px-3 py-1.5 text-sm text-ink hover:border-ink"
        >
          {t('builderHome.newForm')}
        </button>
      ) : (
        <form onSubmit={handleCreate} className="space-y-3 border border-line bg-white p-4">
          <div>
            <label className="mb-1 block text-xs text-ink-muted">{t('builderHome.formName')}</label>
            <input
              className={inputClass}
              value={name}
              onChange={(e) => handleNameChange(e.target.value)}
              placeholder={t('builderHome.formNamePlaceholder')}
            />
          </div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div>
              <label className="mb-1 block text-xs text-ink-muted">{t('builderHome.code')}</label>
              <input
                className={`${inputClass} font-mono`}
                value={code}
                onChange={(e) => {
                  setCode(e.target.value);
                  setCodeTouched(true);
                }}
              />
            </div>
            <div>
              <label className="mb-1 block text-xs text-ink-muted">{t('builderHome.module')}</label>
              <input
                className={inputClass}
                list="module-suggestions"
                value={moduleName}
                onChange={(e) => setModuleName(e.target.value)}
                placeholder={t('builderHome.modulePlaceholder')}
              />
              <datalist id="module-suggestions">
                {existingModules.map((m) => (
                  <option key={m} value={m} />
                ))}
              </datalist>
            </div>
          </div>
          {error && <p className="text-sm text-clay">{error}</p>}
          <div className="flex gap-2">
            <button
              type="submit"
              disabled={submitting}
              className="bg-ink px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
            >
              {submitting ? t('builderHome.creating') : t('builderHome.createAndAddFields')}
            </button>
            <button type="button" onClick={() => setCreating(false)} className="px-3 py-1.5 text-sm text-ink-muted">
              {t('common.cancel')}
            </button>
          </div>
        </form>
      )}

      <div>
        <h3 className="mb-3 text-[11px] font-medium uppercase tracking-wider text-ink-muted">
          {t('builderHome.existingForms')}
        </h3>
        {!forms ? (
          <div className="flex items-center gap-2">
            <LoadingSpinner size="sm" />
            <span className="text-xs uppercase tracking-wide text-ink-muted">{t('common.loading')}</span>
          </div>
        ) : forms.length === 0 ? (
          <p className="text-sm text-ink-muted">{t('builderHome.noForms')}</p>
        ) : (
          <div className="space-y-1">
            {forms.map((f) => (
              <button
                key={f.id}
                onClick={() => navigate(`/builder/${f.id}`)}
                className="flex w-full items-center justify-between border border-line bg-white px-3 py-2 text-start text-sm hover:border-ink"
              >
                <span>
                  <span className="font-medium text-ink">{f.name}</span>
                  <span className="ms-2 text-xs text-ink-muted">{f.moduleName}</span>
                </span>
                <span className="text-[11px] uppercase tracking-wide text-ink-muted">
                  {t(`formStatus.${f.status}`)}
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
