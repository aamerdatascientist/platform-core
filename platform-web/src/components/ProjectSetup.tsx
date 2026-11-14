import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { useLocalized } from '../forms/localized';
import { nextStep, setupSteps, stepState, validSetupId, type SetupStep, type SetupStepId } from '../nav/projectSetup';
import type { StageProjectSummaryDto } from '../types';
import { Icon } from './icons';
import { useOptionalShell } from './shell/ShellData';
import { Button } from './ui';

export interface LabelledStep extends SetupStep {
  label: string;
}

/** The chain's steps with their labels: form steps use the forms' own display names (spec §8). */
export function useSetupSteps(): LabelledStep[] {
  const { t } = useTranslation();
  const { formName } = useLocalized();
  const forms = useOptionalShell()?.forms;
  return useMemo(() => {
    const list = (forms ?? []).filter((f) => f.status === 'Published');
    return setupSteps(list).map((s) => {
      const form = s.formId ? list.find((f) => f.id === s.formId) : null;
      const label = s.id === 'details' ? t('flows.setupDetails') : s.id === 'people' ? t('flows.setupPeople') : form ? formName(form) : s.id;
      return { ...s, label };
    });
  }, [forms, t, formName]);
}

/**
 * The project named by `?setup=` when an administrator can see it, else null - so a bad or
 * foreign id changes nothing on the page (F2-8, AC2-6).
 */
export function useSetupProject(param: string | null): StageProjectSummaryDto | null {
  const shell = useOptionalShell();
  const [project, setProject] = useState<StageProjectSummaryDto | null>(null);
  useEffect(() => {
    setProject(null);
    if (!param || !shell?.isAdmin) return;
    let cancelled = false;
    api.stageProgress
      .projects(shell.token)
      .then((list) => {
        const id = validSetupId(param, list.map((p) => p.id));
        if (!cancelled) setProject(list.find((p) => p.id === id) ?? null);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [param, shell?.isAdmin, shell?.token]);
  return project;
}

/** Success screen in setup mode (F2-2): "Project setup · {done} of {total} done" and one row per step. */
export function SetupCard({ steps, current }: { steps: LabelledStep[]; current: SetupStepId }) {
  const { t } = useTranslation();
  const done = steps.filter((s) => stepState(steps, current, s.id) === 'done').length;
  return (
    <div className="flex w-full flex-col gap-2 rounded-ui-2xl border border-border bg-panel px-4 py-[14px] text-start">
      <span className="text-[15px] font-extrabold text-ink">{t('flows.setupTitle', { done, total: steps.length })}</span>
      <ol className="flex flex-col gap-1.5">
        {steps.map((s) => {
          const state = stepState(steps, current, s.id);
          return (
            <li key={s.id} className={`flex items-center gap-2 text-[15px] ${state === 'done' ? 'text-success' : state === 'current' ? 'font-bold text-ink' : 'text-ink-soft'}`}>
              <span aria-hidden="true" className="flex w-5 justify-center">
                {state === 'done' ? <Icon name="check" size={16} /> : state === 'current' ? '●' : '○'}
              </span>
              <span>
                <bdi>{s.label}</bdi>
                <span className="sr-only"> · {t(`flows.state.${state}`)}</span>
              </span>
            </li>
          );
        })}
      </ol>
    </div>
  );
}

/**
 * The setup strip on a step page (F2-4): pills (done / current / later) with the step's action on
 * a laptop; one line under the header on a phone. `action` is "Next: {step}" or "Finish setup".
 */
export function SetupStrip({
  steps,
  current,
  project,
  compact,
  action,
}: {
  steps: LabelledStep[];
  current: SetupStepId;
  project: StageProjectSummaryDto;
  compact: boolean;
  action: { label: string; to?: string; onClick?: () => void };
}) {
  const { t } = useTranslation();
  const n = steps.findIndex((s) => s.id === current) + 1;
  const step = steps[n - 1];
  const button = action.to ? (
    <Link to={action.to} className="inline-flex min-h-[40px] shrink-0 items-center gap-1.5 rounded-ui-md bg-accent px-3 text-[14px] font-bold text-accent-ink hover:opacity-90">
      {action.label}
      <Icon name="chevronForward" size={16} />
    </Link>
  ) : (
    <Button size="compact" className="!min-h-[40px] shrink-0" onClick={action.onClick}>
      {action.label}
    </Button>
  );
  if (compact)
    return (
      <div role="region" aria-label={t('flows.setupLabel')} className="flex flex-wrap items-center gap-2 rounded-ui-lg bg-accent-soft px-3 py-2">
        <span className="min-w-0 flex-1 text-[13px] text-accent-strong">
          <bdi className="font-bold">{project.code}</bdi> · {t('flows.stepLine', { n, total: steps.length, step: step?.label ?? '' })}
        </span>
        {button}
      </div>
    );
  return (
    <div role="region" aria-label={t('flows.setupLabel')} className="flex flex-wrap items-center gap-2 rounded-ui-xl border border-border bg-panel px-3 py-2">
      <span className="text-[13px] font-bold text-ink-soft">
        <bdi>{project.code}</bdi>
      </span>
      <ol className="flex flex-1 flex-wrap items-center gap-1.5">
        {steps.map((s, i) => {
          const state = stepState(steps, current, s.id);
          return (
            <li key={s.id} className="flex items-center gap-1.5">
              {i > 0 && (
                <span aria-hidden="true" className="text-ink-soft">
                  <Icon name="chevronForward" size={12} />
                </span>
              )}
              <span
                aria-current={state === 'current' ? 'step' : undefined}
                className={`inline-flex items-center gap-1 rounded-ui-pill px-[10px] py-[3px] text-[13px] font-bold ${
                  state === 'done' ? 'bg-success-soft text-success' : state === 'current' ? 'bg-accent text-accent-ink' : 'border border-border text-ink-soft'
                }`}
              >
                {state === 'done' ? <Icon name="check" size={13} /> : <span>{i + 1} ·</span>}
                <bdi>{s.label}</bdi>
              </span>
            </li>
          );
        })}
      </ol>
      {button}
    </div>
  );
}

/** "Project setup complete" with the way to its Stage progress page (F2-7). */
export function SetupComplete({ project }: { project: StageProjectSummaryDto }) {
  const { t } = useTranslation();
  return (
    <div role="status" className="flex flex-col gap-3 rounded-ui-lg bg-success-soft px-4 py-3">
      <span className="flex items-center gap-2 text-[15px] font-bold text-success-strong">
        <Icon name="check" size={18} />
        {t('flows.setupComplete')}
      </span>
      <span className="text-[14px] text-success-strong">{t('flows.setupCompleteBody', { project: project.code })}</span>
      <Link
        to={`/admin/projects/${project.id}/progress`}
        className="inline-flex min-h-[40px] items-center gap-1.5 self-start rounded-ui-md bg-accent px-3 text-[14px] font-bold text-accent-ink hover:opacity-90"
      >
        {t('flows.openProgress')}
        <Icon name="chevronForward" size={16} />
      </Link>
    </div>
  );
}

/** The action that moves a step on: the next step's page, or Finish on the last. */
export function nextAction(steps: LabelledStep[], current: SetupStepId, projectId: string, url: (s: SetupStep, id: string) => string | null) {
  const next = nextStep(steps, current);
  const to = next ? url(next, projectId) : null;
  return next && to ? { label: next.label, to } : null;
}
