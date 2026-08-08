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
