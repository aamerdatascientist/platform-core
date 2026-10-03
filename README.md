# platform-core — Asas

Enterprise low-code platform. **ASP.NET Core 8 + EF Core** backend (Clean Architecture,
CQRS via MediatR), **React 18 + TypeScript** frontend, **Postgres** (Railway-hosted) as
the datastore. First customer is a Saudi construction company; the platform itself is
built to be industry-agnostic. Live at **https://asasksa.co**.

**This file is a snapshot for getting the code running locally and orienting a new
reader — it is not the source of truth for architecture decisions or current project
state.** For that, read, in this order:
1. **`CLAUDE.md`** — the stable record of every "decided, don't relitigate" architectural
   choice, established code conventions, and every environment/engineering gotcha hit so
   far. Read this before touching dynamic-schema, field-editing, access-control, or
   deployment code.
2. **`docs/PROJECT_STATUS.md`** — the living, continuously-updated narrative of what's
   built, what's verified against the real production database (not just "compiles"),
   and what's next. Read this before starting any new work session.

## What this actually is

Instead of a generic EAV/JSON-blob approach to "let an org define custom forms," every
`FormDefinition` gets a **real physical SQL table** (`Data_{Code}`), generated and
evolved by `DynamicSchemaService`, plus a `Report_{Code}` view that resolves Lookup
fields to human-readable values for reporting. That's the core architectural bet this
whole codebase is built around — see `CLAUDE.md`'s "Core architectural decision" section
for the full rationale.

## What's built and live right now

- **Identity/Access**: register (admin-gated — no public signup), login, refresh-token
  rotation, logout.
- **Form Engine**: create/publish forms; dynamic table + reporting-view generation;
  submit and page through submissions. A published form's fields can be live-edited
  directly (relabel, reorder, add a field, rename a field's code, change its type,
  archive, or permanently delete) with no draft/republish cycle required — see
  CLAUDE.md's "Field editing" section for exactly which operations are safe vs. which
  require explicit confirmation.
- **Conditional field visibility ("branching")**: a field can be shown/required only
  when another field on the same form has a certain value.
- **Filtered/cascading Lookup fields**: a Lookup field can narrow its candidates by
  another field's current value on the same form.
- **Dynamic dropdown options**: a Dropdown field can source its option list live from
  another form's submitted values, instead of a fixed list.
- **Form Builder UI** (React): create a form, add every field type, publish, and perform
  every live-edit operation above — all through the actual app, not the API directly.
- **Form access control**: restrict a form to specific roles and/or specific users,
  independently of each other. **Administrators always have full access to every form,
  with no bypass possible** — this was a real gap (an admin could lock themselves out of
  their own form with no recovery path) found and fixed 2026-10-04; see CLAUDE.md's
  "Form access control" section.
- **Workflow Engine**: `Draft → Pending approval → Approved/Rejected`, role-gated
  transitions, full history. Deliberately scoped: no versioning, no notifications, one
  published workflow per form.
- **File Management**: upload/list/view/delete attachments via Azure Blob Storage,
  including attaching a file inline while filling out a form.
- Full **Arabic/RTL** bilingual UI (`react-i18next`), and a dark/light-mode visual design
  ("Structural steel") across the whole authenticated app shell.

See `docs/PROJECT_STATUS.md` for exactly what's been verified against the real
production database vs. what still needs a real pass, and for the current list of live
forms/modules.

**Deliberately not built yet** (not oversights — see CLAUDE.md's "Known gaps" section for
the full reasoning on each): no workflow-designer UI (workflows are still created via the
API or PowerShell scripts); no automated first-admin-bootstrap (the current admin account
was created via a one-off direct SQL insert); no designated Lookup "display field" (the
frontend guesses the first `ShortText` field on the target form); analytics/reporting is
being built directly into the webapp (see PROJECT_STATUS.md's "Analytics / Reporting
direction" for the phased plan — Metabase and Power BI were both evaluated and rejected).

## Repo layout

```
src/
  Platform.Domain          — entities, no dependencies on anything else
  Platform.Application     — CQRS commands/queries (MediatR), one file per
                              command/query containing Command + Validator + Handler
  Platform.Infrastructure  — EF Core, DynamicSchemaService, Postgres-specific code
  Platform.Api             — controllers, auth, DI wiring, Program.cs
platform-web/               — the React/TS frontend (see platform-web/README.md)
tests/
  Platform.Infrastructure.IntegrationTests — xUnit, runs against a real Postgres
                              via Testcontainers (see CLAUDE.md for how to get this
                              working in a sandboxed environment)
scripts/                    — PowerShell seed/rebuild scripts, run directly against
                              the live production API (not idempotent migration
                              tooling — read before running)
docs/                       — PROJECT_STATUS.md plus point-in-time integration
                              checklists written when specific features were first
                              built (historical build plans, not living specs)
```

## Getting it running

**Database: this project's local dev database is the same Railway-hosted Postgres
instance production uses — not a local/Dockerized database.** `docker-compose.yml` in
this repo is a leftover from when the database was SQL Server; it's dead, not used by
anything, and safe to ignore (or delete — see CLAUDE.md's "Low-priority cleanup
candidates"). Don't stand up a local database for this project.

1. **SDK**: pinned to .NET 8.0.100 via `global.json`.

2. **Set secrets** (don't rely on `appsettings.Development.json` placeholders for
   anything beyond a first smoke test — and note `ASPNETCORE_ENVIRONMENT` must be
   `Development` for these to load at all; `Properties/launchSettings.json` already
   handles that, don't remove it):
   ```
   cd src/Platform.Api
   dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<ask for the Railway Postgres connection string>"
   ```

3. **Restore and build**:
   ```
   dotnet restore
   dotnet build
   ```

4. **Apply migrations** (design-time only, doesn't need live DB connectivity — `dotnet
   ef` can't always infer which project is which from the repo root, so pass both
   explicitly):
   ```
   dotnet ef database update --project src/Platform.Infrastructure --startup-project src/Platform.Api
   ```

5. **Run the API**:
   ```
   dotnet run --project src/Platform.Api
   ```
   Pinned to `http://localhost:5080` (see `launchSettings.json` — don't let this drift
   back to ASP.NET's default 5000). Swagger UI is at `/swagger` in Development.

6. **Run the frontend** — see `platform-web/README.md` for the up-to-date frontend setup
   (it's the real app now, not a prototype).

7. **Run the integration tests** (needs Docker + a registry mirror in a restricted
   sandbox — see CLAUDE.md's environment gotchas for the exact invocation that works
   around agent-proxy restrictions):
   ```
   dotnet test tests/Platform.Infrastructure.IntegrationTests
   ```

## A smoke-test walkthrough once it's running

1. `POST /api/auth/login` as the existing admin account (see `docs/PROJECT_STATUS.md`
   for current credentials) — grab the access token, use it as a Bearer token from here
   on. `POST /api/auth/register` requires an existing Administrator token itself, so
   there's no self-service way to create the very first user — see CLAUDE.md's "Known
   gaps" for why.
2. `GET /api/forms` — list the forms already live.
3. `POST /api/forms` — e.g. `{ "code": "smoke-test", "name": "Smoke Test",
   "moduleName": "Testing" }`.
4. `POST /api/forms/{id}/fields` — add a field, e.g. a `ShortText` field called `notes`.
5. `POST /api/forms/{id}/publish` — this is the moment `Data_SmokeTest` and
   `Report_SmokeTest` actually get created in Postgres.
6. `POST /api/forms/{id}/submissions` — submit `{ "notes": "it works" }`.
7. `GET /api/forms/{id}/submissions` — page through what you just submitted.
8. Clean up: delete the test form through the real Form Builder UI (or
   `DELETE /api/forms/{id}`) rather than leaving throwaway data in production.

## Before going live

See `docs/PROJECT_STATUS.md`'s "Before going live" section — at minimum, the Railway
Postgres password and the Blob Storage account key both need rotating; both are sitting
in `appsettings.Development.json`'s git history from initial setup.
