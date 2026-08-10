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

## Known environment gotchas - Metabase analytics deployment (Railway + Azure Postgres)

1. **Azure Database for PostgreSQL Flexible Server blocks common extensions by default.**
   `citext` (and others) must be explicitly added to the server's `azure.extensions`
   allow-list under Server Parameters before any tool that depends on it (e.g. Metabase's
   own first migration) can run `CREATE EXTENSION`.

2. **Azure Postgres Flexible Server can default to Microsoft Entra-only authentication.**
   Check the "Authentication method" dropdown on the server's Connect page explicitly -
   if it only offers Entra, plain password logins will hang/timeout rather than fail
   cleanly, which looks identical to a firewall or network problem.

3. **Metabase no longer parses `MB_DB_CONNECTION_URI` as a generic Postgres URI.**
   It's passed directly to Java's JDBC driver, which requires the `jdbc:` prefix and
   user/password as query parameters - not embedded before an `@`. Correct format:
   ```
   jdbc:postgresql://host:port/dbname?user=<user>&password=<pass>&sslmode=require
   ```
   A malformed string here doesn't fail fast - it hangs until a connection-pool timeout,
   which is easy to misdiagnose as a network/firewall issue.

4. **Azure SQL Database's default "Redirect" connection policy breaks connections from
   outside Azure** (e.g. Railway, or any non-Azure host). The client's second, redirected
   connection on a high port (11000-11999) typically never reaches the server. Fix:
   ```
   az sql server conn-policy update --resource-group <rg> --server <server> --connection-type Proxy
   ```
   This is a server-wide setting - it affects every client connecting to that SQL server,
   not just the one that prompted the change.

5. **Metabase (JVM-based) needs real memory headroom to survive first boot** - plugin
   loading + ~1200 database migrations. Under ~1.2GB total container memory, it silently
   OOM-kills and restarts in a loop with no error in its own log (only visible from the
   platform's own crash/metrics view). 1.5-2GB is a safe floor; give the JVM heap
   (`-Xmx`) real room below the container ceiling, not right up against it.

6. **On high-core-count hosts, Metabase's startup (Java 25) can hang deterministically**
   mid-way through parsing migration changelog files, with the process alive but doing
   nothing (confirmed via flat CPU). Constraining apparent parallelism resolved it:
   ```
   JAVA_TOOL_OPTIONS=-XX:ActiveProcessorCount=2
   ```
   Distinguishing this from a memory-driven crash loop: this failure mode has the
   service still reporting "Online" (not crashed/restarted), and CPU usage flat during
   the stall rather than a GC-pause spike.

7. **A container killed mid-migration can leave Liquibase's tracking tables in a
   genuinely inconsistent state** (e.g. `databasechangelog` present, its paired
   `databasechangeloglock` table missing entirely) - not just a stale lock row to
   clear. If migrations were still in the "reading/parsing changelog files" phase
   (before the "Running N migrations..." log line) when the crash happened, there's
   nothing to preserve - `DROP SCHEMA public CASCADE; CREATE SCHEMA public;` and a
   clean restart is more reliable than trying to hand-patch the partial state.

## Known environment gotchas - custom domain, DNS & Vite/Static Web Apps deployment

8. **Vite + Azure Static Web Apps: `staticwebapp.config.json` must live in
   `public/`, not the app root.** Vite only copies the contents of `public/`
   into the `dist/` build output - since the SWA workflow's `output_location`
   is `dist`, a config file placed anywhere else silently never reaches the
   deployed app. Verify with a real `npm run build` and check the file
   actually landed in `dist/`, don't just trust the source tree.

9. **Azure Static Web Apps' "Custom Domain on Azure DNS" option doesn't
   always fully automate verification**, even when the domain's DNS
   genuinely is hosted on Azure DNS. Be ready for the standard manual
   TXT-record verification step regardless of which "add domain" path is
   offered.

10. **Azure DNS groups multiple TXT values under one record set at the same
    host/name** - unlike some registrar DNS panels (e.g. whois.com's
    myorderbox-based one) that let you add visually separate TXT rows at the
    same name. If a service says "add a TXT record" and one already exists
    at that host, add the new value as an *additional value inside the
    existing record*, not a new record set - Azure will reject a duplicate
    record set at the same name/type with a clear error if you try.

11. **Not every DNS panel supports the ALIAS/ANAME record type needed for
    apex/root-domain custom domains on Azure Static Web Apps.** Confirmed
    both Microsoft 365's own DNS management *and* whois.com's (myorderbox)
    panel lack it - only standard record types (A, CNAME, MX, TXT, etc.).
    Azure DNS does support it. If a bare apex domain (not a subdomain) is a
    hard requirement, plan on migrating the domain's authoritative
    nameservers to Azure DNS - which also means re-adding every other
    existing record (email, etc.) into the new zone, not just the new one.

12. **When a reported bug can't be reproduced in local dev, and the deployed
    production bundle is confirmed content-hash-identical (via Vite's
    filename hashing) to a version already verified working locally, the
    reporter's own browser is a real, common remaining cause** - specifically
    extensions (password managers, Grammarly-style tools, etc.) intercepting
    keyboard/form events before the page's own JS ever sees them. Testing in
    an incognito/private window (which disables extensions by default) is a
    fast way to isolate "real site bug" from "local browser interference."
    In this case: full source inspection (no global keydown listeners, no
    CSP/headers in `staticwebapp.config.json`, no code-splitting) plus a
    content-hash match on the deployed bundle ruled out every code-level
    explanation - the actual cause was a browser extension on the tester's
    machine, confirmed by toggling all extensions off.

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

**A command/query namespace segment that exactly matches a domain entity's simple name
makes unqualified references to that entity ambiguous project-wide, not just in nearby
files.** C# namespace visibility isn't file-scoped - it applies everywhere in the
compilation. Hit when `RefreshTokenCommand`'s namespace
(`Platform.Application.Identity.Commands.RefreshToken`) collided with the
`Platform.Domain.Identity.RefreshToken` domain entity, silently breaking an unrelated,
already-working unqualified reference in `LoginCommand.cs` on the other side of the project.

**Fix going forward:** name command/query namespaces after the action, not a bare noun
that matches a domain type - `RefreshAccessToken`, not `RefreshToken`.

## The one rule that's mattered most

**Every phase gets tested end-to-end against real data before moving to the next phase -
not just "does it compile."** Two real bugs (Lookup values showing as raw GUIDs in
reporting views; a Production-mode JWT crash from a missing launchSettings.json) were only
ever caught this way, never by review. Don't skip this step to save time.
