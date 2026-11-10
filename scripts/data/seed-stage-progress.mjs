#!/usr/bin/env node
// Sets up the stage-progress forms (owner-approved plan, 2026-11-10) through the API, as an
// administrator - never against the database directly. Safe to run again: everything that
// already exists is left as it is.
//
//   API_BASE=https://<api-host> ADMIN_TOKEN=<access token> node scripts/data/seed-stage-progress.mjs [--project-119]
//
// What it does, in order:
//   1. projects: the new setup fields (with their defaults), Location / Planned start date
//      labels, English labels on the status options.
//   2. project-floors: level number, level kind, English label, active - and reorders so the
//      floor's name is its display field.
//   3. stage-plan-default (+ its 36 rows from src/Platform.Application/StageProgress/
//      default-stage-plan.json), stage-plan-project, project-pause-log - administrators only.
//   4. daily-progress-report v2: the old phase fields archived (data kept), a repeatable
//      "stages" section in the Progress step, the project lookup limited to Planning / Active /
//      On hold with "paused" blocking Send.
//   5. Flags: the six forms the calculations read are system forms; zones and footings hidden.
//   --project-119: maps 119's floors (PG = parking, 01-05 = floors 1-5), fills its setup
//      fields (no demolition, no shoring), which copies its plan, and records the starting
//      position the owner confirmed (structure on Floor 4 and walls on Floor 2 at day 4).
// The token is read from the environment and never printed.

import { readFileSync } from 'node:fs';

const base = (process.env.API_BASE ?? '').replace(/\/+$/, '');
const token = process.env.ADMIN_TOKEN ?? '';
const do119 = process.argv.includes('--project-119');
if (!/^https?:\/\//.test(base) || !token) {
  console.error('Set API_BASE (http/https) and ADMIN_TOKEN.');
  process.exit(1);
}

async function call(method, path, body) {
  const res = await fetch(`${base}/api${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, ...(body !== undefined ? { 'Content-Type': 'application/json; charset=utf-8' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${(await res.text()).slice(0, 400)}`);
  return res.status === 204 ? null : res.json();
}

const log = (...a) => console.log(...a);
const opts = (pairs) => JSON.stringify(pairs.map(([value, label, labelEn]) => ({ value, label, labelEn })));
const rules = (o) => JSON.stringify(o);

async function formByCode(code) {
  const list = await call('GET', '/forms');
  const summary = list.find((f) => f.code === code);
  return summary ? call('GET', `/forms/${summary.id}`) : null;
}

const editable = (form) => form.publishedVersion ?? form.draftVersion;

async function ensureForm(code, name, nameEn, moduleName) {
  let form = await formByCode(code);
  if (!form) {
    const created = await call('POST', '/forms', { code, name, moduleName, description: null });
    log(`  created form ${code}`);
    form = await call('GET', `/forms/${created.id ?? created}`);
  }
  if (form.name !== name || form.nameEn !== nameEn) await call('PUT', `/forms/${form.id}/name`, { name, nameEn });
  return call('GET', `/forms/${form.id}`);
}

/** Adds the field unless a field with that code exists (active or archived). */
async function ensureField(form, f) {
  const existing = editable(form).fields.find((x) => x.code === f.code);
  if (existing) return existing;
  const body = { isRequired: false, optionsJson: null, lookupFormDefinitionId: null, validationRulesJson: null, ...f };
  await call('POST', `/forms/${form.id}/fields`, body);
  log(`  + ${form.code}.${f.code}`);
  const fresh = await call('GET', `/forms/${form.id}`);
  Object.assign(form, fresh);
  return editable(fresh).fields.find((x) => x.code === f.code);
}

async function publishIfDraft(form) {
  if (form.status === 'Published') return form;
  await call('POST', `/forms/${form.id}/publish`);
  log(`  published ${form.code}`);
  return call('GET', `/forms/${form.id}`);
}

/** Sets required/default/block-when, keeping the field's other stored rules. */
async function setRules(form, field, changes) {
  const r = field.validationRulesJson ? JSON.parse(field.validationRulesJson) : {};
  const get = (k) => r[k] ?? r[k[0].toUpperCase() + k.slice(1)];
  await call('PUT', `/forms/${form.id}/fields/${field.id}/rules`, {
    isRequired: changes.isRequired ?? field.isRequired,
    min: get('min') ?? null,
    max: get('max') ?? null,
    minExclusive: get('minExclusive') ?? false,
    regex: get('regex') ?? null,
    noFutureDates: get('noFutureDates') ?? false,
    unique: get('unique') ?? false,
    uniqueWithin: get('uniqueWithin') ?? null,
    default: changes.default ?? null,
    blockWhenAttribute: changes.blockWhenAttribute ?? null,
    blockWhenValues: changes.blockWhenValues ?? null,
    blockMessage: changes.blockMessage ?? null,
    blockMessageEn: changes.blockMessageEn ?? null,
  });
}

async function relabel(form, code, label, labelEn) {
  const field = editable(form).fields.find((x) => x.code === code);
  if (field && (field.label !== label || field.labelEn !== labelEn)) await call('PUT', `/forms/${form.id}/fields/${field.id}/label`, { label, labelEn });
}

const roles = await call('GET', '/roles');
const adminRole = roles.find((r) => r.name === 'Administrator');

// ------------------------------------------------------------------ 1. projects
log('projects');
let projects = await formByCode('projects');
if (!projects) throw new Error('No projects form.');
await relabel(projects, 'city', 'الموقع', 'Location');
await relabel(projects, 'start_date', 'تاريخ البدء المخطط', 'Planned start date');
await relabel(projects, 'project_code', 'رمز المشروع', 'Project code');
await relabel(projects, 'project_name', 'اسم المشروع', 'Project name');
await relabel(projects, 'status', 'الحالة', 'Status');
const startDate = editable(projects).fields.find((f) => f.code === 'start_date');
if (startDate && !startDate.isRequired) await setRules(projects, startDate, { isRequired: true });
const status = editable(projects).fields.find((f) => f.code === 'status');
if (status) {
  const en = { planning: 'Planning', active: 'Active', on_hold: 'On hold', completed: 'Completed', cancelled: 'Cancelled' };
  const current = JSON.parse(status.optionsJson);
  if (current.some((o) => !o.labelEn))
    await call('PUT', `/forms/${projects.id}/fields/${status.id}/option-labels`, { options: current.map((o) => ({ value: o.value, label: o.label, labelEn: en[o.value] ?? o.labelEn ?? null })) });
}
for (const f of [
  { code: 'plot_area_m2', label: 'مساحة الأرض (م²)', labelEn: 'Plot area (m²)', fieldType: 'Decimal', isRequired: true, validationRulesJson: rules({ min: 0, minExclusive: true, default: '600' }) },
  { code: 'buildable_pct', label: 'نسبة البناء %', labelEn: 'Buildable %', fieldType: 'Decimal', isRequired: true, validationRulesJson: rules({ min: 1, max: 100, default: '75' }) },
  { code: 'buildable_area_m2', label: 'مساحة الدور (م²)', labelEn: 'Buildable area per floor (m²)', fieldType: 'Decimal', formula: 'plot_area_m2 * buildable_pct / 100' },
  { code: 'parking_levels', label: 'أدوار المواقف', labelEn: 'Parking levels', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 0, default: '1' }) },
  { code: 'apartment_floors', label: 'الأدوار السكنية', labelEn: 'Apartment floors', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 1, default: '5' }) },
  { code: 'apartments_per_floor', label: 'الشقق في الدور', labelEn: 'Apartments per floor', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 1, default: '4' }) },
  { code: 'walls_per_floor', label: 'الجدران في الدور', labelEn: 'Walls per floor', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 0, default: '128' }) },
  { code: 'needs_deconstruction', label: 'هل يوجد هدم؟', labelEn: 'Demolition needed?', fieldType: 'Boolean', isRequired: true },
  { code: 'needs_support', label: 'هل يوجد سند؟', labelEn: 'Shoring needed?', fieldType: 'Boolean', isRequired: true },
  { code: 'outside_sides', label: 'عدد الواجهات للياسة', labelEn: 'Outside sides for plastering', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 1, max: 4, default: '4' }) },
])
  await ensureField(projects, f);
projects = await call('GET', `/forms/${projects.id}`);

// ------------------------------------------------------------------ 2. project-floors
log('project-floors');
let floors = await formByCode('project-floors');
if (!floors) throw new Error('No project-floors form.');
for (const f of [
  { code: 'level_number', label: 'رقم المستوى', labelEn: 'Level number', fieldType: 'Number' },
  { code: 'level_kind', label: 'نوع المستوى', labelEn: 'Level kind', fieldType: 'Dropdown', optionsJson: opts([['parking', 'مواقف', 'Parking'], ['apartment', 'دور سكني', 'Apartment floor']]) },
  { code: 'label_en', label: 'الاسم بالإنجليزية', labelEn: 'Name in English', fieldType: 'ShortText' },
  { code: 'is_active', label: 'نشط', labelEn: 'Active', fieldType: 'Boolean', validationRulesJson: rules({ default: 'true' }) },
])
  await ensureField(floors, f);
floors = await call('GET', `/forms/${floors.id}`);
await relabel(floors, 'floor_label', 'اسم الدور', 'Floor name');
await relabel(floors, 'floor_code', 'رمز الدور', 'Floor code');
{
  // The floor's name first among the text fields: it is what lookups show.
  const active = editable(floors).fields.filter((f) => f.isActive).sort((a, b) => a.displayOrder - b.displayOrder);
  const label = active.find((f) => f.code === 'floor_label');
  const code = active.find((f) => f.code === 'floor_code');
  if (label && code && label.displayOrder > code.displayOrder) {
    const ids = active.map((f) => f.id).filter((id) => id !== label.id);
    ids.splice(ids.indexOf(code.id), 0, label.id);
    await call('PUT', `/forms/${floors.id}/fields/order`, { orderedFieldIds: ids });
  }
}

// ------------------------------------------------------------------ 3. plans and pause log
const planFields = (withProject) => [
  ...(withProject ? [{ code: 'project', label: 'المشروع', labelEn: 'Project', fieldType: 'Lookup', isRequired: true, lookupFormDefinitionId: projects.id }] : []),
  { code: 'stage_code', label: 'رمز المرحلة', labelEn: 'Stage code', fieldType: 'ShortText', isRequired: true, validationRulesJson: rules({ regex: '^[a-z][a-z0-9_]*$' }) },
  { code: 'sort_order', label: 'الترتيب', labelEn: 'Order', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 1 }) },
  { code: 'stage_name_en', label: 'اسم المرحلة (إنجليزي)', labelEn: 'Stage name (English)', fieldType: 'ShortText', isRequired: true },
  { code: 'stage_name_ar', label: 'اسم المرحلة (عربي)', labelEn: 'Stage name (Arabic)', fieldType: 'ShortText', isRequired: true },
  { code: 'substep_code', label: 'رمز الخطوة', labelEn: 'Step code', fieldType: 'ShortText', validationRulesJson: rules({ regex: '^[a-z][a-z0-9_]*$' }) },
  { code: 'substep_name_en', label: 'اسم الخطوة (إنجليزي)', labelEn: 'Step name (English)', fieldType: 'ShortText' },
  { code: 'substep_name_ar', label: 'اسم الخطوة (عربي)', labelEn: 'Step name (Arabic)', fieldType: 'ShortText' },
  { code: 'days', label: 'الأيام', labelEn: 'Days', fieldType: 'Number', isRequired: true, validationRulesJson: rules({ min: 1 }) },
  { code: 'duration_type', label: 'نوع المدة', labelEn: 'Duration type', fieldType: 'Dropdown', isRequired: true,
    optionsJson: opts([['fixed', 'ثابتة للمشروع', 'Fixed (whole project)'], ['per_floor', 'لكل دور', 'Per floor'], ['per_side', 'لكل واجهة', 'Per side']]) },
  { code: 'level_scope', label: 'نطاق الأدوار', labelEn: 'Levels', fieldType: 'Dropdown', isRequired: true,
    optionsJson: opts([['all_levels', 'جميع الأدوار', 'All levels'], ['apartment_floors_only', 'الأدوار السكنية فقط', 'Apartment floors only'], ['parking_only', 'المواقف فقط', 'Parking only'], ['none', 'لا ينطبق', 'None']]) },
  { code: 'applies_when', label: 'تنطبق عندما', labelEn: 'Applies when', fieldType: 'Dropdown', isRequired: true,
    optionsJson: opts([['always', 'دائماً', 'Always'], ['if_deconstruction', 'إذا وُجد هدم', 'If demolition is needed'], ['if_support', 'إذا وُجد سند', 'If shoring is needed']]) },
  { code: 'is_active', label: 'نشط', labelEn: 'Active', fieldType: 'Boolean', isRequired: true, validationRulesJson: rules({ default: 'true' }) },
];

async function adminOnly(form) {
  if (!adminRole) return;
  if (form.allowedRoleIds.length !== 1 || form.allowedRoleIds[0] !== adminRole.id)
    await call('PUT', `/forms/${form.id}/allowed-roles`, { roleIds: [adminRole.id] });
}

log('stage-plan-default');
let defaultPlan = await ensureForm('stage-plan-default', 'خطة المراحل الافتراضية', 'Default stage plan', 'Projects');
for (const f of planFields(false)) await ensureField(defaultPlan, f);
defaultPlan = await publishIfDraft(await call('GET', `/forms/${defaultPlan.id}`));
await adminOnly(defaultPlan);
{
  const existing = await call('GET', `/forms/${defaultPlan.id}/submissions?pageSize=1`);
  const count = existing.totalCount ?? existing.items?.length ?? 0;
  if (count === 0) {
    const rows = JSON.parse(readFileSync(new URL('../../src/Platform.Application/StageProgress/default-stage-plan.json', import.meta.url), 'utf8'));
    for (const r of rows) await call('POST', `/forms/${defaultPlan.id}/submissions`, { ...r, is_active: true });
    log(`  ${rows.length} default plan rows`);
  }
}

log('stage-plan-project');
let projectPlan = await ensureForm('stage-plan-project', 'خطة مراحل المشروع', 'Project stage plan', 'Projects');
for (const f of planFields(true)) await ensureField(projectPlan, f);
projectPlan = await publishIfDraft(await call('GET', `/forms/${projectPlan.id}`));
await adminOnly(projectPlan);

log('project-pause-log');
let pauseLog = await ensureForm('project-pause-log', 'سجل إيقاف المشاريع', 'Project pause log', 'Projects');
for (const f of [
  { code: 'project', label: 'المشروع', labelEn: 'Project', fieldType: 'Lookup', isRequired: true, lookupFormDefinitionId: projects.id },
  { code: 'action', label: 'الإجراء', labelEn: 'Action', fieldType: 'Dropdown', isRequired: true, optionsJson: opts([['pause', 'إيقاف مؤقت', 'Pause'], ['resume', 'استئناف', 'Resume']]) },
  { code: 'date', label: 'التاريخ', labelEn: 'Date', fieldType: 'DateTime', isRequired: true, validationRulesJson: rules({ noFutureDates: true }) },
  { code: 'reason', label: 'السبب', labelEn: 'Reason', fieldType: 'Dropdown', isRequired: true,
    optionsJson: opts([['government_inspection', 'تفتيش حكومي', 'Government inspection'], ['other', 'أخرى', 'Other']]) },
  { code: 'note', label: 'ملاحظة', labelEn: 'Note', fieldType: 'LongText', isRequired: true, visibleWhenFieldCode: 'reason', visibleWhenValuesJson: '["other"]' },
])
  await ensureField(pauseLog, f);
pauseLog = await publishIfDraft(await call('GET', `/forms/${pauseLog.id}`));
await adminOnly(pauseLog);

// ------------------------------------------------------------------ 4. daily report v2
log('daily-progress-report');
let report = await formByCode('daily-progress-report');
if (!report) throw new Error('No daily-progress-report form.');
for (const code of ['milestone', 'zone', 'footing', 'floor', 'mep_trade', 'mep_room', 'phase', 'problem_description', 'has_problem_today', 'plan_completed_today']) {
  const f = editable(report).fields.find((x) => x.code === code);
  if (f?.isActive) {
    await call('POST', `/forms/${report.id}/fields/${f.id}/archive`);
    log(`  archived ${code}`);
  }
}
report = await call('GET', `/forms/${report.id}`);
let section = (editable(report).sections ?? []).find((s) => s.code === 'stages');
if (!section) {
  section = await call('POST', `/forms/${report.id}/sections`, { code: 'stages', titleAr: 'مرحلة', titleEn: 'Stage', minBlocks: 1, maxBlocks: 10 });
  log('  section stages');
  report = await call('GET', `/forms/${report.id}`);
}
const progressStep = (editable(report).steps ?? []).find((s) => s.titleEn === 'Progress') ?? (editable(report).steps ?? [])[1] ?? null;
const inBlock = { sectionId: section.id, stepId: progressStep?.id ?? null };
for (const f of [
  { code: 'stage', label: 'المرحلة', labelEn: 'Stage', fieldType: 'Dropdown', isRequired: true, optionsProvider: 'stage-plan.stages' },
  { code: 'substep', label: 'الخطوة', labelEn: 'Step', fieldType: 'Dropdown', isRequired: true, optionsProvider: 'stage-plan.substeps',
    visibleWhenFieldCode: 'stage', visibleWhenValuesJson: '["true"]', visibleWhenAttribute: 'has_substeps' },
  { code: 'level', label: 'الدور الذي تم العمل عليه اليوم', labelEn: 'Level worked today', fieldType: 'Lookup', isRequired: true, lookupFormDefinitionId: floors.id,
    optionsProvider: 'stage-plan.floors', visibleWhenFieldCode: 'stage', visibleWhenValuesJson: '["one_level"]', visibleWhenAttribute: 'unit_pick' },
  { code: 'floors', label: 'الأدوار التي تم العمل عليها', labelEn: 'Floors worked today', fieldType: 'MultiLookup', isRequired: true, lookupFormDefinitionId: floors.id,
    optionsProvider: 'stage-plan.floors', visibleWhenFieldCode: 'stage', visibleWhenValuesJson: '["floors"]', visibleWhenAttribute: 'unit_pick' },
  { code: 'sides', label: 'الواجهات', labelEn: 'Sides worked today', fieldType: 'MultiSelect', isRequired: true, optionsProvider: 'stage-plan.sides',
    visibleWhenFieldCode: 'stage', visibleWhenValuesJson: '["sides"]', visibleWhenAttribute: 'unit_pick' },
  { code: 'plan_met', label: 'هل تم إنجاز المخطط لليوم؟', labelEn: "Was today's planned work met?", fieldType: 'Boolean', isRequired: true },
  { code: 'finished', label: 'هل اكتملت المرحلة اليوم؟', labelEn: 'Did this stage finish today on the selected floors or sides?', fieldType: 'Boolean', isRequired: true },
  { code: 'has_problem', label: 'هل توجد مشاكل؟', labelEn: 'Any problems today?', fieldType: 'Boolean', isRequired: true },
  { code: 'problem_details', label: 'وصف المشكلة', labelEn: 'Problem description', fieldType: 'LongText', isRequired: true,
    visibleWhenFieldCode: 'has_problem', visibleWhenValuesJson: '["true"]' },
])
  await ensureField(report, { ...f, ...inBlock });
report = await call('GET', `/forms/${report.id}`);
{
  const project = editable(report).fields.find((f) => f.code === 'project');
  if (project && !project.rowFilterJson)
    await call('PUT', `/forms/${report.id}/fields/${project.id}/settings`, {
      formula: null, rowFilterJson: JSON.stringify({ field: 'status', values: ['planning', 'active', 'on_hold'] }), visibleWhenAttribute: null, optionsProvider: null,
    });
  if (project && !(project.validationRulesJson ?? '').toLowerCase().includes('blockwhen'))
    await setRules(report, project, {
      blockWhenAttribute: 'status', blockWhenValues: ['on_hold'],
      blockMessage: 'هذا المشروع متوقف مؤقتاً، ويمكن إرسال التقارير بعد استئنافه.',
      blockMessageEn: 'This project is paused. Reports can be sent once it is resumed.',
    });
}

// English labels on the forms' older fields, so the English screens read in English.
log('english labels');
async function englishOptions(form, code, map) {
  const f = editable(form).fields.find((x) => x.code === code && x.isActive);
  if (!f?.optionsJson) return;
  const current = JSON.parse(f.optionsJson);
  if (current.every((o) => o.labelEn)) return;
  await call('PUT', `/forms/${form.id}/fields/${f.id}/option-labels`, { options: current.map((o) => ({ value: o.value, label: o.label, labelEn: o.labelEn ?? map[o.value] ?? null })) });
}
async function englishLabels(form, map) {
  for (const f of editable(form).fields.filter((x) => x.isActive && !x.labelEn && map[x.code]))
    await call('PUT', `/forms/${form.id}/fields/${f.id}/label`, { label: f.label, labelEn: map[f.code] });
}
report = await call('GET', `/forms/${report.id}`);
if (!report.nameEn) await call('PUT', `/forms/${report.id}/name`, { name: report.name, nameEn: 'Daily progress report' });
await englishLabels(report, {
  report_date: 'Report date', project: 'Project', crew_count: 'Workers on site today', weather: 'Weather',
  had_delay: 'Was there a delay or stoppage today?', delay_cause: 'Cause of the delay', site_photo: 'Site photo',
});
await englishOptions(report, 'weather', { clear: 'Clear', rain: 'Rain', extreme_heat: 'Extreme heat', sandstorm: 'Sandstorm', other: 'Other' });
projects = await call('GET', `/forms/${projects.id}`);
if (!projects.nameEn) await call('PUT', `/forms/${projects.id}/name`, { name: projects.name, nameEn: 'Projects' });
await englishLabels(projects, {
  client_name: 'Client', project_type: 'Project type', project_manager: 'Project manager', total_floors: 'Number of floors',
  expected_completion: 'Expected completion', notes: 'Notes',
});
await englishOptions(projects, 'project_type', { residential: 'Residential', commercial: 'Commercial', infrastructure: 'Infrastructure', industrial: 'Industrial', other: 'Other' });
floors = await call('GET', `/forms/${floors.id}`);
if (!floors.nameEn) await call('PUT', `/forms/${floors.id}/name`, { name: floors.name, nameEn: 'Project floors' });
await englishLabels(floors, { project: 'Project' });

// ------------------------------------------------------------------ 5. flags
log('flags');
for (const code of ['projects', 'project-floors', 'stage-plan-default', 'stage-plan-project', 'daily-progress-report', 'project-pause-log']) {
  const f = await formByCode(code);
  if (f && !f.isSystem) await call('PUT', `/forms/${f.id}/flags`, { isSystem: true, isHidden: !!f.isHidden });
}
for (const code of ['project-zones', 'project-footings']) {
  const f = await formByCode(code);
  if (f && !f.isHidden) await call('PUT', `/forms/${f.id}/flags`, { isSystem: !!f.isSystem, isHidden: true });
}

// ------------------------------------------------------------------ project 119
if (do119) {
  log('project 119');
  const page = await call('GET', `/forms/${projects.id}/submissions?pageSize=200`);
  const p119 = page.items.find((r) => r.values.project_code === '119');
  if (!p119) throw new Error('Project 119 not found.');

  // Its floors: PG is the parking level, 01-05 floors 1-5 (owner, 2026-11-10).
  const floorPage = await call('GET', `/forms/${floors.id}/submissions?pageSize=200`);
  const floorFields = editable(floors).fields.filter((f) => f.isActive);
  const levelIds = {};
  for (const row of floorPage.items.filter((r) => r.values.project === p119.id)) {
    const code = String(row.values.floor_code ?? '');
    const level = code.toUpperCase() === 'PG' ? 0 : Number(code);
    if (!Number.isInteger(level)) continue;
    levelIds[level] = row.id;
    if (row.values.level_number === level) continue;
    const values = Object.fromEntries(floorFields.map((f) => [f.code, row.values[f.code] ?? null]));
    Object.assign(values, {
      level_number: level, level_kind: level === 0 ? 'parking' : 'apartment', label_en: level === 0 ? 'Parking' : `Floor ${level}`, is_active: true,
    });
    await call('PUT', `/forms/${floors.id}/submissions/${row.id}`, values);
    log(`  floor ${code} -> level ${level}`);
  }

  if (p119.values.parking_levels == null) {
    const projectFields = editable(projects).fields.filter((f) => f.isActive && f.fieldType !== 'Attachment' && !f.formula);
    const values = Object.fromEntries(projectFields.map((f) => [f.code, p119.values[f.code] ?? null]));
    Object.assign(values, {
      plot_area_m2: 600, buildable_pct: 75, parking_levels: 1, apartment_floors: 5, apartments_per_floor: 4, walls_per_floor: 128,
      needs_deconstruction: false, needs_support: false, outside_sides: 4,
    });
    await call('PUT', `/forms/${projects.id}/submissions/${p119.id}`, values);
    log('  setup fields filled (its floors and plan follow)');
  }
  await call('POST', `/stage-progress/projects/${p119.id}/setup`);

  // The starting position (owner, 2026-11-10): everything before Floor 4's structure finished;
  // structure on Floor 4 and walls on Floor 2 at day 4 of their days.
  const done = (stage, substep = null, level = null) => ({ stageCode: stage, substepCode: substep, levelId: level, side: null, earnedDays: 0, finished: true });
  const plan = JSON.parse(readFileSync(new URL('../../src/Platform.Application/StageProgress/default-stage-plan.json', import.meta.url), 'utf8'));
  const entries = [
    ...plan.filter((r) => ['paperwork_1', 'paperwork_2', 'paperwork_3', 'excavation', 'foundation'].includes(r.stage_code)).map((r) => done(r.stage_code, r.substep_code)),
    ...[0, 1, 2, 3].map((l) => done('structure', null, levelIds[l])),
    ...[0, 1].map((l) => done('walls', null, levelIds[l])),
    { stageCode: 'structure', substepCode: null, levelId: levelIds[4], side: null, earnedDays: 4, finished: false },
    { stageCode: 'walls', substepCode: null, levelId: levelIds[2], side: null, earnedDays: 4, finished: false },
  ];
  if (entries.some((e) => e.levelId === undefined)) throw new Error('A floor of 119 is missing.');
  const result = await call('PUT', `/stage-progress/projects/${p119.id}/baseline`, entries);
  const progress = await call('GET', `/stage-progress/projects/${p119.id}`);
  log(`  starting position: ${result.count} units, ${progress.earnedDays} of ${progress.plannedDays} days`);
}
log('done');
