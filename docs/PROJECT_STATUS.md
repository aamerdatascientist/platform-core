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

## Built and verified in Code's sandbox - not yet tested by the project owner against the real Azure DB

- **Form Builder UI** (create forms, add/remove fields, publish): built and verified
  end-to-end against a local SQL Server in Code's sandbox. Doesn't meet the bar above yet -
  needs a real pass against the Azure DB before it counts as verified.
- **Confirmed gap while building it**: `FormDefinition.StartNewDraftVersion()` exists only
  as a domain method - no command, handler, or endpoint anywhere wires it up. Published
  forms are genuinely read-only in the builder right now, not just an unbuilt frontend
  affordance. Worth a follow-up round.

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

## Immediate next steps, in priority order

The frontend app shell (routing, workflow status/approval UI) and File Management are
fully done. The Form Builder UI is built but not yet fully done - see below.

1. **Verify the Form Builder UI against the real Azure DB** - see "Built and verified in
   Code's sandbox" above. Same bar every other phase has already cleared.
2. **Wire `StartNewDraftVersion()` up** so published forms stop being permanently read-only
   in the builder - a command/handler/endpoint, following the same pattern as
   `PublishFormVersionCommand`.
3. **After those:** continue the backend roadmap (Dashboards/Reporting is Phase 4, AI
   Assistant is Phase 5) or keep extending the frontend (e.g. dashboard views, further polish).

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
