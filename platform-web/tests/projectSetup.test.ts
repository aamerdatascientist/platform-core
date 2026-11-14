// Admin flows spec F2 / F3-5 / V-1: setup and assign parameter handling, bad ids included.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { nextStep, preselectUsers, setupSteps, setupUrl, stepForForm, stepState, validSetupId } from '../src/nav/projectSetup.ts';

const FORMS = [
  { id: 'f-projects', code: 'projects' },
  { id: 'f-floors', code: 'project-floors' },
  { id: 'f-plan', code: 'stage-plan-project' },
  { id: 'f-daily', code: 'daily-progress-report' },
];
const P = '0f7b05c9-1bf5-4193-ac45-e981b3830036';

test('the four steps of the audit, in order (F2-3)', () => {
  assert.deepEqual(setupSteps(FORMS).map((s) => s.id), ['details', 'floors', 'plan', 'people']);
});

test('a step whose form is missing is dropped and the rest renumber', () => {
  assert.deepEqual(setupSteps([{ id: 'f-projects', code: 'projects' }]).map((s) => s.id), ['details', 'people']);
});

test('each step opens the right page for the project (F2-4, F2-6)', () => {
  const s = setupSteps(FORMS);
  assert.equal(setupUrl(s[0], P), null);
  assert.equal(setupUrl(s[1], P), `/forms/f-floors?tab=records&setup=${P}`);
  assert.equal(setupUrl(s[2], P), `/forms/f-plan?tab=records&setup=${P}`);
  assert.equal(setupUrl(s[3], P), `/admin/assignments?setup=${P}`);
});

test('next step, form-to-step and done/current/later', () => {
  const s = setupSteps(FORMS);
  assert.equal(nextStep(s, 'details')?.id, 'floors');
  assert.equal(nextStep(s, 'people'), null);
  assert.equal(stepForForm(s, 'f-plan')?.id, 'plan');
  assert.equal(stepForForm(s, 'f-daily'), null);
  assert.deepEqual(s.map((x) => stepState(s, 'plan', x.id)), ['done', 'done', 'current', 'later']);
});

test('bad setup ids are ignored (AC2-6)', () => {
  const known = [P];
  assert.equal(validSetupId(P, known), P);
  assert.equal(validSetupId(P.toUpperCase(), known), P.toUpperCase());
  for (const bad of [null, '', 'abc', '120', '00000000-0000-0000-0000-000000000000', `${P}x`, "'; drop table x; --"]) assert.equal(validSetupId(bad, known), null, String(bad));
});

test('assign preselection keeps known, active people only (F3-5)', () => {
  const users = [
    { id: 'a', isActive: true },
    { id: 'b', isActive: false },
  ];
  assert.deepEqual(preselectUsers(['a'], users), ['a']);
  assert.deepEqual(preselectUsers(['b'], users), []);
  assert.deepEqual(preselectUsers(['zzz'], users), []);
  assert.deepEqual(preselectUsers(undefined, users), []);
});
