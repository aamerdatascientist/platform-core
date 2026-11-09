# Asas v2.0

**Status (2026-11-07): complete on staging, not in production.** Everything below runs at
`https://staging.asasksa.co` from branch `feature/redesign` (= `staging`, head `9cb19c9` before this
document). Production (`https://asasksa.co`, branch `main`) is still at `8544610` (2026-11-03) and
gets v2.0 only on the owner's explicit word, using the checklist in section 7.

v2.0 is two bodies of work that were built on staging one after the other:

- **Part A - new features (2026-10-26 to 2026-11-06):** stricter lookups, daily form assignments,
  reminders on Telegram and WhatsApp, and stakeholder PDF reports with a written analysis by Claude.
- **Part B - the redesign (2026-11-06 to 2026-11-07):** the whole web app rebuilt to the approved
  design in `docs/redesign/HANDOFF.md`, in ten phases, with new backend pieces (form steps, English
  labels, My entries, record search, manual reminders, offline sending).

Version 1.x is what production runs today: the form engine and builder, records, field rules,
access control, workflows, files, Arabic/English, and the Executive overview with the client's plan
and lateness KPIs (released 2026-11-03 by cherry-pick).

Related documents: `docs/redesign/HANDOFF.md` (the approved design spec), `docs/redesign/PLAN.md`
(phase 1 audit + the exact schema/API change per phase), `CLAUDE.md` (architecture and rules),
`docs/STAGING.md` (environments and every setting), `docs/PROJECT_STATUS.md` (narrative).

---

## 1. What a user sees in v2.0

| Who | What's new |
| --- | --- |
| Everyone | A **Today** home page: what's due today, the last two weeks as coloured day cells, other forms. A phone layout with a bottom tab bar (Today, Forms, My entries, Settings) and a desktop layout with a side menu and Ctrl/⌘K search. |
| Everyone | **Forms filled in steps** on a phone, with a draft saved as you go, a review before sending, and a "Sent" screen. Fields have proper controls (date quick pick, stepper, yes/no, chips, a search sheet for projects). |
| Everyone | **Works without signal**: an entry made offline is kept on the phone and sent by itself later, counted for the day it was filled in. |
| Everyone | **My entries**: your own sent entries, newest first, each one opens read-only. |
| Everyone | Form names, field labels, options and step titles in **English and Arabic**; the app never mixes the two. Light, dark or "Match phone". |
| Everyone | **Reminders** on Telegram and WhatsApp when a daily form isn't filled yet (09:00 and 14:00 Saudi time on working days). |
| Administrators | **Records** tab on each form: search, project / phase / delay / date filters, choose columns, a side drawer with the answers, photos, workflow, edit and delete. |
| Administrators | **Form builder** rebuilt: fields grouped by step, a settings panel, a live phone preview, English names. |
| Administrators | **Users & roles** with a drawer per person (roles, WhatsApp number + test, Telegram, password reset, deactivate). |
| Administrators | **Form assignments**: assign a form to people to fill every working day, a grid of who filled what for the last 14/30/60 days, and "Remind those who haven't filled". |
| Administrators | **Stakeholder reports**: daily, weekly and monthly PDF reports with Claude's written analysis, sent on WhatsApp at 18:00. |
| Administrators | **Executive overview** restyled (same numbers). |
| Project managers | Today, the daily progress report, their own entries and Settings - nothing else, enforced by the server. |

---

## 2. Part A - features built before the redesign

### A1. Lookups must exist and belong to the chosen parent; "no duplicates" rule - 2026-10-26
`4446b91`, `3e3f3eb`
- A Lookup answer must be a real, live record of the target form, and when the lookup is filtered by
  another field (e.g. zone by project), it must belong to that parent. Checked on the server.
- Optional text rules `unique` and `uniqueWithin` (e.g. a floor code unique within its project).
  `scripts/go-live/apply-field-rules.ps1` sets them for project, zone, floor and footing codes.

### A2. Executive overview: the client's plan and lateness KPIs - 2026-11-02/03 (already in production)
`2e73888` `8c40205` `4d7ba56` `beec0d1` `facd610` `54423bb` - cherry-picked to `main` on 2026-11-03.
Work complete measured against the client's plan, forecast finish, days late, lateness, a stage
filter. Listed here only because the same changes sit on both branches under different commit ids.

### A3. Daily form assignments - 2026-11-03
`dadb038`, `59d5ade`. Migration `AddFormAssignments`.
- An administrator assigns a published form to users from a start date (optional end date). Each
  working day (Saturday-Thursday, Saudi calendar) the user is due to submit it once.
- "Filled" is read from the form's own table (a row the user created that Saudi day) - nothing is
  stored twice. Day status: filled / missed / pending (today).
- Refuses unpublished forms, inactive users and users without access to the form.
- API `api/form-assignments`: `GET mine` (anyone), `GET /`, `GET days`, `POST /`, `POST {id}/stop`.

### A4. WhatsApp reminders - built 2026-11-03, switched on 2026-11-07
`43dd17b`, `f809bd5`, `44bb525`, `9cb19c9`. Migration `AddWhatsAppNotifications`.
- Meta Cloud API template messages: `form_assigned` when someone is assigned, `form_reminder` at
  the reminder times to anyone whose form for today isn't filled.
- Every send is a `NotificationLog` row written before sending; its unique index is the
  "send at most once" lock across restarts.
- Numbers are stored as international digits (05XXXXXXXX becomes 9665XXXXXXXX).
- Meta approved the three templates on 2026-11-07; the app's switch `WHATSAPP_READY` is now `true`.
  The owner confirmed it works on staging the same day. Staging still sends
  from Meta's test number (5 confirmed recipients); production needs a business number
  (section 7, step 5).

### A5. Telegram reminders - 2026-11-05
`4c019bf`, `03e04df`, `da78671`. Migration `AddTelegramNotifications`.
- Free, no approvals. Each person's Telegram chat (usually a small group the administrator makes
  per engineer: Users > Telegram > Link to a group) gets the same messages, with a button to the form.
- A message goes on every channel that is set up on the server and linked for that person.
- One bot per environment (two servers polling one bot steal each other's messages).

### A6. Stakeholder reports - 2026-11-06
`c3b3c39`, `627ad4d`, `09c5956`, `1e24eea`. Migration `AddStakeholderReports`.
- Daily (working days), weekly (Thursday) and monthly (last day) at 18:00 Saudi: dashboard figures
  plus a written analysis by Claude Opus 5.5, as an A4 PDF and a phone PDF (Gotenberg on Railway).
  The phone PDF goes to each recipient as a WhatsApp document (`stakeholder_report` template).
- Honesty and safety: Claude gets the figures only and must copy numbers, never compute them; any
  number not in the figures, any link, e-mail or markup makes it retry once, then the report goes
  out without the analysis. Form text is shortened and treated as data.
- Budget: hard cap $15 a month (worst case reserved before each call). Claude is reached through a
  small relay in Singapore, because Anthropic refuses the API's Hong Kong region.

---

## 3. Part B - the redesign, phase by phase

Spec: `docs/redesign/HANDOFF.md` (approved by the owner 2026-11-06, "build it exactly as shown",
18 screens). Every phase ended with: build, tests, a check of every screen at 390×844 and 1440×900
in Arabic/English × light/dark locally, deploy to staging, the same check live, then a report.

### Phase 1 - audit and plan (2026-11-06)
`1dee83f`. Mapped every handoff section to real files, listed ten conflicts and the plan for each
(`docs/redesign/PLAN.md`). Owner decisions taken with it:
1. Admin pages back in the desktop side menu under "Administration" (reverses the 2026-10-21 decision).
2. Forms are filled in steps on a phone.
3. Every name, label, option and step title has English and Arabic.
4. My entries for every user, own rows only, enforced on the server - and record detail requires
   form access as well as ownership.
5. Offline queue.
6. Executive overview stays at 1760×990 (the 1600×900 design × 1.1).
7. "Remind those who haven't filled" works once per person per day, separate from the automatic reminders.
8. The server's `CreatedAtUtc` stays server time; the phone's entered-at time is stored separately.

### Phase 2 - foundation (2026-11-06)
`2f565ac`, `586a544`
- The handoff's colour, radius and type tokens (light and dark), Tajawal 400-800, no uppercase
  headings, visible focus everywhere, motion that switches off under "reduce motion".
- "Match phone" theme is the default for new users (set before first paint in `public/boot.js`).
- The handoff's strings merged; `scripts/check-i18n.mjs` runs first in `npm run build` and fails the
  build on any Arabic/English key gap (866 keys each today).
- `components/icons.tsx` (inline SVG, flipped for Arabic) and `components/ui/`: buttons, chips,
  yes/no, segmented control, stepper, date quick pick, lookup row, field shell with errors, bottom
  sheet / dialog / drawer (focus trap, Esc, focus returns), cards, pills, banners, empty state, day
  cells, table pieces.

### Phase 3 - shells and routes (2026-11-06)
`6968f46`, `a3f6e60`
- Below 1024px: bottom tab bar and a Forms tab (opens the form directly when there is only one).
  From 1024px: 264px side menu (Today with its count, forms by module, Administration, the person,
  Settings) and a Ctrl/⌘K command palette that also works on an Arabic keyboard.
- Routes: `/assignments` → `/`, new `/forms`, `/entries`, `/account` restored as change password.
- iPhone safe area (`viewport-fit=cover`); radius utilities renamed `rounded-ui-*` (the old names
  collided with Tailwind's own `rounded-r-*`).

### Phase 4 - Sign in, Today, Settings, Sent (screens 01, 02, 07, 09, 10) (2026-11-06/07)
`068c723`, `7a3316b`, `da0aa57`
- Sign in with the button in the thumb zone; Today (greeting by the phone's clock, due cards, the
  14-day strip, other forms); Settings (account, reminders, language, theme); Sent screen.
- Measured against the reference HTML: 17px phone inputs, pill paddings, row sizes.

### Phase 5 - form filling (screens 03-07, 11) (2026-11-07)
`37678ae`, `bdd7231`
- One control per field type (handoff §7), the lookup search sheet with Recent and "Your last
  choice", conditional blocks, validation on Send then on leaving a field with focus on the first
  problem, a full-screen step header and sticky Send on a phone, one 720px card on desktop.
- Fix found here: `html, body { overflow-x: clip }` - `hidden` made the body a scroll container and
  broke `position: sticky`.

### Phase 6 - form steps, English labels, the builder (screens 03-06, 21) (2026-11-07)
`877bbfa`, `b9e5b1f`, `77e0675`, `01fc25e`. Migration `AddFormStepsAndEnglishLabels`.
- Backend: `FormDefinitions.NameEn`, `FieldDefinitions.LabelEn` and `StepId`, new `FormSteps`
  table; dropdown options gain an optional `labelEn`; endpoints to rename a form, set option labels,
  add / rename / reorder / delete steps and move a field to a step - all safe on live forms. No
  `Data_*` table or `Report_*` view touched. 16 tests.
- Filling: one step per screen with progress, Next checks only that step, review cards with Edit,
  drafts autosaved 500ms after a change (photos in IndexedDB), resumed where you left off.
- Every name/label/option/step follows the UI language and falls back to the other one, never blank.
- Builder (21): fields by step with drag and drop, a settings panel replacing the old per-field
  dialogs (same API calls and confirmations), a live phone preview in either language.
- Data: the daily progress report split into Basics / Progress / Photos on staging with
  `scripts/data/seed-daily-report-steps.mjs` (through the API; presentation only).

### Phase 7 - My entries and Records (screens 08, 20) (2026-11-07)
`1f28824`, `8b56693`. No migration.
- Backend: `IDynamicDataRepository.SearchAsync` / `GetRecordAsync` (filters, escaped text search,
  date ranges, own rows, cursor - every identifier checked, every value a parameter).
  `GET api/forms/{id}/submissions` gains `q`, `from`, `to`, `dateField`, `f=code:value` and returns
  sender, resolved lookup labels (no more raw GUIDs) and attachment counts.
  New `GET api/forms/{formId}/submissions/{recordId}` (sender or admin, plus form access) and
  `GET api/submissions/mine`. 13 tests, including 403s.
- My entries (08) grouped by Saudi day with summaries and pills, 20 at a time; entry detail.
- Desktop form page with Fill | Records tabs (in the URL); Records filters, column picker, drawer.

### Phase 8 - admin pages (screens 22-25) and builder home (2026-11-07)
`00b3f5c`, `c824e60`. No migration.
- Backend: `POST api/form-assignments/remind-pending` (kind `manual`, once a day per person/form/
  channel); grid days carry the entry id and "Reminded at". 5 tests.
- Users & roles (22), Form assignments (23 - the old "by day" page merged in and redirected),
  Stakeholder reports (24 - raw errors only behind "Show reason"), builder home, Executive
  overview restyle (25 - calculations untouched).
- **Sign-in fix** (owner's iPhone showed "couldn't reach the server"): reads and sign-in/refresh are
  retried once on a dropped connection, and an answer from the server is never shown as "offline".

### Phase 9 - offline queue (screen 12) (2026-11-07)
`1428883`, `233030e`. Migration `AddSubmissionReceipts`.
- Backend: `Idempotency-Key` header on sending a form - the receipt is claimed before the row, so a
  repeat returns the same record and never makes a second one. `X-Entered-At` (the phone's time) is
  validated (≤ 5 minutes ahead, ≤ 3 days old), kept on the receipt and used for the assignment day
  and for display. 9 tests.
- App: an entry with no connection (or no answer) is kept in IndexedDB with its photos and sent on
  app open, when the connection returns, every 60 s and on "Try sending now". "Saved on this phone"
  screen, waiting list, offline banner, "Sent: n saved entries".
- Background Sync is not used: the sign-in token lives in localStorage, which a service worker
  can't read.

### Phase 10 - polish and the acceptance checklist (2026-11-07)
`526625d`. No backend change.
- Every page that loads data has a loading state (announced to screen readers) and an error state
  with "Try again" that recovers without a reload; empty states say what will fill them.
- The loading mark stands still under "reduce motion" (it used to vanish).
- Phone: every tappable element has at least a 44×44 hit area (drawn sizes unchanged), fields
  44px, the assignments grid 44px cells; "Stop" always visible (no hover on a phone).
- Arabic data on English screens is isolated in its own direction (chips, "Choose …", headers,
  role descriptions); language names carry their own language.
- Checked against the handoff's acceptance checklist (section 6 below).

### After phase 10 - fixes the owner found on staging (2026-11-07)
- `44bb525`: every WhatsApp control shown, saying "not set up yet" until Meta approved the templates.
- `40898d2`: **Form assignments page 500.** Phase 9 reads three days past a date range; the list asks
  for an open-ended range, and adding days to `DateOnly.MaxValue` threw. The list now reads up to
  today and the tracker clamps (`AddDaysClamped`). Two regression tests run the real handler.
- `ea1b7b4`: "Assign a form" card full width on a phone (it kept its desktop 380px cap).
- `9cb19c9`: WhatsApp switched on; Settings and the user drawer show where messages go instead of
  a "Connect" button (messages go to the number on the account).

---

## 4. Database changes - six migrations, all additive

Applied to staging in this order (each after a backup). **None is on production yet.** None
touches a `Data_*` table or a `Report_*` view.

| # | Migration | Adds |
| --- | --- | --- |
| 1 | `20261103115509_AddFormAssignments` | table `FormAssignments` |
| 2 | `20261103124525_AddWhatsAppNotifications` | `Users.PhoneNumber`, table `NotificationLogs` |
| 3 | `20261105193824_AddTelegramNotifications` | `Users.TelegramChatId`, `TelegramLinkCode`, `TelegramLinkCodeExpiresAtUtc`; `Channel` in the send-once index |
| 4 | `20261106144004_AddStakeholderReports` | tables `ReportRecipients`, `ReportRuns`, `ReportDeliveries` |
| 5 | `20261107020113_AddFormStepsAndEnglishLabels` | `FormDefinitions.NameEn`, `FieldDefinitions.LabelEn` + `StepId`, table `FormSteps` |
| 6 | `20261107104800_AddSubmissionReceipts` | table `SubmissionReceipts` (unique `UserId, IdempotencyKey`) |

Production's last migration is `20261004120000_AddFieldDynamicOptionsSource`. Script all six at once:

```
dotnet ef migrations script 20261004120000_AddFieldDynamicOptionsSource 20261107104800_AddSubmissionReceipts \
  --idempotent --project src/Platform.Infrastructure --startup-project src/Platform.Api -o v2-migrations.sql
```

(design-time needs `Jwt__Secret=...`, `ConnectionStrings__PostgresConnection=Host=localhost...` and
`ASPNETCORE_ENVIRONMENT=Development` in the environment; nothing connects. On the Mac also
`DOTNET_ROOT=$HOME/.dotnet`. Checked 2026-11-07: the script holds exactly these six migrations and
nothing that drops a table or column or touches a `Data_*` table.)

---

## 5. API changes (all additive - old callers keep working)

| Endpoint | Who | Since |
| --- | --- | --- |
| `GET api/form-assignments/mine` | anyone signed in | A3 |
| `GET/POST api/form-assignments`, `GET .../days`, `POST .../{id}/stop` | admins | A3 |
| `POST api/form-assignments/remind-pending` | admins | phase 8 |
| `PUT api/users/{id}/phone`, `POST api/users/{id}/whatsapp-test` | admins | A4 |
| `GET/POST link/DELETE api/me/telegram` | anyone signed in | A5 |
| `POST api/users/{id}/telegram-link`, `DELETE .../telegram`, `POST .../telegram-test` | admins | A5 |
| `GET api/reports`, recipients CRUD, `POST run` (10/hour), `GET runs/{id}/download` | admins | A6 |
| `PUT api/forms/{id}/name`, `PUT .../fields/{id}/option-labels`, steps add/rename/order/delete, `PUT .../fields/{id}/step`; label and add-field take `labelEn`/`stepId` | admins | phase 6 |
| `GET api/forms/{id}/submissions` + `q`, `from`, `to`, `dateField`, `f=code:value`; richer items | admins | phase 7 |
| `GET api/forms/{formId}/submissions/{recordId}` | sender or admin, with form access | phase 7 |
| `GET api/submissions/mine` | anyone signed in, own rows | phase 7 |
| `POST api/forms/{id}/submissions` + `Idempotency-Key`, `X-Entered-At` headers | as before | phase 9 |

New error codes: `assignment.*`, `submission.inProgress` (409), `submission.enteredAtInvalid` (400).

---

## 6. How it was tested

- **Backend:** 260 tests; the 242 that don't need Docker pass on this Mac (the 18
  `DynamicSchemaServiceTests` start Postgres through Testcontainers and need Docker).
  Run: `dotnet test --filter "FullyQualifiedName!~DynamicSchemaServiceTests"`.
- **Frontend:** `npm run build` (type check, Arabic/English parity, production build).
- **Browser suites** (Playwright, against a stand-in API and against the live staging site): 1,181
  checks across phases 2-10, WhatsApp and sign-in - every screen in Arabic/English × light/dark at
  390×844 and 1440×900; no page errors, no sideways scrolling, tap targets, labelled controls,
  contrast ≥ 4.5:1, Esc and focus return, reduced motion, error states recovering, drafts, the
  offline queue and idempotent resend.
- **Pixel audits** against the reference HTML: 3 known 1.5px-vs-1px border differences and the
  sheet title 3px higher than drawn - kept deliberately.
- **Real data:** each new read endpoint checked read-only against the staging database through a
  local API.
- **Acceptance checklist** (`HANDOFF.md` §12): screens match; nothing overflows at 375px; RTL/LTR
  mirror; no mixed languages; tap targets ≥ 44; keyboard and focus; screen-reader names; contrast;
  drafts / queue / idempotency; PM role limited with direct URLs refused by the server; existing
  behaviour unchanged; locale parity; build and tests pass.

Deliberate deviations from the drawings: the overview keeps its language and mode toggles; Records
shows the first 9 non-long-text columns by default (spec text); builder home has no drawn screen;
Background Sync not used (above).

---

## 7. Production release checklist - only when the owner says so

1. **Back up production** (`pg_dump -Fc` into the repo root; `*.dump` is git-ignored).
2. **Apply the six migrations** (section 4) to the production database. The API never migrates on
   startup, and the new code queries the new tables and columns.
3. **Merge `feature/redesign` into `main`** and push - this deploys the API and the site. (`main` was
   released by cherry-pick on 2026-11-03, so this is a merge, not a fast-forward; the cherry-picked
   dashboard commits merge as already applied.) Watch both GitHub workflows.
4. **Seed the daily report's steps** on production (presentation only):
   `API_BASE=https://nexus-platform-api-b6cvdedgfdgkgee7.eastasia-01.azurewebsites.net ADMIN_TOKEN=... node scripts/data/seed-daily-report-steps.mjs --dry-run`, then without `--dry-run`.
   Optional: `scripts/go-live/apply-field-rules.ps1` for the duplicate-code rules (A1).
5. **Production settings** on `nexus-platform-api` (the owner sets secrets; see `docs/STAGING.md`):
   - `Notifications__AppUrl` = `https://asasksa.co`
   - Telegram: a **separate production bot** - `Telegram__BotToken`, `Telegram__BotUsername`
   - WhatsApp: a **business number** (not the test number) in the same WhatsApp account as the
     approved templates - `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`; payment method and,
     for higher limits, business verification in Meta
   - Reports: `Reports__PdfServiceUrl`, `Reports__PdfServiceUsername`, `Reports__PdfServicePassword`,
     `Reports__AnthropicApiKey`, `Reports__AnthropicBaseUrl` (the relay), optional
     `Reports__MonthlyBudgetUsd` (15)
   Without these the features stay quiet (nothing is sent); the app works.
6. **Check on production** at 390×844 and 1440×900: sign in, Today, fill and send the daily report
   (and once offline), My entries, Records, the builder, Users, Form assignments, Reports, Overview.
   Send one WhatsApp and one Telegram test message.
7. Tell the client's users once: the app looks different; reminders come on Telegram/WhatsApp.

Nothing else changes: same database, same API host, same deploy pipeline, same sign-in, same
security headers (the site's CSP already lists both API hosts).

---

## 8. Still open after v2.0

1. Production release (section 7) - waiting on the owner.
2. WhatsApp business number for production (staging uses Meta's test number, 5 recipients).
3. Rotate the Railway Postgres password and the Blob Storage key (overdue since go-live).
4. Project 119's expected completion is temporary until the client plans the stages after structure.
5. Older gaps (`docs/PROJECT_STATUS.md`): no workflow designer, no first-admin bootstrap, no builder
   panel for dynamic dropdown sources, a few data-entry rules not yet enforced.
6. Housekeeping: delete the session's database connection files when testing is done.
