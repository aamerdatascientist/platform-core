#!/usr/bin/env node
// Splits the Daily Progress Report into its three filling steps (redesign HANDOFF §9.2):
//   1 الأساسيات / Basics    - report date, project, workers, weather
//   2 التقدم / Progress     - everything after weather up to the photo (phase ... planned-work question)
//   3 الصور / Photos        - the attachment field(s)
// Steps are presentation only: no column, table or stored answer is touched.
//
// Runs through the API as an administrator, never against the database directly:
//   API_BASE=https://<api-host> ADMIN_TOKEN=<access token> node scripts/data/seed-daily-report-steps.mjs [--dry-run]
// The token is read from the environment and never printed. Safe to run twice: a form that
// already has steps is left alone.

const FORM_CODE = 'daily-progress-report';
const BASICS = ['report_date', 'project', 'crew_count', 'weather'];
const STEPS = [
  { titleAr: 'الأساسيات', titleEn: 'Basics' },
  { titleAr: 'التقدم', titleEn: 'Progress' },
  { titleAr: 'الصور', titleEn: 'Photos' },
];

const base = (process.env.API_BASE ?? '').replace(/\/+$/, '');
const token = process.env.ADMIN_TOKEN ?? '';
const dryRun = process.argv.includes('--dry-run');
if (!/^https?:\/\//.test(base) || !token) {
  console.error('Set API_BASE (http/https) and ADMIN_TOKEN.');
  process.exit(1);
}

async function call(method, path, body) {
  const res = await fetch(`${base}/api${path}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, ...(body ? { 'Content-Type': 'application/json; charset=utf-8' } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${(await res.text()).slice(0, 300)}`);
  return res.status === 204 ? null : res.json();
}

const forms = await call('GET', '/forms');
const summary = forms.find((f) => f.code === FORM_CODE);
if (!summary) throw new Error(`No form with code ${FORM_CODE}.`);
const form = await call('GET', `/forms/${summary.id}`);
const version = form.status === 'Published' ? form.publishedVersion : form.draftVersion;
if (!version) throw new Error('The form has no version to edit.');
if ((version.steps ?? []).length > 0) {
  console.log(`${FORM_CODE} already has ${version.steps.length} steps - nothing to do.`);
  process.exit(0);
}

const fields = version.fields.filter((f) => f.isActive).sort((a, b) => a.displayOrder - b.displayOrder);
const missing = BASICS.filter((code) => !fields.some((f) => f.code === code));
if (missing.length) throw new Error(`Expected fields not found: ${missing.join(', ')}`);
const plan = fields.map((f) => ({
  field: f,
  step: BASICS.includes(f.code) ? 0 : f.fieldType === 'Attachment' ? 2 : 1,
}));

for (const [i, s] of STEPS.entries()) {
  console.log(`${i + 1} ${s.titleAr} / ${s.titleEn}: ${plan.filter((p) => p.step === i).map((p) => p.field.code).join(', ')}`);
}
if (dryRun) process.exit(0);

const created = [];
for (const s of STEPS) created.push(await call('POST', `/forms/${summary.id}/steps`, s));
for (const p of plan) await call('PUT', `/forms/${summary.id}/fields/${p.field.id}/step`, { stepId: created[p.step].id });

const check = await call('GET', `/forms/${summary.id}`);
const after = check.status === 'Published' ? check.publishedVersion : check.draftVersion;
const placed = after.fields.filter((f) => f.isActive && f.stepId).length;
console.log(`Done: ${after.steps.length} steps, ${placed} of ${fields.length} active fields placed.`);
