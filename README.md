# Platform — Phase 0 (Identity + Form Engine core)

This is the foundation of the low-code platform described in `platform-architecture-foundation.md`
(the companion document you should already have). It implements exactly two things end to end,
deliberately: **Identity/Access** and the **Form Engine's dynamic schema core**. Everything else
(Workflow, Notifications, Files, Dashboards, AI) is intentionally not here yet — see the roadmap
in the architecture doc.

## What actually works in this codebase

- Register / login with JWT access + refresh tokens (`AuthController`)
- Create a form definition, add fields to its draft version, publish it (`FormsController`)
- **Publishing a form generates a real physical SQL table** — one column per field,
  additive schema evolution across versions (`DynamicSchemaService`)
- Publishing also generates a human-readable reporting view (`Report_<FormName>`) meant for
  Power BI / future AI-assistant consumption — never point either of those at the raw table
- Submitting and paging through form data go straight against the generated table via Dapper
  (`DynamicDataRepository`), not EF Core — see the architecture doc for why
- Soft-delete query filters, audit fields (`CreatedAtUtc`/`CreatedByUserId`/etc.) on every
  static entity
- Integration tests for `DynamicSchemaService` that run against a **real** SQL Server
  container via Testcontainers — this component generates DDL, and DDL bugs are exactly the
  kind of thing a mock will hide from you

## What's deliberately NOT here yet

- Workflow engine, approvals, notifications
- File/attachment storage (Attachment fields are modeled but intentionally don't get a
  physical column — see `FieldType` in the Domain project)
- Dashboards, Power BI wiring itself (the reporting views are the hook point for it)
- The React frontend / form renderer / form builder UI
- Row-level permission scoping beyond `[Authorize]` (department/role-based data filtering)
- EF Core migrations for the static schema (see below — you generate the first one)
- Seeding (default roles, an initial admin user)

Building any of these should mean reading the relevant section of the architecture doc first,
not guessing at a pattern that isn't there yet.

## Getting it running

1. **Start local SQL Server:**
   ```
   docker compose up -d
   ```

2. **Restore and build:**
   ```
   dotnet restore
   dotnet build
   ```

3. **Set the JWT secret** (don't rely on the placeholder in `appsettings.Development.json`
   for anything beyond a first smoke test):
   ```
   cd src/Platform.Api
   dotnet user-secrets init
   dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
   ```

4. **Create and apply the first migration** (this is the static schema only — Users, Roles,
   FormDefinitions, etc. The dynamic per-form tables are created later, at runtime, by
   `DynamicSchemaService` when a form is published, not by this migration):
   ```
   dotnet ef migrations add InitialCreate --project src/Platform.Infrastructure --startup-project src/Platform.Api
   dotnet ef database update --project src/Platform.Infrastructure --startup-project src/Platform.Api
   ```

5. **Run the API:**
   ```
   dotnet run --project src/Platform.Api
   ```
   Swagger UI is at `/swagger` in development.

6. **Run the integration tests** (needs Docker, spins up its own throwaway SQL Server
   container — separate from the one in step 1):
   ```
   dotnet test
   ```

## A smoke-test walkthrough once it's running

1. `POST /api/auth/register` — you'll need a `DefaultRoleId`, which means seeding at least
   one `Role` row first (there's no seed data yet — insert one directly or add a seeding
   step, your call for now).
2. `POST /api/auth/login` — grab the access token, use it as a Bearer token from here on.
3. `POST /api/forms` — e.g. `{ "code": "stock-adjustment", "name": "Stock Adjustment",
   "moduleName": "StockManagement" }`.
4. `POST /api/forms/{id}/fields` — add a couple of fields, e.g. a `Number` field called
   `quantity` and a `ShortText` field called `reason`.
5. `POST /api/forms/{id}/publish` — this is the moment `Data_StockAdjustment` and
   `Report_StockAdjustment` actually get created in SQL Server. Go look at them with any
   SQL client — that's the whole point of this phase.
6. `POST /api/forms/{id}/submissions` — submit `{ "quantity": 10, "reason": "cycle count" }`.
7. `GET /api/forms/{id}/submissions` — page through what you just submitted.

## Honest gaps worth knowing about before you build on this

- **No seed data.** There's no default Administrator role/user/permission set yet.
- **Field type changes after publish aren't handled.** `AddFieldDefinitionCommand` only
  supports adding new fields to a draft. Changing an existing published field's type needs
  the migration-job design described in the architecture doc — not built yet.
- **`SubmitFormDataCommand` does light validation** (required fields only) — full per-field
  validation against `ValidationRulesJson` (min/max/regex) isn't wired in yet.
- **No rate limiting, no output caching, no API versioning** beyond a placeholder — fine for
  Phase 0, not fine to ship.
