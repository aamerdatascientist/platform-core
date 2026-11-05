# Knowledge transfer - Asas / platform-core (as of 2026-11-05)

Hand this file to a new Claude Code session. It summarises everything worked on with the owner
(Aamer) from 2026-11-02 to 2026-11-05, plus the standing context needed to continue safely.
Read it together with `CLAUDE.md` (stable architecture + gotchas), `docs/PROJECT_STATUS.md`
(narrative, newest at the top) and `docs/STAGING.md` (environments). No secrets are in this file.

---

## 1. Ground rules the owner has set (follow these first)

1. **Staging first, production only on the owner's explicit word.** Every change is tried on
   staging (`https://staging.asasksa.co`); the owner reviews it in the app and then says what goes
   to production. "All this on staging not production" was said explicitly for the assignment work.
2. **Back up production before any data change** (pg_dump `-Fc` into the repo root; `*.dump` is
   git-ignored).
3. **Production data: add, never clear.** For the backfill the owner said production gets only
   the inserts; the "replace/clear" step was staging-only. Real entries made by the client must
   never be touched unless the owner asks (e.g. 2026-11-03 floor 04 report: owner said "leave as is").
4. **Commit with explicit file paths.** Never commit these untracked files: `Claude outputs/`,
   `scripts/db/backup-db.ps1`, `scripts/go-live/clear-all-form-data.sql`,
   `scripts/go-live/preview-form-data-counts.sql`, `src/Platform.Api/madina.json`. (A `git add src`
   was refused once because it would have swept in `madina.json`.)
5. Commit messages end with the attribution lines the session's system reminder gives.
6. The owner writes short, informal messages, often with screenshots on the clipboard
   ("check the clipboard"); read the image (`Get-Clipboard -Format Image` > save to scratchpad PNG).
7. The owner wants plain-language answers with clear choices; when asked for options, give costs.

## 2. The product and stack (short)

- **Asas** - an enterprise low-code platform; first customer is a Saudi construction company.
  Live at **https://asasksa.co** (production) and **https://staging.asasksa.co** (staging).
- ASP.NET Core 10 (Clean Architecture, CQRS with MediatR - one file per command/query holding
  Command + Validator + Handler, FluentValidation), EF Core + Npgsql for the static schema,
  Dapper for the **dynamic form tables** (`Data_{Code}`, one real table per form).
- React + TypeScript (Vite, Tailwind 3.4), English/Arabic (RTL) via react-i18next, light/dark.
- Azure App Service (East Asia) for the API, Azure Static Web Apps for the site, Railway Postgres
  (Singapore) for the databases, Azure Blob Storage for attachments.
- Repo: `C:\Users\ShahAamer(Ascend)\platform-core`, GitHub `aamerdatascientist/platform-core`.

## 3. Branches and deploys - IMPORTANT, they have diverged

| Branch | Deploys to | Head (2026-11-05) | Contents |
| --- | --- | --- | --- |
| `main` | production (`nexus-platform-api` + `nexus-platform-web`) | `8544610` | everything up to 2026-10-26 + the 6 dashboard commits (cherry-picked) |
| `chore/dotnet-10` (working branch, local only) | - | `43dd17b` + docs commit | everything |
| `staging` | staging (`asas-api-staging` + `asas-web-staging`) | `43dd17b` | = working branch |
| `release/dashboard-2026-11-03` | - | `8544610` | the cherry-pick branch that became `main` |

- Deploy staging: `git push origin HEAD:staging` (both GitHub workflows run, ~2-3 min).
- Deploy production: push to `main` - **only when the owner says so**.
- Held back from production (on the working branch / staging only):
  - `4446b91`, `3e3f3eb` (2026-10-26) - Lookups must exist and belong to the chosen project;
    optional `unique` / `uniqueWithin` text rule.
  - `dadb038`, `59d5ade` - form assignments + tracking page.
  - `43dd17b` - WhatsApp notifications.
- To release them: back up production, **apply the two new migrations to production first**
  (`20261103115509_AddFormAssignments`, `20261103124525_AddWhatsAppNotifications`; the API never
  migrates on startup), then merge `chore/dotnet-10` into `main`.
- To release only part: branch from `origin/main`, cherry-pick, build + test, push to `main`
  (what was done 2026-11-03).

## 4. Databases and how to reach them

- Production: Railway service `Postgres`, project `abundant-flow`, Singapore.
- Staging: Railway service `Postgres-staging`, same project, Singapore. Separate database -
  never point staging at production.
- From the owner's Windows machine, Claude Code reaches both directly with
  `C:\Program Files\PostgreSQL\18\bin\psql.exe` / `pg_dump.exe` (`PGSSLMODE=require`,
  `PGCLIENTENCODING=UTF8`). Previous sessions kept connection details in the session scratchpad:
  `prod.conn` / `staging.conn` (host, port, db, user - one per line) + `pgpass.conf` (passwords),
  and a wrapper `q.sh <prod|staging> -c "sql"` / `-f file.sql` that sets `PGPASSFILE`. **Those
  files were in the old session's scratchpad; a new session must ask the owner for the
  connection strings again** (the owner copies `DATABASE_PUBLIC_URL` from Railway; never print or
  commit passwords). `dump.sh` is the same wrapper around `pg_dump`.
- Migrations: `dotnet ef migrations script <from> <to> --project src/Platform.Infrastructure
  --startup-project src/Platform.Api`, then run the SQL with psql.
- Claude Code's auto-mode permission check refuses production writes the owner didn't name
  explicitly (it refused the expected-date update until the owner said "yes") - ask, don't
  work around it.

## 5. Data facts (project 119)

- Project `119` (`Data_Projects`, Id `f7b05c91-1bf5-4193-ac45-e981b3830036`), start 2026-01-20,
  **expected completion 2026-09-15** on both databases (was 2027-12-01; changed 2026-11-03).
  That date is temporary, set by the owner's rule: the plan through structure + electrical/
  plumbing = 180 working days, +10% = 198 working days, Fridays and holidays skipped. Later
  stages (finishes etc.) aren't in the client's plan yet.
- Daily progress report form: table `Data_DailyProgressReport`, form version
  `22be464c-4ad7-4028-a63b-bc4baa0cdfcc`. Reporter: **Qasim** (user `fccbf619-1d02-4b00-9830-f827a2fc6362`,
  role Project manager).
- Floors (`Data_ProjectFloors`): PG `ad453fbf...`, 01 `7df57981...`, 02 `8abd250b...`,
  03 `00cd555c...`, 04 `7f47d7bd...`, 05 `24ea6c14...`. One zone `c42fc3ca...`; footings
  `46035b58...` (01), `c4a0f30c...` (00).
- **Real reports on production:** 2026-11-02 floor 03, 2026-11-03/04/05 floor 04 (structural).
  The jump from floor 03 (only 3 days) to floor 04 is correct per the owner.
- **Backfill (generated history):** 273 rows, 2026-01-20 .. 2026-11-01, tagged
  `Extensions = '{"backfill":"2026-11-02"}'`, created as Qasim. On staging since 2026-11-02, on
  production since 2026-11-03 (copied from staging; identical ids). Generator:
  `scripts/data/backfill-project-119.mjs` (deterministic; its output's ids match production's
  rows exactly). Delete all: `DELETE FROM "Data_DailyProgressReport" WHERE "Extensions" = '{"backfill":"2026-11-02"}'`.
  - Owner's rules for it: follow the client's plan; floors PG/01/02 exactly 17 days each; floor 03
    started 2026-10-31; all lateness before the floors (~78 working days late in paperwork, ~34 in
    excavation/foundation); every unit gets exactly its planned met days; electrical + plumbing
    done on PG/01/02 (MEP rooms `floor_1`..`floor_3`, 60 parallel reports); **no problems** on any
    generated row ("we have no proof to back them"); weather mixed with rain on exactly 6 days;
    Saudi holidays skipped (02-22, 03-19..22, 05-26..29, 09-23) and all Fridays.
- Staging differs from production: it has the backfill + the 2026-11-02 real report only (not
  11-03 onwards), one extra user, and the two new tables.
- Backups in the repo root: `asas-staging-backup-2026-11-02-before-replace.dump`,
  `asas-prod-backup-2026-11-03-before-backfill.dump` (plus older `asas-backup-2026-10-2*.dump`).

## 6. Dashboard (Executive Overview) - live on production

`/dashboards/executive-overview`, administrators only, fixed 1760x990 canvas scaled to the window
(no `sm:`/`lg:` classes on it). Code: `src/Platform.Application/Analytics/Queries/GetExecutiveOverview/ExecutiveOverviewCalculator.cs`
(pure logic, unit-tested), `GetExecutiveOverviewQuery.cs`, frontend `routes/ExecutiveOverview.tsx`,
`components/ExecutiveOverviewPanels.tsx`, `components/ExecutiveOverviewCharts.tsx`.

- **Client's plan** (`OverviewProgressPlan`, given 2026-11-02): paperwork - design approval 10,
  permit application 8, utility approvals 5, final permit 3 (26); excavation 18 per zone;
  foundation 14 per footing; structural 17 per floor (owner's figure "~102.5" for 6 floors);
  MEP 6 days per trade per floor (3 trades), alongside structure. `Slack = 0.10`.
- Work complete = each unit's plan-met days capped at its planned days / total planned days.
  Status tile compares work complete with time elapsed (capped at 100%), e.g. "Behind 43 pts".
- **KPIs added 2026-11-03** (owner picked: forecast finish, days late / schedule slip, % of
  lateness, plus a stage filter all KPIs follow; with colours and thresholds):
  - Forecast finish: chain paperwork -> excavation -> foundation -> structural + 6-day MEP tail;
    pace = plan-met share of the last 30 days; counted forward in working days (Friday off).
    Target = expected completion (whole project) or stage start + planned x 1.1. Green on/before
    target, amber <= 30 days late, red beyond. Target date in the tile's hover text.
  - Days late: working days where no report in scope met its plan. Green <= 5, amber <= 15.
  - Lateness: days late / worked days. Green < 10%, amber <= 25%.
  - Thresholds in `OverviewKpiThresholds`; API sends `tone` good/warn/bad; tiles have a
    coloured start edge.
  - Stage filter: `phase` query parameter (`paperwork|excavation|foundation|structural|mep`) on
    `GET /api/analytics/executive-overview` and the public endpoint.
- Chart tweaks 2026-11-02: plan-met line is a rounded monotone curve (owner: "more round"),
  values shown on worker columns. Tiles grid is 8 columns; labels/details/captions wrap to two
  lines (`line-clamp-2`; owner: "do a text wrap on all"). Arabic subtitle fixed to
  "التقارير اليومية من {{from}} حتى {{asOf}}" with `dir="auto"`.
- Weather chart hides a condition with < 5 reports (`MinimumReportsPerWeather`); the owner asked
  why rain disappeared and said "no need" to change it.

## 7. Form assignments - staging only

Owner's request: assign users to fill forms; a page listing assigned users, form, filled or not,
time assigned and time filled. Choices: **recurring daily**, plus a **"My assignments" page**.
Then: move "filled or not" to its own page with **one date filter, default today**.

- Domain `src/Platform.Domain/Forms/FormAssignment.cs` (AuditableEntity): `FormDefinitionId`,
  `UserId`, `StartDate`, `EndDate?`, `Note`; `Stop(today)` (soft delete if not started, else
  EndDate = today). Table `FormAssignments`.
- Working days: Saturday-Thursday, Saudi calendar (UTC+3). Friday only - no holiday list here.
- "Filled" = a row in the form's dynamic table created by that user that Saudi day
  (`IDynamicDataRepository.GetSubmissionStampsAsync`, Dapper). Status filled / missed / pending.
- `src/Platform.Application/Forms/Assignments/`: `FormAssignmentTracker.cs` (pure `BuildDays`,
  `LoadAsync`), queries `GetAssignmentDays`, `GetMyAssignments` (today + 13 days back),
  `GetFormAssignments`; commands `CreateFormAssignments` (refuses unpublished form
  `assignment.formNotPublished`, inactive user `assignment.userInactive`, no access
  `assignment.noFormAccess`; skips overlapping; start >= today) and `StopFormAssignment`.
- API `api/form-assignments`: `GET mine` (anyone signed in); admin: `GET /`, `GET days`,
  `POST /`, `POST {id}/stop`.
- Pages: `/assignments` (`routes/MyAssignments.tsx`, sidebar `ChecklistIcon`),
  `/admin/assignments` (`routes/FormAssignments.tsx`), `/admin/assignments/status`
  (`routes/AssignmentTracking.tsx`; Friday shows "day off" note). Settings page links both.
  Shared `components/AssignmentStatus.tsx`. Translations under `assignments.*` (en/ar).
- Verified by the owner on staging: Qasim assigned, "due today" on his phone (assignment then
  stopped 2026-11-03).

## 8. WhatsApp notifications - staging only, OFF until configured

Owner asked for notifications that are fast, cheap, not email; asked for costs of the options
(SMS, WhatsApp, web push, Telegram ...), chose **WhatsApp** "as we can then add an agent later".

- Meta Cloud API, template messages (~0.04 SAR per utility message in Saudi Arabia).
- `User.PhoneNumber` (international digits, no `+`; `PhoneNumbers.Normalize` turns 05XXXXXXXX into
  9665XXXXXXXX). `PUT /api/users/{id}/phone`, `POST /api/users/{id}/whatsapp-test`. Users page has
  a WhatsApp button (`components/UserPhonePanel.tsx`); assign page marks users with no number in
  red; tracking page has a WhatsApp column (reminders sent/failed).
- `NotificationLog` table (Kind `assigned` / `reminder-N` / `test`, Status sending/sent/failed);
  unique (AssignmentId, Kind, ForDate) = at-most-once across restarts.
- `AssignmentNotifier` (assigned + reminders + test), `WhatsAppSender` (HttpClient),
  `AssignmentReminderService` (BackgroundService, every minute, sends the latest passed reminder
  slot within 2 h; default 09:00 and 14:00 Saudi time, working days, only unfilled).
- Settings section `WhatsApp`: `PhoneNumberId`, `AccessToken` (both required to switch on),
  `AppUrl`, optional `ApiVersion` (v23.0), `TemplateLanguage` (ar), `AssignedTemplate`
  (form_assigned), `ReminderTemplate` (form_reminder), `ReminderTimes` (see the binder caveat in
  `docs/STAGING.md` - overriding probably adds times instead of replacing).
- **Go-live steps for the owner** (full text, including the two Arabic template bodies):
  `docs/STAGING.md` > "WhatsApp". In short: Meta Business app + WhatsApp product, test number
  (5 verified recipients), Phone number ID, two Utility/Arabic templates approved, permanent
  System User token, then `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`,
  `WhatsApp__AppUrl=https://staging.asasksa.co` on `asas-api-staging`; then save a number on the
  Users page and press "Send a test message". The owner said "let's circle back on this later".
- Not tested against Meta yet. Unit tests cover phone normalisation and reminder slots.

## 9. Tests and build

- Tests live in `tests/Platform.Infrastructure.IntegrationTests` (unit + handler tests, and
  Postgres tests that need Docker). Docker doesn't work on the owner's machine, so run:
  `dotnet test tests/Platform.Infrastructure.IntegrationTests --filter "FullyQualifiedName!~DynamicSchemaServiceTests&FullyQualifiedName!~Postgres"`
  -> 133 pass on the working branch, 116 on `main`.
- Frontend: `cd platform-web && npx tsc -b && npx vite build`.

## 10. Session timeline (owner's requests, in order)

- 11-02: check the day's single real entry; copy project + entry to staging and backfill history
  there ("current stage ~3rd floor 5th day"); remove the generated problem comments; rounder chart
  line; electrical/plumbing done up to floor 2; "ahead of plan by 5%" questioned -> rebuilt on the
  client's plan (pasted: paperwork ~26, excavation ~18, foundation ~28, structural ~102.5, MEP 5-7
  per trade per floor); floor 03 started 2 days before; asked the hard-coded total duration.
- 11-02 night: expected end date recomputed from the plan +10%; two new KPIs -> three KPIs + stage
  filter with colours; Claude Code update question ("Restart to update" = quit and reopen);
  forecast tile text cut off -> wrap all; Arabic subtitle order fixed; weather question (no change).
- 11-03: form assignments feature; tracking on its own page with one date filter; notification
  options and costs; WhatsApp built; then: push the backfill + dashboard (only) to production,
  keeping the two real entries; set production's expected date (yes); leave the floor 04 entry.
- 11-05: update all docs and write this file.

## 11. Open items (priority order)

1. Owner reviews assignments + WhatsApp on staging; then the production release (section 3).
2. WhatsApp Meta setup (section 8) - waiting on the owner.
3. Rotate the Railway Postgres password and the Blob Storage key (overdue since go-live).
4. Replace the temporary expected completion once the client plans the stages after structure;
   extend `OverviewProgressPlan` with them.
5. Possible fix: make `WhatsApp:ReminderTimes` replace, not add to, the defaults.
6. Older gaps (see `docs/PROJECT_STATUS.md`): `find-not-null-columns.sql` on production; expected
   completion after start rule; stock outflow over stock in; records page-size cap; site photo
   enforced only by the page; renaming/deleting a field others' visibility depends on; first-admin
   bootstrap; builder panel for dynamic dropdown sources; workflow designer UI.
7. Housekeeping: the old session scratchpad held `pgpass.conf` / `*.conn` with database passwords
   - delete them when no longer needed.
