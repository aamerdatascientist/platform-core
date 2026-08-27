# Project status

Update this file at the end of every session - what changed, what's next. Keep CLAUDE.md
itself stable; this is where the narrative goes.

## Verified end-to-end against a real database - by the project owner, not just Code's sandbox

**All four phases built so far, plus every feature added since, now meet this bar.** All
of the entries below were originally verified against the real Azure SQL database, before
it was decommissioned - the backend has since fully migrated to Postgres (see "Postgres
migration" below, now complete) and been re-verified working end-to-end against the real
Postgres/Railway database as part of that migration. Where a bullet below still says
"Azure DB," that's the historical record of when it was first verified, not a claim about
the current database.

- **Phase 0** (Identity + Form Engine core): register, login, create/publish a form,
  submit data, read it back. Confirmed via the browser frontend and Swagger.
- **Phase 1** (Stock Management, 7 forms): seeded via `scripts/seed-stock-management-forms.ps1`.
  Materials form loaded and rendered correctly in `platform-web` - the first "it works" moment.
- **Phase 2** (Operations, 7 forms): seeded via `scripts/seed-operations-forms.ps1`, loaded
  and submitted through in the real frontend.
- **Phase 3** (Workflow Engine): `scripts/seed-stock-adjustment-workflow.ps1` created the
  real `Draft -> Pending approval -> Approved/Rejected` workflow. A real Stock Adjustment
  record was submitted, walked through `submit-for-approval` then `approve` via the real
  API, and its final state (`approved`, `isFinal: true`) and 3-entry history were confirmed
  correct at every step.
- **`GET /api/forms` (list endpoint)**: done and verified against the real Azure DB.
  `FormPicker` now shows real navigation grouped by module instead of pasted form IDs.
- **Refresh-token flow**: done and verified against the real Azure DB. Rotation and logout
  both confirmed working end-to-end.
- **Frontend app shell (real routing) and workflow status/approval UI**: done, verified
  directly by the project owner in the browser - not via Code's sandbox, since this was
  frontend-only work with no backend changes. Confirmed: routing survives a page refresh,
  role-gated approval buttons correctly appear on draft records and correctly disappear on
  final-state (approved) records.
- **Submission validation** (per-field type and constraint checking): done and verified
  end-to-end against the real Azure DB by the project owner in the browser. Bad values now
  show as a red-highlighted field with a specific message instead of a raw error.
- **File Management** (upload/list/view/delete attachments via Azure Blob Storage):
  done and verified for real - upload, list, view, and delete all confirmed working
  against the real Azure Storage account by the project owner through the actual frontend
  UI, including the inline-during-submit flow (attaching a file while filling out the
  form, not as a separate step afterward). This clears the bar Code's sandbox couldn't
  reach on its own, since the sandbox's network policy blocks the real storage account
  outright - only logic-verified there via the Azurite emulator beforehand.
- **Design refresh**: done and verified in the browser by the project owner - frontend-only,
  no backend changes. White content area, dark navy sidebar, indigo accent, sans-serif UI
  chrome (mono now limited to genuine field codes only, not the whole UI), two-column
  FormView layout with a sticky Workflow/Attachments panel, and the Nexus placeholder
  logo/wordmark.

## Built and verified in Code's sandbox - not yet tested by the project owner against the real database

- **Form Builder UI** (create forms, add/remove fields, publish, edit a published form's
  fields via a new draft version): built and verified end-to-end against a local SQL
  Server in Code's sandbox, including the `StartNewFormVersionCommand`/`DeleteFormCommand`
  work from PR #15 and the FormBuilder draft-status fix from `16a7d52`. Doesn't meet the
  bar above yet - needs a real pass against the real Postgres/Railway database before it
  counts as verified (the target database has changed since this was written - it was
  Azure SQL when this was first built, Postgres now).
- **Form access control** (restrict which roles AND/OR individual users can see/use each
  form): done and verified via SQL/API in Code's sandbox. Role-based: restriction,
  unrestriction, the "open to everyone by default" rule (confirmed by diffing
  `GET /api/forms` before and after the migration: same 16 forms, unchanged), and
  admin-only form building (`Create`/`AddField`/`Publish`/`RemoveField`/`StartNewVersion`/
  `Delete` all correctly `403` for non-admins) all confirmed working. Per-user, layered on
  top: confirmed role-based and user-based access work independently in both directions -
  a direct grant gives access with no matching role at all, and revoking a direct grant
  doesn't touch anyone's role-based access (verified via SQL: revoking one user's
  `FormDefinitionUsers` row left `FormDefinitionRoles` completely untouched, and a
  different user's role-based access kept working the whole time). Not yet tested in the
  actual browser UI.

## What just got fixed along the way (worth knowing, not just "it works now")

- **EF Core tracking bug, hit 5 times across Phases 1 and 3** - see `CLAUDE.md`'s "Known
  engineering gotchas" section for the pattern and the fix. Now an established convention
  in this codebase, not a one-off patch.
- **Access tokens expire after 30 min with no refresh flow** - hit repeatedly during manual
  testing as spurious-looking 401s. Not a bug each time it happens - just re-login. Fixed:
  the refresh-token flow now handles this, see above.
- **`GetDraftVersionOrThrow()` pattern.** `FormDefinition.GetDraftVersion()` throws a plain
  `InvalidOperationException` when a form has no open draft (typically: it's already
  published) - every caller (`RemoveFieldCommand`, `AddFieldDefinitionCommand`,
  `PublishFormVersionCommand`) was letting that bubble straight into the generic 500
  handler, so callers only ever saw a raw `"An unexpected error occurred."` with no useful
  message - the real text only ever reached the server log, never the HTTP response. Fixed
  with `FormDefinition.GetDraftVersionOrThrow()` (`Platform.Application/Forms/
  FormDefinitionExtensions.cs`), used everywhere `GetDraftVersion()` used to be called
  directly - it translates the domain exception into a proper 400 `ValidationException`
  with a readable message instead. Same shape of trap as the EF Core tracking bug above: a
  domain method that can throw on an expected, normal outcome (not a bug) needs every
  caller to guard against it explicitly, or it becomes an unhelpful 500. Watch for this
  pattern in any new code that touches `FormDefinition`/`FormVersion`.
- **FormBuilder draft-status bug, fixed.** The root cause diagnosed earlier -
  `FormBuilder.tsx` deciding which view to show off `formDefinition.status === 'Draft'`
  instead of `draftVersion !== null` - is now fixed and pushed to `main`
  (`16a7d52`). The same commit also carried the remaining font/layout design-refresh
  cleanup that had been staged but not yet committed: `font-mono` is now genuinely
  limited to real field codes across the Form Builder, Form View, Workflow, and
  Attachments UI too, not just the pages the first design-refresh commit touched. Both
  are confirmed present in `main`.

## Immediate next steps, in priority order

The frontend app shell (routing, workflow status/approval UI), File Management, and the
Postgres migration are all fully done. The Form Builder UI is built but not yet fully
done - see below. The "Structural steel" visual direction is implemented and merged, but
not yet visually confirmed by the project owner in a real browser - Code's sandbox
verified it via headless-Chromium screenshots and computed-style checks, not a person
actually looking at it.

1. **Verify the Form Builder UI against the real Postgres/Railway database** - see
   "Built and verified in Code's sandbox" above. Same bar every other phase has already
   cleared; the target database changed (Azure SQL -> Postgres) since this was written.
2. **Verify form access control in the actual browser UI** - SQL/API-verified only so far
   (both role-based and per-user); needs a real pass confirming restricted forms actually
   disappear from navigation, the admin-only actions are hidden or gated correctly in the
   Form Builder UI, and there's a real way for an admin to manage per-user grants, not
   just blocked server-side.
3. **Real-device retest of the mobile horizontal-overflow fix** (PR #28) before merging -
   still open, not addressed this session - see that section below for the preview URL.
   Reported by real users; treat as high priority.
4. **A human look at "Structural steel" in a real browser** - see the note above. Code's
   sandbox verified layout/measurements/colors programmatically, which isn't the same as
   someone actually using it, especially across both languages and both modes together.
5. **Build a real first-admin-bootstrap mechanism** - see the "Known gap" in the Postgres
   migration section below and in CLAUDE.md. Currently a manual one-off SQL insert; would
   need repeating by hand for any future fresh environment.
6. **After those:** Analytics/Reporting (previously "Phase 4" of the backend roadmap) is
   now actively underway - see "Analytics / Reporting direction" below for the full
   phased plan. AI Assistant remains the phase after that.

## Analytics — Metabase and Power BI (SUPERSEDED - see "Analytics / Reporting direction" below)

> The two sections below (Metabase, then a planned shift to Power BI) are kept as
> historical record - the infrastructure details and reasoning are still real, even
> though neither is the current direction. Metabase was superseded by a planned Power BI
> direction; Power BI has since been evaluated in detail and rejected too, in favor of
> building analytics directly into the webapp - see "Analytics / Reporting direction"
> further down for the current plan and the reasoning for moving off Power BI. Don't
> restart either Metabase or Power BI work without reading why each was moved away from.

### Current architecture
- **Metabase itself**: self-hosted on **Railway** (`metabase-production-ebca.up.railway.app`),
  Hobby plan ($5/mo minimum usage credits)
- **Metabase's own app-database**: Railway-managed Postgres, provisioned in the same
  Railway project — internal-only, stores Metabase's own settings/dashboards/users,
  not business data
- **Business data source**: unchanged — Azure SQL `test2` database, connected via
  the `Report_*` views
- **Connection**: `jdbc:postgresql://${{Postgres.PGHOST}}:${{Postgres.PGPORT}}/${{Postgres.PGDATABASE}}?user=${{Postgres.PGUSER}}&password=${{Postgres.PGPASSWORD}}`
  — Railway's variable-reference syntax pulls credentials directly from the paired
  Postgres service, nothing hand-copied
- **Working environment variables on the Metabase service**:
  - `JAVA_OPTS=-Xmx1400m` (heap ceiling; container memory set to 8GB, real usage is
    ~1.5–2GB — see CLAUDE.md's Metabase/Railway gotcha #5)
  - `JAVA_TOOL_OPTIONS=-XX:ActiveProcessorCount=2` (see CLAUDE.md's Metabase/Railway gotcha #6)

### Superseded: Azure App Service + Azure Postgres Flexible Server
This was the original plan and is now abandoned, not paused. Root causes across
multiple real, distinct failures (memory ceiling, connection string format, Postgres
auth mode, blocked extensions, App Service's own container start-timeout behavior,
and at least one still-unexplained silent hang) made this combination specifically
unreliable. See CLAUDE.md's Metabase/Railway gotchas #1–#7 for the individual lessons;
Railway avoids the platform-specific ones entirely (no extension allow-listing, no
Entra-auth default, no App Service container-timeout behavior).
**Follow-up**: the abandoned Azure Postgres Flexible Server resource (`metabase-appdb`)
is no longer used by anything — worth deleting to stop incurring cost, once confirmed
nothing else depends on it.

### Azure SQL side-effect (server-wide, not Metabase-specific)
`construction-site-aamer-shah`'s connection policy was changed from **Redirect** to
**Proxy** to allow Railway (a non-Azure host) to connect at all. This is a server-wide
setting — it now applies to every client connecting to this SQL server, not just Metabase.

### Current connection credentials — TEMPORARY
Metabase is currently connected to `test2` using the **full admin SQL login**, not the
read-only `PowerBIReader` account that was set up earlier. This was a deliberate
short-term choice to unblock testing. **Follow-up**: switch the connection in
Metabase (Admin → Databases → Asas Reports → edit) to `PowerBIReader` once convenient
— low effort, same screen, no rebuild needed. Until then, a Metabase compromise would
expose full read/write access to `test2`, not just read access to the `Report_*` views.

### Dummy/demo data
`test2` was seeded with ~113 rows of realistic construction-industry demo data across
all 14 real business form tables (Materials, Locations, Equipment, Trades, Projects,
GoodsReceipt, MaterialIssue, StockAdjustment, StockTransfer, PhysicalStockCount,
LaborLog, EquipmentLog, DailySiteReport, TaskTracking) — Lookup fields (material,
location, project, trade, equipment) are internally consistent, referencing real
generated rows in their parent tables. `Data_Q`, `Data_Q1`, `Data_Testing` (leftover
scratch forms) were intentionally left empty. The seed script used is a one-off,
transaction-wrapped SQL file — not currently committed to the repo; worth adding to
`/scripts` if it'll be reused (e.g. re-seeding after a schema change).

### What's built and working
- First real Metabase dashboard created manually via the UI (Questions → Dashboard
  workflow), pulling live from `test2` — proof the full pipeline works end to end
- Pattern established for building further Questions: `Summarize` → aggregate + group
  by → pick chart type → save; use `Report_*` views (not raw `Data_*` tables) whenever
  a field is a Lookup, so it resolves to a real name instead of a GUID

### Deliberate gaps / not yet done
- No dashboards built beyond the first manual walkthrough example
- No scheduled reports/alerts configured in Metabase
- `PowerBIReader` credential switch (see above) - **now moot**: `PowerBIReader` was a
  read-only Azure SQL login, and Azure SQL has since been deleted entirely (see the
  Postgres migration section). Nothing to switch to anymore; this item is dead, not just
  deferred.
- Old Azure Postgres resource cleanup (see above) - status unconfirmed, not checked this
  session; don't assume it was done.

## Analytics — status update (reconsidered from previous session)

Metabase-on-Railway (documented above) is still deployed and was working, with one
dashboard built manually as a proof of concept. However, **the direction has since
shifted toward Power BI instead** — Metabase was judged too limited/immature for the
team's needs.

**Decision made**: Power BI, Import mode with frequent scheduled refresh (up to 8x/day
on Pro) — the lower-effort middle ground between a fully live DirectQuery setup and a
one-time static import. A more ambitious pre-aggregated-KPI-table + DirectQuery
approach was considered and rejected for now due to build time, not ruled out
permanently.

**Not yet built**: no Power BI Desktop work has started yet. `PowerBIReader` (the
read-only SQL account created during the Metabase troubleshooting) is still unused and
ready for this purpose.

**Open question, not yet decided**: whether to keep the working Metabase/Railway setup
running in parallel, or wind it down once Power BI is functional. Worth a deliberate
decision rather than letting both quietly exist indefinitely — Railway is a real, small
ongoing cost either way.

## Analytics / Reporting direction — building into the webapp (CURRENT)

**Decided this session, superseding both the Metabase and Power BI directions above.**
Power BI was evaluated in real detail and rejected - recorded here as an established
fact, not something to re-research if it comes up again:
- **Import mode** caps at a limited number of scheduled refreshes per day (up to 8x/day
  on Pro) - not live data. The earlier "lower-effort middle ground" framing (see the
  status-update section above) undersold how far from real-time that actually is for an
  operational tool.
- **DirectQuery** against a non-Azure cloud Postgres (Railway) generally still needs a
  persistent on-premises data gateway - there's no good cloud-native DirectQuery path
  for this setup, and the gateway has documented reliability issues specifically for
  Postgres sources in the Power BI Service.
- **Embedding for viewers without their own Power BI license** needs either Power BI
  Embedded (a Fabric capacity, roughly $260+/month minimum) or an insecure public link -
  neither is acceptable for this use case.

**Current phase: Phase 0 - auditing.** Reviewing the existing `Report_*` views and the
current aggregate-query surface before building anything new.

**Planned phases:**
1. A real analytics query layer - CQRS-pattern aggregate queries (matching this
   codebase's existing command/query convention), not ad-hoc SQL scattered around.
2. One or two real dashboards, built with the "Structural steel" components (`StatusLed`,
   the rivet-strip divider, etc. - see below), using polling-based live updates
   initially, not WebSockets.
3. A kiosk/screen-display route - needs a new long-lived, scoped, view-only token type,
   distinct from the normal session access token (which expires in 30 min and isn't
   meant to sit on a wall-mounted display indefinitely).
4. Scheduled automated PDF report generation.

**Defaults chosen for the build** (starting points, not locked-in architecture
decisions): Recharts for charting; fixed dashboards over a generic dashboard-builder for
now (less to build, revisit if a real need for one shows up); a lightweight
`IHostedService` timer for the first scheduled report job, not Hangfire (no need for
Hangfire's persistence/retry machinery yet at one job).

## Custom Domain, Email & Live App Access (LIVE)

### Domain
**`asasksa.co`** — not `asas.com`, which was unavailable. Registered through
whois.com (registrar: PDR Ltd. / PublicDomainRegistry). Chosen specifically because it
reads as "Asas" + "KSA" (Saudi Arabia), a reasonable fallback naming pattern given the
`.com` was taken.

### DNS
Authoritative DNS for `asasksa.co` is hosted on **Azure DNS** (zone name `asasksa.co`,
resource group `aamer_shah_test`) — not whois.com's own DNS, and not Microsoft 365's.
This was a deliberate migration, not the original plan: both whois.com's and Microsoft
365's DNS panels lack ALIAS record support, which Azure Static Web Apps requires for a
bare apex domain (see CLAUDE.md gotcha #11). Nameservers at whois.com point to Azure's
4 (`ns1-08.azure-dns.com`, `.net`, `.org`, `.info`).
**Every DNS record for this domain — email and app both — now lives in this one Azure
DNS zone.** Nothing should be added back on whois.com's own DNS panel going forward;
it's no longer authoritative.

### Email — Microsoft 365 Business Basic
Hosted Exchange email is live for `@asasksa.co` addresses (aamer + a few colleagues).
MX, CNAME (autodiscover), and TXT (SPF) records are all in the Azure DNS zone above.
**Known constraint hit during setup**: Microsoft 365's business signup flow requires a
ZATCA Tax Identification Number for Saudi-registered businesses. Ascend was not
VAT-registered at the time of signup; a TIN was ultimately provided directly by the
user to complete signup. Zoho Mail (free tier, no TIN requirement) was evaluated as an
alternative and is a viable fallback if ever needed, but was not the path taken.

### Web app custom domain
The existing Azure Static Web App (`black-field-04a8cb300...`) is now also reachable
at **`https://asasksa.co`** directly (apex domain, SSL auto-provisioned by Azure). The
original `azurestaticapps.net` URL still works as well.
**Backend CORS was updated** (`Cors__AllowedOrigins`) to allow `https://asasksa.co` —
required for login/API calls to work from the new domain; without it the site loads
but every API call fails silently.

### Two bugs found and fixed post-launch
1. **404 on page refresh / direct navigation to any route** — fixed by adding
   `platform-web/public/staticwebapp.config.json` with a `navigationFallback` rule
   (see CLAUDE.md gotcha #8 for the Vite-specific placement detail). Confirmed fixed.
2. **Enter key didn't submit the sign-in form** — investigated at length (source
   review, live local test, deploy-bundle content-hash comparison — see CLAUDE.md
   gotcha #12). Root cause was **a browser extension on the user's own machine**, not
   a code bug. No code change was needed or made.

## Arabic/RTL support (MERGED)

**PR #26, merged into `main`.** Two phases:
- **Phase 1** (infrastructure): `react-i18next` + `i18next`, `en`/`ar` languages persisted
  to `localStorage`, `LanguageToggle` in the sidebar (redesigned mid-phase into an
  iOS-style pill switch per feedback), `dir="rtl"`/`"ltr"` kept in sync on `<html>` driving
  every Tailwind `rtl:`/logical-utility class app-wide, Calibri Bold in Arabic mode.
- **Phase 2** (full translation): every screen swept - Form Builder, BuilderHome, Form
  View, User Management, Welcome, sign-in - genuinely all static app UI translated, RTL
  logical positioning applied to every newly-touched component, Calibri Bold made
  unconditional (both languages, not just Arabic, so switching languages doesn't change
  text weight).

**Two real bugs found and fixed during Phase 2 follow-up, both worth knowing about:**
1. **Arabic Code in Form Builder raw-500'd instead of showing a clear validation
   message** - `FieldDefinition.NormalizeColumnName` throws a plain `ArgumentException`
   when Code doesn't normalize to a valid SQL identifier (Code becomes a physical column
   name, so it must stay Latin/alphanumeric; Label/"Field name" is free to be any
   language). Confirmed via live testing, not assumed. Fixed backend-side
   (`AddFieldDefinitionCommand`), plus a broader mechanism added so backend validation
   errors carry a stable, translatable `code` (e.g. `form.field.codeMustBeLatin`) instead
   of only ever showing the backend's raw English text - see `ValidationException.Code` /
   `ExceptionHandlingMiddleware` / `platform-web/src/api/errorMessage.ts`.
2. **Error messages could get stuck in whichever language was active when they were first
   shown**, not updating live on a language toggle - because the resolved *string* was
   being stored in component state instead of the *source* to resolve from. Fixed with
   `platform-web/src/hooks/useErrorMessage.ts`, which re-resolves via `t()` on every
   render; replaced every error `useState` in the app with this hook.

A reported "AddFieldForm regression" (valid Latin Code + Arabic Label suddenly failing)
turned out **not** to be a real regression - reproduced the exact interaction against a
mocked backend matching the real endpoint's response shape and it worked cleanly. No
frontend code change was needed for that specific report.

**Deliberate scope boundary, not an oversight:** only this app's own hardcoded fallback
strings and the one specific backend error code above are translated. The other ~79
backend exception-throw sites across the codebase still surface raw English text - the
mechanism to localize any of them now exists and is cheap to extend (give the exception a
code, add the two locale-file entries), but doing so for every one of them was treated as
separate follow-up work, not part of this PR.

## Mobile horizontal-overflow fix (PR OPEN, NOT YET CONFIRMED)

**PR #28, `fix/mobile-horizontal-overflow` -> `main`, open but not merged.** Real users
reported needing to zoom out / being able to swipe horizontally on mobile, on every
screen, both languages - initially investigated as a viewport-meta-tag or global-CSS
issue (both ruled out: the tag is present and correct, and Playwright mobile-device
emulation across 7+ screens/both languages/multiple device widths never reproduced any
page-level overflow).

**Real cause, found from an actual screen recording on a real iPhone 16 Pro Max**: a
touch swipe on the Daily Site Report + submissions table screen shifted a field label and
the table's right-edge columns together - a page-level shift, not the table's own
intentional internal scroll. This is iOS Safari's elastic/rubber-band overscroll, not a
persistent DOM overflow (which is why static viewport checks, even real-device-emulated
ones, never caught it - `documentElement.scrollWidth` never actually exceeds
`innerWidth`). `Layout.tsx`'s own `overflow-hidden` only ever contained its inner `<div>`,
never reaching `html`/`body` - the true document root had zero horizontal-overflow
protection. Fixed with `overflow-x: hidden` + `overscroll-behavior-x: none` on
`html, body` in `index.css`.

**Not yet confirmed against real Safari** - this sandbox only has Chromium available (no
WebKit binary at all), and a Chromium touch-event simulation predictably showed no shift
either before or after the fix, since Chromium doesn't implement Safari's elastic-scroll
physics. **Needs a real-device retest** (re-recording the same Daily Site Report swipe) on
the PR's preview URL before merging with confidence:
`https://black-field-04a8cb300-28.eastasia.7.azurestaticapps.net`

The same investigation also surfaced two things worth flagging separately, not related to
the mobile CSS fix itself:
- **Sign-in took ~55-60 seconds in the reporter's recording.** Not yet root-caused with
  certainty - no Application Insights SDK exists in this codebase to pull real request
  timing from, and the sandbox can't reach the live backend or Azure SQL to check
  directly. Best working theory: `EnableRetryOnFailure()` is called with default EF Core
  settings (up to 6 retries, exponential backoff capped at 30s/retry) - a first request
  hitting a fully-paused Azure SQL serverless instance needing 2-3 retry cycles while it
  resumes lands comfortably in the observed range, independent of the wifi drop also
  visible in the recording. Worth checking the App Service's own Log Stream / Kudu
  console directly to confirm. **This specific theory no longer applies going forward**
  - Azure SQL (and its serverless auto-pause behavior) is gone; Postgres/Railway doesn't
  auto-pause the same way. If slow sign-in is reported again post-migration, this isn't
  the explanation - look at App Service cold starts instead (see CLAUDE.md's "Always On"
  gotcha).
- **`src/Platform.Api/appsettings.Development.json` has a real Azure SQL hostname,
  username, and plaintext password committed to git** (tracked since commit `3a14d3b`,
  still present in git history regardless of any future edit). Flagged to the project
  owner; not yet rotated or removed from history as of this writing - see "Before going
  live" below, which now also needs to cover the newer Postgres credential (see next
  section) committed the same way.

## Postgres migration - COMPLETE

The whole backend has moved off Azure SQL Server onto Postgres (Railway-hosted,
`metro.proxy.rlwy.net:36575`, database `railway`) as the primary datastore. **Azure SQL
(`test2`) has been deleted** - the ~45x cost savings that motivated this migration is
realized. Don't reference Azure SQL as the live or dev database anywhere going forward;
CLAUDE.md's environment gotchas have been updated to match.

**What's done:**
- EF-tracked static schema: `Npgsql.EntityFrameworkCore.PostgreSQL` wired into both
  `Platform.Api` and `Platform.Infrastructure`, a fresh `InitialPostgres` migration
  applied, all 19 tables (Users, Roles, Permissions, RefreshTokens, Departments,
  FormDefinitions/Versions/FieldDefinitions, FormDefinitionRoles/Users, Workflow*,
  FileMetadataEntries) live on Railway Postgres.
- `DynamicSchemaService` and `DynamicDataRepository` - the dynamic `Data_*`/`Report_*`
  table generation and raw-SQL data access layer - are now **fully ported to Postgres**.
  Npgsql throughout, no SQL Server code path remains active. Verified end-to-end with
  real data, including finding and fixing the Arabic seed-data encoding bug along the
  way (Windows PowerShell's `Invoke-RestMethod` not UTF-8-encoding its `-Body` - see
  CLAUDE.md).
- Old SQL Server EF migrations preserved but inert in `Migrations/SqlServer/`; the
  SQL-Server-specific methods in `SqlTypeMapper` (`ToSqlColumnType`,
  `AssertSafeIdentifier`) are now dead code - see CLAUDE.md's "Low-priority cleanup
  candidates," not urgent, nothing depends on them.
- `claude/project-setup-api-7feho0` (all of this migration's work) merged into `main`.

**New established conventions from this work** (permanent record in CLAUDE.md):
- Dynamic form `DateTime` fields default an unspecified offset to UTC+3 (Saudi local
  time) - deliberately different from the static schema's audit columns, which stay UTC.
- Npgsql requires `Offset=0` on any `DateTimeOffset` written to a `timestamptz` column -
  call `.ToUniversalTime()` before writing. SQL Server accepted a non-zero offset
  directly; Postgres/Npgsql doesn't.

**Known gap, not yet resolved:** there's still no automated first-admin-bootstrap
mechanism (see CLAUDE.md). The current admin account was created via a one-off direct
SQL insert (credentials below) - the same manual step would be needed again for any
future fresh environment. Worth a real fix eventually, not urgent while there's only the
one environment.

**Key IDs** (current - replaces an earlier set that went stale after a mid-session
cleanup/re-seed):
- Locations form: `9f8896a7-f6e9-484f-ac20-9bfc2fde7fa3`
- Stock Adjustment form: `dd23d28f-dc81-4133-9513-c5b1e7452dae`
- Administrator role: `d573b2ad-3410-4924-9885-c582ceb24f28` (unchanged throughout)
- Stock Adjustment workflow definition: `9cab64bc-e963-49e5-8301-3a78af45dd95`

**Admin credentials (for reference):** `admin@asasksa.co` / `TempAdmin123!`

**Credential hygiene:** the Railway Postgres connection string, including its password,
is sitting in `appsettings.Development.json` in git history - same deferred-security-item
category as the old exposed Azure SQL credential (see "Before going live" below). Azure
SQL itself is now deleted, so that specific exposure is moot; Postgres and Blob Storage
are the two live credentials still needing rotation before going live.

## "Structural steel" visual direction (MERGED)

**`feature/structural-steel` merged into `main`** via a clean fast-forward, deployed
live. A deliberate scope decision, not a partial rollout: the reskin applies to the whole
authenticated app shell and sign-in - everywhere `Layout` wraps, not just the two screens
originally scoped for a validation pass. `FormRenderer`/`SubmissionsTable` are shared
across every form, so re-skinning them was never going to stay "two screens" in practice
- see the Step 1 audit that decided this before implementation started.

**What shipped:**
- Full dark/light mode toggle (`src/theme/mode.ts` - mirrors the existing `i18n` module-
  singleton pattern, not React Context) alongside the existing language toggle.
- Font: Archivo replaces Space Grotesk for headers; IBM Plex Mono kept for data/mono
  text (already loaded, reused as-is).
- New reusable components: `StatusLed.tsx` (glowing LED-style status indicator - replaced
  the old bordered status pills everywhere, and a separate measured thin-accent-line
  active-nav-item treatment in `FormPicker.tsx` that predated this work and had never
  been swept over), `Switch.tsx` (shared iOS-style sliding toggle - both the language and
  mode toggles are thin wrappers around it now, not two separate implementations), and
  the `.rivet-strip` divider utility class.
- Design tokens (light/dark color pairs, 2px radius, recessed panel shadow, sign-in
  background texture) as CSS custom properties in `index.css`, referenced from
  `tailwind.config.js` so they work as normal Tailwind utility classes.

**Not yet done:** a real human look at this in an actual browser - see "Immediate next
steps" above. Verified in Code's sandbox via headless Chromium (computed styles,
`getBoundingClientRect` measurements, screenshots) across both languages and both modes,
which isn't the same as someone actually using it.

**Worth knowing as a pattern, not just a one-off fix:** several inputs/selects had no
explicit `bg-*` class at all, even before this redesign - invisible against the old
light-only palette, and only surfaced as a white-box-on-dark-background bug once dark
mode existed to contrast against. See CLAUDE.md's engineering gotchas - check for this
pattern in any new form-control markup going forward.

## Known environment facts specific to this deployment

- GitHub repo: `aamerdatascientist/platform-core`.
- Local dev machine can't run Docker (corporate-locked virtualization) - Postgres
  (Railway-hosted) is the database for local dev too, not just "production." Don't
  suggest Docker.
- Postgres: Railway-hosted, `metro.proxy.rlwy.net:36575`, database `railway`. Azure SQL
  (`construction-site-aamer-shah.database.windows.net`, database `test2`) has been
  deleted - don't reference it as live.
- Local frontend dev server: `http://localhost:5173`. Local API: `http://localhost:5080`
  (pinned via `launchSettings.json` - don't let it drift back to the ASP.NET default 5000).

## Before going live

- **Rotate the Railway Postgres password and the Blob Storage account key.** Both were
  pasted into chat and committed to `appsettings.Development.json` during setup.
  Deliberately deferred until active development wraps up, not forgotten - don't ship
  without doing this. (Azure SQL had the same exposure, but that credential is now moot -
  the database itself was deleted as part of the Postgres migration.)
