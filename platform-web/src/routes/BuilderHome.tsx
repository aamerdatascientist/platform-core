import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { builderInput } from '../components/builder/FieldSettings';
import { LoadingSpinner } from '../components/LoadingSpinner';
import { Button, ClickableRow, EmptyState, Sheet, TableCard, tdClass, thClass } from '../components/ui';
import { useLocalized } from '../forms/localized';
import { useErrorMessage } from '../hooks/useErrorMessage';
import type { FormSummaryDto } from '../types';

function slugify(input: string): string {
  return input.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
}

/**
 * Form builder home (phase 8, styled like the other administration pages - no screen of its own
 * in the handoff): every form with its module and status, opening the builder (21); "+ New form"
 * opens the existing create form in a dialog.
 */
export function BuilderHome({ token }: { token: string }) {
  const { t } = useTranslation();
  const { formName } = useLocalized();
  const navigate = useNavigate();
  const [forms, setForms] = useState<FormSummaryDto[] | null>(null);
  const [creating, setCreating] = useState(false);
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [codeTouched, setCodeTouched] = useState(false);
  const [moduleName, setModuleName] = useState('');
  const [error, setError] = useErrorMessage();
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    api.forms.list(token).then(setForms).catch((err) => setError({ err, fallbackKey: 'formBuilder.loadError' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim() || !code.trim() || !moduleName.trim()) return setError({ key: 'builderHome.requiredError' });
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

  const modules = [...new Set((forms ?? []).map((f) => f.moduleName))];
  const moduleLabel = (m: string) => t(`moduleName.${m}`, { defaultValue: m });

  return (
    <div className="flex flex-col gap-[18px]">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <span className="text-[14px] text-ink-soft">{t('nav.administration')}</span>
          <h1 className="text-[28px] font-extrabold text-ink">{t('settings.formBuilder')}</h1>
          <p className="text-[14px] text-ink-soft">{t('builderHome.subtitle')}</p>
        </div>
        <Button size="desktop" onClick={() => (setError(null), setCreating(true))}>
          {t('builderHome.newForm')}
        </Button>
      </div>

      {!forms ? (
        error ? <p className="text-[14px] text-danger">{error}</p> : <LoadingSpinner />
      ) : forms.length === 0 ? (
        <EmptyState title={t('builderHome.noForms')} />
      ) : (
        <TableCard className="[&_table]:min-w-[560px]">
          <thead>
            <tr>
              <th className={thClass}>{t('builderHome.formName')}</th>
              <th className={thClass}>{t('builderHome.module')}</th>
              <th className={thClass}>{t('usersV2.colStatus')}</th>
            </tr>
          </thead>
          <tbody>
            {[...forms]
              .sort((a, b) => a.moduleName.localeCompare(b.moduleName) || a.name.localeCompare(b.name))
              .map((f) => (
                <ClickableRow key={f.id} onOpen={() => navigate(`/builder/${f.id}`)} label={formName(f)}>
                  <td className={tdClass}>
                    <span className="flex flex-col">
                      <span className="font-bold">
                        <bdi>{formName(f)}</bdi>
                      </span>
                      <span dir="ltr" className="text-start font-mono text-[12px] text-ink-soft rtl:text-right">
                        {f.code}
                      </span>
                    </span>
                  </td>
                  <td className={tdClass}>{moduleLabel(f.moduleName)}</td>
                  <td className={tdClass}>
                    <span className={`rounded-ui-pill px-[9px] py-[3px] text-[12px] font-bold ${f.status === 'Published' ? 'bg-success-soft text-success' : 'bg-accent-soft text-accent-strong'}`}>
                      {t(`formStatus.${f.status}`)}
                    </span>
                  </td>
                </ClickableRow>
              ))}
          </tbody>
        </TableCard>
      )}

      <Sheet open={creating} onClose={() => setCreating(false)} title={t('builderHome.newForm')} desktopWidth={560}>
        <form onSubmit={handleCreate} className="flex flex-col gap-3 pb-2">
          <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
            {t('builderHome.formName')}
            <input className={builderInput} value={name} placeholder={t('builderHome.formNamePlaceholder')} onChange={(e) => (setName(e.target.value), !codeTouched && setCode(slugify(e.target.value)))} />
          </label>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
              {t('builderHome.code')}
              <input dir="ltr" className={`${builderInput} font-mono`} value={code} onChange={(e) => (setCode(e.target.value), setCodeTouched(true))} />
            </label>
            <label className="flex flex-col gap-1.5 text-[14px] font-bold text-ink">
              {t('builderHome.module')}
              <input className={builderInput} list="module-suggestions" value={moduleName} placeholder={t('builderHome.modulePlaceholder')} onChange={(e) => setModuleName(e.target.value)} />
              <datalist id="module-suggestions">
                {modules.map((m) => (
                  <option key={m} value={m} />
                ))}
              </datalist>
            </label>
          </div>
          {error && <p className="text-[14px] text-danger">{error}</p>}
          <div>
            <Button type="submit" size="desktop" disabled={submitting}>
              {submitting ? t('builderHome.creating') : t('builderHome.createAndAddFields')}
            </Button>
          </div>
        </form>
      </Sheet>
    </div>
  );
}
