// Admin flows spec F5 / V-1: the grouping map. Run: npm test (node --test, no extra packages).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildNav, everydayForms, groupContainsPath, PHONE_ADMIN_ORDER, type NavForm } from '../src/nav/navMap.ts';

const f = (code: string, moduleName = 'Operations', extra: Partial<NavForm> = {}): NavForm => ({ id: `id-${code}`, code, moduleName, status: 'Published', ...extra });
// The staging forms, in the API's order (by module, then Arabic name).
const STAGING = [
  f('daily-progress-report', 'Daily Reports'),
  f('project-floors'),
  f('project-footings', 'Operations', { isHidden: true }),
  f('projects'),
  f('project-zones', 'Operations', { isHidden: true }),
  f('project-pause-log', 'Projects'),
  f('stage-plan-default', 'Projects'),
  f('stage-plan-project', 'Projects'),
  f('stock-inflow', 'StockManagement'),
  f('stock-outflow', 'StockManagement'),
];
const flat = (groups: ReturnType<typeof buildNav>) =>
  groups.map((g) => [g.id, g.items.map((i) => (i.kind === 'form' ? i.form.code : `#${i.id}`))]);

test('administrator: the target menu, in order (F5-2)', () => {
  assert.deepEqual(flat(buildNav(STAGING, true)), [
    ['daily', ['daily-progress-report', 'stock-inflow', 'stock-outflow']],
    ['projects', ['projects', 'project-floors', 'stage-plan-project', '#stageProgress', 'project-pause-log']],
    ['team', ['#users', '#assignments', '#messages', '#reports']],
    ['overview', ['#overview']],
    ['advanced', ['#builder', 'stage-plan-default']],
  ]);
});

test('project manager who may open only the daily report: Daily work only (AC5-3)', () => {
  assert.deepEqual(flat(buildNav([f('daily-progress-report', 'Daily Reports')], false)), [['daily', ['daily-progress-report']]]);
});

test('no admin pages for a non-administrator, even with every form open', () => {
  const groups = buildNav(STAGING, false);
  assert.ok(groups.every((g) => g.items.every((i) => i.kind === 'form')));
  assert.ok(!groups.some((g) => g.id === 'team' || g.id === 'overview'));
});

test('hidden and unpublished forms never show (F5-3)', () => {
  const codes = buildNav([...STAGING, f('draft-form', 'X', { status: 'Draft' })], true).flatMap((g) => g.items.flatMap((i) => (i.kind === 'form' ? [i.form.code] : [])));
  assert.ok(!codes.includes('project-footings') && !codes.includes('project-zones') && !codes.includes('draft-form'));
});

test('an unmapped form gets a group named after its module, right after Projects (F5-3)', () => {
  const groups = buildNav([...STAGING, f('safety-check', 'Safety')], true);
  assert.deepEqual(groups.map((g) => g.id), ['daily', 'projects', 'module:Safety', 'team', 'overview', 'advanced']);
  assert.equal(groups[2].moduleName, 'Safety');
  assert.equal(groups[2].labelKey, null);
});

test('the fallback group still lands after Projects when the person sees no Projects forms', () => {
  const groups = buildNav([f('daily-progress-report'), f('safety-check', 'Safety')], false);
  assert.deepEqual(groups.map((g) => g.id), ['daily', 'module:Safety']);
});

test("Today's other forms: menu order, no Advanced setup (F5-9)", () => {
  assert.deepEqual(everydayForms(buildNav(STAGING, true)).map((x) => x.code), [
    'daily-progress-report', 'stock-inflow', 'stock-outflow', 'projects', 'project-floors', 'stage-plan-project', 'project-pause-log',
  ]);
});

test('Advanced setup knows its own pages (F5-7 auto-open)', () => {
  const advanced = buildNav(STAGING, true).find((g) => g.id === 'advanced')!;
  assert.ok(groupContainsPath(advanced, '/builder'));
  assert.ok(groupContainsPath(advanced, '/builder/abc'));
  assert.ok(groupContainsPath(advanced, '/forms/id-stage-plan-default'));
  assert.ok(!groupContainsPath(advanced, '/forms/id-projects'));
  assert.ok(!groupContainsPath(advanced, '/builders'));
});

test('phone Settings > Administration order (F5-9)', () => {
  assert.deepEqual(PHONE_ADMIN_ORDER, ['users', 'assignments', 'messages', 'reports', 'overview', 'stageProgress', 'builder']);
});
