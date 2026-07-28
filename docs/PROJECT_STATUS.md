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

## What just got fixed along the way (worth knowing, not just "it works now")

- **EF Core tracking bug, hit 5 times across Phases 1 and 3** - see `CLAUDE.md`'s "Known
  engineering gotchas" section for the pattern and the fix. Now an established convention
  in this codebase, not a one-off patch.
- **Access tokens expire after 30 min with no refresh flow** - hit repeatedly during manual
  testing as spurious-looking 401s. Not a bug each time it happens - just re-login. Real
  fix (an actual refresh-token flow) is still on the backlog, see gaps below.

## Immediate next steps, in priority order

1. **`GET /api/forms` (list endpoint).** Smallest, highest-leverage next piece - unblocks
   real frontend navigation instead of pasting form IDs by hand. Already flagged as
   priority #1 in `platform-web/README.md`'s own suggested next steps.
2. **Refresh-token flow.** The 401-from-expiry pattern above will keep recurring during any
   real usage, not just testing, until this exists.
3. **After those two:** decide between building out the real frontend app shell (routing,
   nav, a workflow status/action UI on top of SubmissionsTable) vs. continuing the backend
   roadmap (Dashboards/Reporting is Phase 4, AI Assistant is Phase 5).

## Known environment facts specific to this deployment

- GitHub repo: `aamerdatascientist/platform-core`.
- Local dev machine can't run Docker (corporate-locked virtualization) - Azure SQL free
  tier is the database for local dev too, not just "production." Don't suggest Docker.
- Azure SQL server: `construction-site-aamer-shah.database.windows.net`, database `test2`.
- Local frontend dev server: `http://localhost:5173`. Local API: `http://localhost:5080`
  (pinned via `launchSettings.json` - don't let it drift back to the ASP.NET default 5000).
