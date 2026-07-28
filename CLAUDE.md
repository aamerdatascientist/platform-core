# platform-core

Enterprise low-code platform. ASP.NET Core 8 + EF Core backend (Clean Architecture),
React/TS frontend. First customer is a construction company; the platform itself is meant
to be industry-agnostic. Full rationale: see chat history / architecture doc if available,
or ask before re-deriving decisions below from scratch.

**Current phase and what to do next: see `docs/PROJECT_STATUS.md`.** That file changes
often; this one shouldn't. Read it before starting work each session.

## Core architectural decision - do not relitigate without discussion

Dynamic form data is stored as **real SQL tables**, one per `FormDefinition` (schema-per-
form), not EAV and not a JSON blob. `DynamicSchemaService` generates/evolves these tables.
Table name: `Data_{PascalCase(Code)}`, assigned once at first publish, never changes.
Reporting view: `Report_{PascalCase(Code)}`, regenerated on every publish, resolves Lookup
fields to human-readable values (not raw GUIDs - this was a real bug once, watch for
regressions in any new dynamic-schema work).

Workflow state lives in the **static** schema (`WorkflowInstance.RecordId` references a
dynamic table row by GUID), not bolted onto the dynamic tables. Keeps Workflow cleanly
separate from the Form Engine.

## Established code conventions

- Domain entities: private setters, static `Create` factories, `AuditableEntity` base.
  `CreatedAtUtc`/`CreatedByUserId` are populated by `ApplicationDbContext.SaveChangesAsync`'s
  audit interceptor - handlers never set these themselves.
- CQRS via MediatR, one file per command/query containing Command + Validator + Handler
  together (vertical slice organization, not layered by type).
- `FieldType` enum: ShortText, LongText, Number, Decimal, Boolean, DateTime, Dropdown,
  Lookup, Attachment. Attachment never gets a physical column - resolves through the
  (not-yet-built) File Management module instead.
- Identifiers that end up in raw DDL (table/column names) go through
  `SqlTypeMapper.AssertSafeIdentifier` - hard security boundary, not cosmetic.

## Known environment gotchas - don't rediscover these

- **Azure SQL serverless auto-pauses.** `EnableRetryOnFailure()` is required on
  `UseSqlServer()` or the first request after any idle period fails with error 40613.
- **`ASPNETCORE_ENVIRONMENT` must be `Development` locally**, or JWT config throws (empty
  secret) since Development-only appsettings + user-secrets don't load otherwise.
  `Properties/launchSettings.json` handles this now - don't remove it.
- **Enums serialize as strings** (`JsonStringEnumConverter` registered in `Program.cs`) -
  the PowerShell seed scripts and the frontend both depend on this. Don't remove it.
- Local dev DB is **Azure SQL** (free tier), not Docker/local SQL Server - Docker doesn't
  work on this machine (corporate-locked virtualization). Don't suggest Docker again.

## Known gaps - deliberate, not oversights (check `docs/PROJECT_STATUS.md` for current priority)

- No `GET /api/forms` list endpoint - frontend can't show real navigation yet.
- No refresh-token flow - access tokens just expire (30 min), nothing renews them. Sign
  out/in again in the frontend, or re-login via Swagger, when you hit a 401 that isn't a
  real bug.
- No designated "display field" on `FormDefinition` for Lookup rendering - frontend
  guesses (first ShortText field on the target form).
- Workflow Engine: no versioning, no notifications, one published workflow per form.
- No form-builder or workflow-designer UI - everything's created via API/PowerShell scripts.

## Known engineering gotchas - hit multiple times, check for this pattern in new code

**EF Core silently no-ops inserting a new child entity reached only through an
already-tracked parent's navigation collection.** Client-generated GUID keys (every entity
here uses `Guid.NewGuid()` at construction, never the CLR default `Guid.Empty`) defeat EF's
new-vs-existing heuristic, so the child gets misread as already existing and the insert
does nothing - no error, just missing data. Hit five times across two features so far:
`AddFieldDefinitionCommand`, `StartNewDraftVersion`, `AddWorkflowStateCommand`,
`AddWorkflowTransitionCommand` (and its nested `WorkflowTransitionRole` children), and
`ExecuteWorkflowTransitionCommand`'s history entries.

**Fix, now the established convention:** domain methods that create a child entity return
it; the handler explicitly calls `_db.Set<T>().Add(child)` rather than relying on EF's
relationship fixup to pick it up automatically. Check for this in any future "parent
creates child via a domain method" code before it bites a sixth time.

## The one rule that's mattered most

**Every phase gets tested end-to-end against real data before moving to the next phase -
not just "does it compile."** Two real bugs (Lookup values showing as raw GUIDs in
reporting views; a Production-mode JWT crash from a missing launchSettings.json) were only
ever caught this way, never by review. Don't skip this step to save time.
