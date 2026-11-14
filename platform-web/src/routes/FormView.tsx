import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { statusKindOf, type StatusKind } from '../api/errorMessage';
import { FormRenderer } from '../components/FormRenderer';
import { Icon } from '../components/icons';
import { RecordsTab } from '../components/records/RecordsTab';
import { useOptionalShell } from '../components/shell/ShellData';
import { StatusPage } from '../components/StatusPage';
import { ErrorState, InfoBanner, LoadingState, useIsDesktop } from '../components/ui';
import { useLocalized } from '../forms/localized';
import { useErrorMessage } from '../hooks/useErrorMessage';
import { SetupStrip, useSetupProject, useSetupSteps } from '../components/ProjectSetup';
import { nextStep, setupUrl, stepForForm } from '../nav/projectSetup';
import type { FormDefinitionDto, RecordItemDto } from '../types';

/**
 * A form's page. On a phone the form is the whole page (its step header carries the title,
 * HANDOFF 03-06). On a desktop (screen 20): breadcrumb (module), title with the status pill and
 * "Edit form" for administrators, then the tabs Fill | Records (count) - the tab lives in the
 * URL (?tab=records). Project managers only fill forms in: no tab bar, no records (the server
 * refuses them the list anyway).
 */
export function FormView({ token }: { token: string }) {
  const { t } = useTranslation();
  const { formId } = useParams<{ formId: string }>();
  const [search, setSearch] = useSearchParams();
  const desktop = useIsDesktop();
  const { formName } = useLocalized();
  const shell = useOptionalShell();
  const isAdmin = shell?.isAdmin ?? false;

  const [form, setForm] = useState<FormDefinitionDto | null>(null);
  const [loadFailure, setLoadFailure] = useState<StatusKind | null>(null);
  const [error, setError] = useErrorMessage();
  const [sentShown, setSentShown] = useState(false);
  const [editing, setEditing] = useState<RecordItemDto | null>(null);
  // Admin flows F2-3/F2-4: a project setup step (?setup={projectId}) on the floors or stage plan form.
  const setupParam = search.get('setup');
  const setupProject = useSetupProject(setupParam);
  const setupStepsList = useSetupSteps();
  const [notice, setNotice] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [count, setCount] = useState<number | null>(null);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    if (!formId) return;
    setForm(null);
    setEditing(null);
    setNotice(null);
    setError(null);
    setLoadFailure(null);
    setSentShown(false); // a new form never starts on the previous form's Sent screen (setup "Next" stays on this page)
    api.forms
      .get(token, formId)
      .then(setForm)
      .catch((err) => {
        const kind = statusKindOf(err);
        if (kind && kind !== 'sessionEnded') setLoadFailure(kind);
        else setError({ err, fallbackKey: 'formView.loadError' });
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [formId, token, retry]);

  if (loadFailure) return <StatusPage kind={loadFailure} />;
  if (error) return <ErrorState message={error} onRetry={() => setRetry((n) => n + 1)} />;
  if (!form || !shell?.me) return <LoadingState />;

  const setupStep = setupProject ? stepForForm(setupStepsList, form.id) : null;
  const setupNext = setupStep ? nextStep(setupStepsList, setupStep.id) : null;
  const setupStrip =
    setupProject && setupStep && setupNext ? (
      <SetupStrip
        steps={setupStepsList}
        current={setupStep.id}
        project={setupProject}
        compact={!desktop}
        action={{ label: t('flows.next', { step: setupNext.label }), to: setupUrl(setupNext, setupProject.id) ?? '/' }}
      />
    ) : null;
  const keepSetup: Record<string, string> = setupStrip && setupParam ? { setup: setupParam } : {};

  if (!desktop) {
    // On a phone a setup step shows the strip and this project's records (checked or edited here).
    if (setupStrip && setupProject)
      return (
        <div className="flex flex-col gap-3">
          <h1 className="text-[22px] font-extrabold text-ink">
            <bdi>{formName(form)}</bdi>
          </h1>
          {setupStrip}
          {editing ? (
            <FormRenderer
              key={`edit-${editing.id}`}
              token={token}
              formDefinition={form}
              editRecord={editing}
              onCancelEdit={() => setEditing(null)}
              onSubmitted={() => {
                setEditing(null);
                setRefreshKey((k) => k + 1);
              }}
            />
          ) : (
            <RecordsTab token={token} form={form} refreshKey={refreshKey} onCount={setCount} onEdit={setEditing} initialProject={setupProject.id} />
          )}
        </div>
      );
    return <FormRenderer token={token} formDefinition={form} onSubmitted={() => setRefreshKey((k) => k + 1)} onSentChange={setSentShown} />;
  }

  const tab = isAdmin && (search.get('tab') === 'records' || (setupStrip && !editing)) ? 'records' : 'fill';
  const setTab = (next: 'fill' | 'records') => {
    setNotice(null);
    setSearch(next === 'records' ? { tab: 'records', ...keepSetup } : keepSetup, { replace: true });
  };
  const moduleLabel = t(`moduleName.${form.moduleName}`, { defaultValue: form.moduleName });
  const tabClass = (on: boolean) =>
    `-mb-px flex h-[44px] items-center gap-2 border-b-[3px] px-4 text-[16px] ${on ? 'border-accent font-bold text-accent-strong' : 'border-transparent font-medium text-ink-soft hover:text-ink'}`;

  return (
    <div className="flex flex-col gap-[18px]">
      {!sentShown && (
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div className="flex min-w-0 flex-col gap-1">
            <span className="text-[14px] text-ink-soft">{moduleLabel}</span>
            <div className="flex flex-wrap items-center gap-[10px]">
              <h1 className="text-[28px] font-extrabold text-ink">
                <bdi>{formName(form)}</bdi>
              </h1>
              <span className={`rounded-ui-pill px-[10px] py-[3px] text-[12px] font-bold ${form.status === 'Published' ? 'bg-success-soft text-success' : 'bg-accent-soft text-accent-strong'}`}>
                {t(`formStatus.${form.status}`)}
              </span>
            </div>
          </div>
          {isAdmin && (
            <Link to={`/builder/${form.id}`} className="flex h-[42px] items-center gap-2 rounded-ui-lg border border-border bg-panel px-4 text-[15px] font-medium text-ink hover:border-ink-soft">
              <Icon name="pencil" size={16} />
              {t('records.editForm')}
            </Link>
          )}
        </div>
      )}

      {!sentShown && setupStrip}

      {isAdmin && !sentShown && (
        <div role="tablist" className="flex gap-1 border-b border-border">
          <button type="button" role="tab" aria-selected={tab === 'fill'} onClick={() => setTab('fill')} className={tabClass(tab === 'fill')}>
            {t('records.tabFill')}
          </button>
          <button type="button" role="tab" aria-selected={tab === 'records'} onClick={() => setTab('records')} className={tabClass(tab === 'records')}>
            {t('records.tabRecords')}
            {count !== null && <span className="rounded-ui-pill bg-accent-soft px-2 py-px text-[12px]">{count}</span>}
          </button>
        </div>
      )}

      {notice && <InfoBanner>{notice}</InfoBanner>}

      {tab === 'fill' &&
        (editing ? (
          <div className="flex flex-col gap-3">
            <InfoBanner>{t('formView.editingRecord')}</InfoBanner>
            <FormRenderer
              key={`edit-${editing.id}`}
              token={token}
              formDefinition={form}
              editRecord={editing}
              onCancelEdit={() => {
                setEditing(null);
                setTab('records');
              }}
              onSubmitted={() => {
                setEditing(null);
                setRefreshKey((k) => k + 1);
                setTab('records');
                setNotice(t('formView.recordSaved'));
              }}
            />
          </div>
        ) : (
          <FormRenderer token={token} formDefinition={form} onSubmitted={() => setRefreshKey((k) => k + 1)} onSentChange={setSentShown} />
        ))}

      {/* Mounted (hidden) on the Fill tab too, so the tab's count is there from the start. */}
      {isAdmin && (
        <div hidden={tab !== 'records'}>
          <RecordsTab
            token={token}
            form={form}
            refreshKey={refreshKey}
            onCount={setCount}
            initialProject={setupProject && setupStrip ? setupProject.id : undefined}
            onEdit={(record) => {
              setEditing(record);
              setSearch(keepSetup, { replace: true });
            }}
          />
        </div>
      )}
    </div>
  );
}
