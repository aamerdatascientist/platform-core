# Project status

Update this file at the end of every session - what changed, what's next. Keep CLAUDE.md
itself stable; this is where the narrative goes.

## Verified end-to-end against the REAL Azure SQL database - by the project owner, not just Code's sandbox

**All four phases built so far now meet this bar. This is the first point in the project
where that's true across the board.**

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

## What just got fixed along the way (worth knowing, not just "it works now")

- **EF Core tracking bug, hit 5 times across Phases 1 and 3** - see `CLAUDE.md`'s "Known
  engineering gotchas" section for the pattern and the fix. Now an established convention
  in this codebase, not a one-off patch.
- **Access tokens expire after 30 min with no refresh flow** - hit repeatedly during manual
  testing as spurious-looking 401s. Not a bug each time it happens - just re-login. Fixed:
  the refresh-token flow now handles this, see above.

## Immediate next steps, in priority order

The frontend app shell (routing, workflow status/approval UI) is done now too - every item
from the previous "Immediate next steps" lists is complete.

1. **Decide the next phase of work:** continue the backend roadmap (Dashboards/Reporting is
   Phase 4, AI Assistant is Phase 5) or keep extending the frontend (e.g. dashboard views,
   further polish).

## Known environment facts specific to this deployment

- GitHub repo: `aamerdatascientist/platform-core`.
- Local dev machine can't run Docker (corporate-locked virtualization) - Azure SQL free
  tier is the database for local dev too, not just "production." Don't suggest Docker.
- Azure SQL server: `construction-site-aamer-shah.database.windows.net`, database `test2`.
- Local frontend dev server: `http://localhost:5173`. Local API: `http://localhost:5080`
  (pinned via `launchSettings.json` - don't let it drift back to the ASP.NET default 5000).
