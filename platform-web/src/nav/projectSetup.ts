/**
 * Project setup chain (admin flows spec F2, 2026-11-14). Carried in the address (`setup={projectId}`);
 * nothing about it is stored. Steps, per the audit (A-5): 1 Project details (done by the submit),
 * 2 Floors and 3 Stage plan - both created automatically when the project is saved, so these steps
 * open that form's records filtered to the project to check them - and 4 Who reports on it (Form
 * assignments, daily report selected). A step whose form isn't on this database is dropped.
 * Pure and import-free so `node --test` can run it (tests/).
 */

export type SetupStepId = 'details' | 'floors' | 'plan' | 'people';

export const SETUP_FORMS = { projects: 'projects', floors: 'project-floors', plan: 'stage-plan-project', daily: 'daily-progress-report' } as const;

export interface SetupForm {
  id: string;
  code: string;
}

export interface SetupStep {
  id: SetupStepId;
  /** The form a records step opens (floors, plan); null for the others. */
  formId: string | null;
}

export function setupSteps(forms: readonly SetupForm[]): SetupStep[] {
  const byCode = (code: string) => forms.find((f) => f.code === code)?.id ?? null;
  const steps: SetupStep[] = [{ id: 'details', formId: null }];
  const floors = byCode(SETUP_FORMS.floors);
  if (floors) steps.push({ id: 'floors', formId: floors });
  const plan = byCode(SETUP_FORMS.plan);
  if (plan) steps.push({ id: 'plan', formId: plan });
  steps.push({ id: 'people', formId: null });
  return steps;
}

/** Where a step opens, for this project; null for "Project details" (already done by the submit). */
export function setupUrl(step: SetupStep, projectId: string): string | null {
  const q = `setup=${encodeURIComponent(projectId)}`;
  if (step.id === 'people') return `/admin/assignments?${q}`;
  if (step.formId) return `/forms/${step.formId}?tab=records&${q}`;
  return null;
}

/** The step a form page is, when it is one (by form id), else null. */
export function stepForForm<S extends SetupStep>(steps: readonly S[], formId: string): S | null {
  return steps.find((s) => s.formId === formId) ?? null;
}

export function nextStep<S extends SetupStep>(steps: readonly S[], current: SetupStepId): S | null {
  const i = steps.findIndex((s) => s.id === current);
  return i >= 0 && i + 1 < steps.length ? steps[i + 1] : null;
}

/** Steps before the current one are done (the chain only moves forward). */
export function stepState(steps: readonly SetupStep[], current: SetupStepId, step: SetupStepId): 'done' | 'current' | 'later' {
  const c = steps.findIndex((s) => s.id === current);
  const i = steps.findIndex((s) => s.id === step);
  return i < c ? 'done' : i === c ? 'current' : 'later';
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The `setup` value when it names a project the person can see; anything else is ignored (F2-8, AC2-6). */
export function validSetupId(value: string | null | undefined, projectIds: readonly string[]): string | null {
  if (!value || !GUID.test(value)) return null;
  return projectIds.some((id) => id.toLowerCase() === value.toLowerCase()) ? value : null;
}

/** People to tick from `?assign=`: known and active only (F3-5). */
export function preselectUsers(ids: readonly string[] | undefined, users: readonly { id: string; isActive: boolean }[]): string[] {
  if (!ids?.length) return [];
  return ids.filter((id) => users.some((u) => u.id === id && u.isActive));
}
