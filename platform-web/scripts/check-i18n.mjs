// Fails the build when en.json and ar.json disagree (owner, 2026-11-06: "a test that fails the
// build"). Keys are compared after removing i18next plural suffixes, because Arabic needs more
// plural forms (_zero/_two/_few/_many) than English (_one/_other). Also checks that every
// plural group has its _other form in both languages and that no value is empty.
import { readFileSync } from 'node:fs';

const load = (lang) => JSON.parse(readFileSync(new URL(`../src/i18n/locales/${lang}.json`, import.meta.url), 'utf8'));
const flatten = (obj, prefix = '') =>
  Object.entries(obj).flatMap(([k, v]) => (v && typeof v === 'object' ? flatten(v, `${prefix}${k}.`) : [[`${prefix}${k}`, v]]));
const PLURAL = /_(zero|one|two|few|many|other)$/;

const problems = [];
const langs = { en: flatten(load('en')), ar: flatten(load('ar')) };
const base = {};
for (const [lang, entries] of Object.entries(langs)) {
  base[lang] = new Set(entries.map(([k]) => k.replace(PLURAL, '')));
  for (const [k, v] of entries) if (typeof v !== 'string' || v.trim() === '') problems.push(`${lang}: ${k} is empty`);
  const keys = new Set(entries.map(([k]) => k));
  for (const k of keys) {
    if (PLURAL.test(k) && !keys.has(k.replace(PLURAL, '_other'))) problems.push(`${lang}: ${k} has no _other form`);
  }
}
for (const k of base.en) if (!base.ar.has(k)) problems.push(`ar is missing ${k}`);
for (const k of base.ar) if (!base.en.has(k)) problems.push(`en is missing ${k}`);

if (problems.length) {
  console.error(`i18n parity check failed (${problems.length}):\n  ${[...new Set(problems)].join('\n  ')}`);
  process.exit(1);
}
console.log(`i18n parity OK: ${base.en.size} keys in en and ar.`);
