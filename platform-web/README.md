# platform-web — the real frontend

**This is the real, live app — not a prototype.** An earlier version of this file
described `App.tsx` as "a deliberately thin shell" proving a form renderer works, with no
nav, no refresh-token handling, no form builder, and attachments rendering as a disabled
placeholder. None of that is true anymore: all of it has since been built, and this app
is what's actually running at **https://asasksa.co**. See the root `README.md` and
`CLAUDE.md`/`docs/PROJECT_STATUS.md` (one level up) for the full project picture — this
file just covers getting the frontend running.

## What's here (Asas v2.0 - the redesign, `docs/redesign/HANDOFF.md`; release notes `docs/releases/ASAS-v2.0.md`)

- **Two shells** (`routes/Layout.tsx`, `components/shell/`): below 1024px a phone layout with a
  bottom tab bar (`TabBar.tsx`: Today, Forms, My entries, Settings); from 1024px a 264px side menu
  (`SideMenu.tsx`: Today, forms by module, Administration for admins) and a Ctrl/⌘K command palette.
  `ShellData.tsx` loads the person, their forms and today's assignments once.
- **Shared components** in `components/ui/` (buttons, chips, yes/no, segmented, stepper, date quick
  pick, lookup row, sheet/dialog/drawer with focus trap, cards, pills, tables, empty / loading /
  error states) and `components/icons.tsx`. Colours, radii and type are tokens in `index.css`
  (copied from the handoff's `tokens.css`) - never raw hex values in components.
- **Pages** (`routes/`): `SignIn`, `Today` (home), `FormsList`, `FormView` (phone: stepped filling;
  desktop: Fill | Records tabs), `MyEntries` + `EntryDetail`, `Settings` + `Account`, and for
  administrators `FormBuilder` + `BuilderHome`, `UserManagement`, `FormAssignments`,
  `StakeholderReports`, `ExecutiveOverview` (standalone 16:9 page).
- `components/FormRenderer.tsx` renders **any** published form from its field metadata - one
  control per field type (`components/form/`), steps, drafts (`forms/draftStore.ts`), review, the
  lookup sheet, conditional blocks. Shared logic in `forms/fieldValues.ts` (visibility and value
  rules that must keep agreeing with the backend's `SubmissionValueValidator` - see CLAUDE.md),
  `forms/localized.ts` (English/Arabic names with fallback), `forms/recordAnswers.ts` (one way to
  show a stored answer).
- **Offline queue**: `forms/offlineQueue.ts` (IndexedDB) and `components/shell/OfflineQueue.tsx` -
  entries made without signal are kept and sent later with an idempotency key.
- Builder: `components/builder/` (fields by step, settings panel, live phone preview).
  `resolveEditableFields` must keep agreeing with the backend's `FormEditTargetResolver`.
- WhatsApp switch: `config/whatsapp.ts` (`WHATSAPP_READY`, on since 2026-11-07).
- Refresh-token handling (`api/client.ts`, single-flight; safe requests retried once on a dropped
  connection), tokens in localStorage shared by tabs (`auth/tokenStore.ts`).
- Arabic/English (`react-i18next`; `npm run build` fails on any key-parity gap via
  `scripts/check-i18n.mjs`), RTL throughout, light/dark/"Match phone" (`public/boot.js` sets it
  before first paint).
- `/dev/ui` (development builds only) shows every shared component.

## Things that break silently if forgotten

- `public/staticwebapp.config.json` carries the site's Content-Security-Policy, which **names
  the API origin**. A new `VITE_API_BASE_URL` needs the same change there.
- No inline `<script>` in `index.html` (the CSP forbids it) — pre-mount code goes in
  `public/boot.js`.
- Arrows and chevrons are SVG, flipped explicitly for Arabic — text glyphs (‹ › ↗) mirror
  inconsistently across browsers in right-to-left text.
- `html, body` use `overflow-x: clip`, not `hidden` - `hidden` makes the body a scroll container
  and breaks every `position: sticky` (step header, Send footer).
- On a phone every tappable element gets a 44×44 hit area from `index.css`; keep new controls
  inside that rule rather than shrinking it.

## Getting it running

```
npm install
npm run dev
```

Create `.env.local` with:
```
VITE_API_BASE_URL=http://localhost:5080
```
(or wherever your local API instance is actually reachable — see the root `README.md`
for backend setup; it talks to the same Railway Postgres production uses, not a local
database).

Dev server runs at `http://localhost:5173`.

## Known, deliberate gaps (not oversights — see root CLAUDE.md for the full reasoning)

- No admin-UI panel yet for configuring a Dropdown's dynamic-options source from the Form
  Builder screen — set via the API/PowerShell scripts today (a Lookup's filter does have
  a panel). Worth adding before the next form that needs either is built by hand
  through the builder instead of a script.
- No workflow-designer UI — workflows are still created via API/PowerShell scripts.
- Lookup display labels are a guess (first `ShortText` field on the target form) — there's
  no designated `DisplayFieldCode` on `FormDefinition` yet.

For anything architectural — why forms work this way, what's verified against the real
production database, what's next — read the root `CLAUDE.md` and
`docs/PROJECT_STATUS.md`, not this file.
