# Asas UI redesign — mockup flow (2026-11-06)

Mockup canvas: https://claude.ai/artifact/GDpsLin6hZF8Mi8u5yjfFC (19 artboards: 12 phone, 6 desktop, 1 shared side menu).

**Status: approved by Aamer 2026-11-06 ("build it exactly as shown").** A handoff zip for Claude Code was delivered the same day (`asas-redesign-handoff.zip`, meant to live at `platform-core/docs/redesign/`): HANDOFF.md build spec, tokens.css, i18n-additions.json (176 keys, ar/en parity), 18 screen PNGs + static HTML references, and a start prompt. Build order: audit/plan → foundation → shells → sign-in/Today/Settings → form renderer → bilingual labels + steps + builder → My entries + Records → admin pages → offline queue → polish.

## What staging looks like today (walked 2026-11-06, signed in as Admin)
- One long single-column form per page; every yes/no and short list is a native dropdown; records table sits under the form with all 17 fields as columns (overflows on desktop, unusable on phone).
- Phone: hamburger menu, labels left-aligned in English mode even though they are Arabic.
- Form names, field labels and Yes/No options are single-language: Arabic labels with English "Yes/No/Select…" in English mode, and the reverse.
- Admin pages are reachable only through Settings > Administration.
- Builder: 17 fields as rows of small text buttons (Rules, Only show when…, Filter by…, Rename code, Change type, Remove).
- Users: rows wrap inconsistently; actions are inline text links.
- Stakeholder reports: raw API errors shown in the history table.
- Home after sign-in is an empty "Pick a form" page.

## Direction in the mockup
- Phone: bottom tab bar (Today / Forms / My entries / Settings); Today replaces My assignments as home; forms split into steps with a progress bar, draft auto-save, chips/segmented buttons instead of dropdowns, lookups in a searchable bottom sheet, a review step, a clear sent screen; offline queue screen.
- Desktop: one side menu with forms by module plus an Administration group; form pages split into Fill / Records tabs, curated columns + column picker, record drawer.
- Builder: fields grouped by step, settings panel (bilingual labels, "show when" sentence, rules), live phone preview in AR/EN.
- Assignments + daily tracking merged into one who-filled-what grid with a remind button.
- Reports: setup pills, schedule cards with next run, recipients, run-now, readable history with "show reason".
- Look: Tajawal kept; accent darkened to #2E6A9E in light mode for text contrast; dark-mode accent #6AA8DB.

## Decisions (all accepted as shown, 2026-11-06)
1. Admin pages move back into the desktop side menu (reverses the 2026-10-21 "sidebar = forms + Settings" decision).
2. Forms are filled in steps on a phone; the builder gets a step concept.
3. English labels for every form, field, option and step; the UI never mixes languages.
4. "My entries" for every user incl. project managers, own submissions only, enforced server-side.
5. Offline queue on the phone, with an idempotency key on submission so retries never duplicate.
