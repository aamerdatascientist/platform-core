# Project status

Update this file at the end of every session - what changed, what's next. Keep CLAUDE.md
itself stable; this is where the narrative goes.

## Verified end-to-end against the REAL Azure SQL database - by the project owner, not just Code's sandbox

**All four phases built so far, plus every feature added since, now meet this bar.**

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

## Built and verified in Code's sandbox - not yet tested by the project owner against the real Azure DB

- **Form Builder UI** (create forms, add/remove fields, publish, edit a published form's
  fields via a new draft version): built and verified end-to-end against a local SQL
  Server in Code's sandbox, including the `StartNewFormVersionCommand`/`DeleteFormCommand`
  work from PR #15 and the FormBuilder draft-status fix from `16a7d52`. Doesn't meet the
  bar above yet - needs a real pass against the Azure DB before it counts as verified.
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

The frontend app shell (routing, workflow status/approval UI) and File Management are
fully done. The Form Builder UI is built but not yet fully done - see below.

1. **Verify the Form Builder UI against the real Azure DB** - see "Built and verified in
   Code's sandbox" above. Same bar every other phase has already cleared.
2. **Verify form access control in the actual browser UI** - SQL/API-verified only so far
   (both role-based and per-user); needs a real pass confirming restricted forms actually
   disappear from navigation, the admin-only actions are hidden or gated correctly in the
   Form Builder UI, and there's a real way for an admin to manage per-user grants, not
   just blocked server-side.
3. **After those:** continue the backend roadmap (Dashboards/Reporting is Phase 4, AI
   Assistant is Phase 5) or keep extending the frontend (e.g. dashboard views, further polish).

**Newer, currently-open items (added after the above list was last reordered - not yet
folded into a single priority order with it):**
- **Real-device retest of the mobile horizontal-overflow fix** (PR #28) before merging -
  see that section above for the preview URL. This was reported by real users; treat as
  high priority.
- **Add `ConnectionStrings__PostgresConnection` to the App Service's Application
  Settings**, then run `dotnet ef database update` against the Railway Postgres database
  from somewhere that can reach it, then confirm the 19 tables exist - see the Postgres
  migration section above for exact commands. Blocks any further Postgres migration work.
- **Merge `claude/project-setup-api-7feho0` into `main`** once the above is confirmed
  working, so the Postgres wiring isn't sitting only on a side branch.

## Analytics — Metabase (LIVE)

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
- `PowerBIReader` credential switch (see above)
- Old Azure Postgres resource cleanup (see above)

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
  console directly to confirm.
- **`src/Platform.Api/appsettings.Development.json` has a real Azure SQL hostname,
  username, and plaintext password committed to git** (tracked since commit `3a14d3b`,
  still present in git history regardless of any future edit). Flagged to the project
  owner; not yet rotated or removed from history as of this writing - see "Before going
  live" below, which now also needs to cover the newer Postgres credential (see next
  section) committed the same way.

## Postgres migration - Phase 1: provider wired, not yet applied (IN PROGRESS)

Decision made: migrate the whole backend off Azure SQL Server onto Postgres (Railway-hosted,
`metro.proxy.rlwy.net:36575`, database `railway`) as the **primary** datastore, not a
secondary analytics store. This is a multi-phase migration; what's landed so far on
`claude/project-setup-api-7feho0` (not yet merged into `main`) is provider + connection
wiring only:

- `Npgsql.EntityFrameworkCore.PostgreSQL` added to both `Platform.Api` and
  `Platform.Infrastructure` (the package needs to be in the latter too - that's where
  `UseNpgsql` is actually called, and package references don't flow from a project to
  what it depends on).
- `DependencyInjection.cs`: `UseSqlServer` -> `UseNpgsql`, reading a new
  `PostgresConnection` connection string. The old `DefaultConnection` (SQL Server) stays
  in config, just unused by the DbContext for now.
- New `PostgresConnection` entries added to `appsettings.json` (empty placeholder) and
  `appsettings.Development.json` (real Railway credentials - see the credential-hygiene
  note above, same concern applies here).
- Old SQL Server EF migrations moved to `Migrations/SqlServer/` (namespace updated,
  excluded from compilation) so they're preserved for reference but inert. A fresh
  `InitialPostgres` migration generated in `Migrations/Postgres/` from the current model
  - covers the full EF-tracked static schema (19 tables: Users, Roles, Permissions,
  RefreshTokens, Departments, FormDefinitions/Versions/FieldDefinitions,
  FormDefinitionRoles/Users, Workflow*, FileMetadataEntries). Nothing dynamic-form-related
  is in it - those tables were never part of the EF model (`DynamicSchemaService` manages
  them via raw ADO.NET, entirely outside EF's migration system), and this phase
  deliberately doesn't touch that.

**Explicitly NOT done yet - each one blocks going further:**
1. **The App Service's live Application Settings still need a
   `ConnectionStrings__PostgresConnection` entry added manually** (Portal or `az cli`,
   whichever's convenient) - the deployed backend has no Postgres connection string
   configured until this happens. Couldn't be done from Code's sandbox - no Azure CLI
   available, and Azure endpoints are blocked by the same egress policy that's blocked
   Azure SQL all along.
2. **The `InitialPostgres` migration has not been applied to the live Railway database.**
   `dotnet ef database update --project src/Platform.Infrastructure --startup-project
   src/Platform.Api` needs to run from somewhere that can actually reach
   `metro.proxy.rlwy.net:36575` - confirmed unreachable from Code's sandbox (raw TCP
   connect times out, see CLAUDE.md). No tables exist in the Railway database yet.
3. **`DynamicSchemaService` and everything dynamic-form-related is still 100%
   SQL-Server-specific** (raw DDL, `SqlTypeMapper`, the whole `Data_*`/`Report_*` table
   generation pattern) - deliberately out of scope for this phase, not an oversight. The
   app cannot actually run against Postgres end-to-end until this is migrated too.
4. **`claude/project-setup-api-7feho0` itself is not yet merged into `main`** - it was
   brought up to date with `main` (13-commit gap, merged cleanly, no conflicts) before this
   work started, but the Postgres work sits on top of that merge, unmerged back.

## Known environment facts specific to this deployment

- GitHub repo: `aamerdatascientist/platform-core`.
- Local dev machine can't run Docker (corporate-locked virtualization) - Azure SQL free
  tier is the database for local dev too, not just "production." Don't suggest Docker.
- Azure SQL server: `construction-site-aamer-shah.database.windows.net`, database `test2`.
- Local frontend dev server: `http://localhost:5173`. Local API: `http://localhost:5080`
  (pinned via `launchSettings.json` - don't let it drift back to the ASP.NET default 5000).

## Before going live

- **Rotate the Azure SQL password and Blob Storage account key.** Both were pasted into
  chat during setup. Deliberately deferred until active development wraps up, not
  forgotten - don't ship without doing this.
- **Rotate the Railway Postgres password too**, same reason - it's sitting in
  `appsettings.Development.json` in git history the same way the Azure SQL credentials
  are (see the Postgres migration section above).
