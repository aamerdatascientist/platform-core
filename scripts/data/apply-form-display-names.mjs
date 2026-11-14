#!/usr/bin/env node
// Form display names for the admin menu (admin flows spec F5-4 / F5-5, 2026-11-14), through the
// API as an administrator - never against the database directly. Display names only: form
// codes, tables and data don't change. Safe to run again: a form already named as below is left
// alone, and a form that doesn't exist on this database is skipped.
//
//   API_BASE=https://<api-host> ADMIN_TOKEN=<access token> node scripts/data/apply-form-display-names.mjs [--dry-run]
//
// - stock-inflow / stock-outflow: English names added, Arabic names kept.
// - project-pause-log: "Pause / resume" / "إيقاف واستئناف المشروع", so the menu, the page title and
//   search agree.
// The token is read from the environment and never printed. Node sends UTF-8, so the Arabic is
// safe (the PowerShell encoding traps don't apply here).

const NAMES = [
  { code: 'stock-inflow', name: 'وارد المخزون', nameEn: 'Stock inflow' },
  { code: 'stock-outflow', name: 'صادر المخزون', nameEn: 'Stock outflow' },
  { code: 'project-pause-log', name: 'إيقاف واستئناف المشروع', nameEn: 'Pause / resume' },
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
    headers: { Authorization: `Bearer ${token}`, ...(body !== undefined ? { 'Content-Type': 'application/json; charset=utf-8' } : {}) },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status} ${(await res.text()).slice(0, 400)}`);
  return res.status === 204 ? null : res.json();
}

const forms = await call('GET', '/forms');
let changed = 0;
for (const want of NAMES) {
  const form = forms.find((f) => f.code === want.code);
  if (!form) {
    console.log(`skip   ${want.code}: not on this database`);
    continue;
  }
  if (form.name === want.name && (form.nameEn ?? null) === want.nameEn) {
    console.log(`same   ${want.code}: ${want.nameEn} / ${want.name}`);
    continue;
  }
  console.log(`${dryRun ? 'would ' : ''}rename ${want.code}: "${form.nameEn ?? '-'}" / "${form.name}" -> "${want.nameEn}" / "${want.name}"`);
  if (!dryRun) await call('PUT', `/forms/${form.id}/name`, { name: want.name, nameEn: want.nameEn });
  changed++;
}
console.log(dryRun ? `Dry run: ${changed} to change.` : `Done: ${changed} changed.`);
