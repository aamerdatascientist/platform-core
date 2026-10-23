# Staging environment

A second copy of Asas for trying changes before they reach the client. Set up 2026-10-23.
Everything that is not a secret is recorded here; secrets live only in Azure and GitHub.

## What exists

| Piece | Production | Staging |
| --- | --- | --- |
| API (App Service, Linux, .NET 8) | `nexus-platform-api` | `asas-api-staging` |
| API URL | `https://nexus-platform-api-b6cvdedgfdgkgee7.eastasia-01.azurewebsites.net` | `https://asas-api-staging-ancbhsa7grf5emfh.eastasia-01.azurewebsites.net` |
| App Service plan | `ASP-aamershahtest-8f8b` (Basic B1, East Asia) | **the same plan** - shared, no extra plan cost |
| Site (Static Web App, Free) | `nexus-platform-web` - `https://asasksa.co` | `asas-web-staging` - `https://thankful-water-079284100.6.azurestaticapps.net` |
| Database (Railway project `abundant-flow`) | service `Postgres`, public `metro.proxy.rlwy.net:36575` | service `Postgres-staging`, public `altaria.proxy.rlwy.net:48044` (created 2026-10-23) |
| File attachments | storage account `platformcoreattach2026`, container `attachments` | same account, container `attachments-staging` |
| Deployed from | push to `main` | push to `staging` |
| Workflows | `deploy-api.yml`, `azure-static-web-apps-black-field-04a8cb300.yml` | `deploy-api-staging.yml`, `deploy-web-staging.yml` |

All resources are in subscription "Azure subscription 1", resource group `aamer_shah_test`,
directory `aamerdatascientistgmail.onmicrosoft.com` (sign in as aamerdatascientist@gmail.com).

### Choices made, and why

- **Shared App Service plan.** Staging adds no plan cost. The trade-off: staging and
  production share one B1 machine (1 vCPU, 1.75 GB), so heavy testing on staging can slow
  production down a little. If that ever matters, move `asas-api-staging` to its own plan.
- **Shared storage account, separate container.** The container name used to be hard-coded
  as `attachments`, which would have mixed staging uploads into production's. It is now the
  `Files:ContainerName` setting (default `attachments`, so production needs no change).
  The container is not created automatically - it must exist before the first upload.
- **No Application Insights** on staging (billed separately, not needed there).
- **Basic authentication (SCM) on, FTP off.** The deploy workflow uses a publish profile,
  which needs SCM basic auth; FTP is not used.
- **Always On** is on, like production.
- **No pull-request previews** on the staging site: they would be built against whichever
  API the PR's `.env` file names.
- The site's CSP (`platform-web/public/staticwebapp.config.json`) names **both** API origins
  in `connect-src`, so the same file works for both sites.

## Staging API settings

Set on `asas-api-staging` > Environment variables:

| Setting | Value | Status |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` | set 2026-10-23 |
| `Cors__AllowedOrigins__0` | `https://thankful-water-079284100.6.azurestaticapps.net` | set 2026-10-23 |
| `Files__ContainerName` | `attachments-staging` | set 2026-10-23 |
| `ConnectionStrings__PostgresConnection` | the **staging** database's connection string | **owner to set** |
| `ConnectionStrings__BlobStorage` | same value as production's | **owner to set** |
| `Jwt__Secret` | a **new** random value, at least 32 characters, not production's | **owner to set** |
| `Dashboard__ShareKey` | leave unset (public dashboard link off) | - |

Use a different `Jwt__Secret` from production's, so a sign-in token from one environment is
never accepted by the other.

## Filling the staging database

The API does not migrate on startup, so the schema has to be put there by hand. Staging is
filled with **a copy of a production dump**: real forms, users and data to test against, and
no first-admin bootstrap needed. It holds real client data, so treat access to it the same
way as production. Users sign in on staging with their production passwords as of the dump.

`scripts/staging/restore-staging-db.ps1` does it (PostgreSQL 18 client tools, installed at
`C:\Program Files\PostgreSQL\18\bin` on the owner's machine):

1. Railway > `abundant-flow` > `Postgres-staging` > Variables > copy `DATABASE_PUBLIC_URL`.
2. `powershell -ExecutionPolicy Bypass -File scripts\staging\restore-staging-db.ps1`
   (newest `asas-backup-*.dump` in the repo root by default; `-DumpPath` for another).

It reads the URL from the clipboard so the password is never on a command line, **refuses
any URL that isn't `altaria.proxy.rlwy.net:48044`** (so it cannot touch production), replaces
whatever staging held, deletes the copied sign-in sessions (`RefreshTokens`), prints user /
form / migration counts, and leaves the API's Npgsql connection string on the clipboard.
Re-run it any time to refresh staging from a newer dump.

Files attached in production point at blobs in the `attachments` container; staging reads
`attachments-staging`, so attachments copied from production won't open on staging. New
uploads on staging work normally.

To start empty instead: `dotnet ef database update --project src/Platform.Infrastructure
--startup-project src/Platform.Api --connection "<staging connection string>"`, then insert
the first Administrator by hand (no bootstrap mechanism yet - see CLAUDE.md "Known gaps").

## Secrets - set by the owner, never by Claude

| Where | Name | How |
| --- | --- | --- |
| `asas-api-staging` env vars | `ConnectionStrings__PostgresConnection` | on the clipboard after the restore script |
| `asas-api-staging` env vars | `ConnectionStrings__BlobStorage` | copy production's value (`nexus-platform-api` > Environment variables > Show value) |
| `asas-api-staging` env vars | `Jwt__Secret` | `powershell -ExecutionPolicy Bypass -File scripts\staging\new-jwt-secret.ps1` puts a new one on the clipboard |
| GitHub Actions secret | `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` | `asas-api-staging` > Overview > ... > *Download publish profile*, then `powershell -ExecutionPolicy Bypass -File scripts/staging/set-publish-profile-secret.ps1` (sets the secret, deletes the file) |
| GitHub Actions secret | `AZURE_STATIC_WEB_APPS_API_TOKEN_STAGING` | `asas-web-staging` > Overview > ... > *Manage deployment token* > copy, then `powershell -Command "Get-Clipboard -Raw \| gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN_STAGING"` |

All six were set on 2026-10-23. Commands typed into Claude Code with `!` run in bash, which
eats backslashes and expands `$HOME`/`$?` before PowerShell sees them - use forward slashes
in script paths and keep `$` out of `!` one-liners (put the logic in a script instead).

## First deploy

**Done 2026-10-23.** Database restored from `asas-backup-2026-10-21.dump` (5 users, 7 forms,
8 migrations - matching the code; 108 copied sign-in sessions removed). `staging` pushed from
`chore/staging-environment`; both workflows green (runs 39953710263, 39953710261). Checked:
the API answers a wrong sign-in with 401 (so it reaches the staging database), CORS allows the
staging site and refuses `asasksa.co`, the site serves a bundle pointing at the staging API,
and its CSP names both APIs. The first request after a deploy returned Azure's welcome page /
404 for about a minute while the app started - normal, not a fault.

GitHub runs a push's workflows from the pushed branch itself, so `staging` can be created
straight from the branch holding the staging workflows - it doesn't need to reach `main`
first. Push it (`git push origin chore/staging-environment:staging`) once the secrets above
exist, then check: open the staging site, sign in, open a form, submit a test record, attach
a file (it should land in `attachments-staging`, not `attachments`).

## Day-to-day use

- Try a change on staging: push the branch to `staging`
  (`git push origin <branch>:staging --force-with-lease` if staging should become exactly
  that branch). Both staging workflows run; production is not touched.
- When it is good, merge to `main` as before - that is still the production deploy.
- Runtime note: Azure shows .NET 8 as reaching end of support on 10 Nov 2026 on App Service.
  Both apps run .NET 8; plan the move to .NET 10 (LTS), and try it on staging first.
