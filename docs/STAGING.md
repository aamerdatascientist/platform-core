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
| Database | Railway Postgres | **a separate Postgres database** (see "Still to do") |
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

## Still to do (owner)

1. **Create the staging database.** Recommended: a second Postgres service in the same
   Railway project. Copy its connection string into `ConnectionStrings__PostgresConnection`
   on `asas-api-staging` (Npgsql format, like production's).
2. **Fill it.** The API does not migrate on startup, so the schema has to be put there by
   hand. Two options:
   - **Copy production (recommended).** Restore a recent production dump into the staging
     database (`pg_restore --no-owner --dbname "<staging url>" <dump>`). Staging then has the
     real forms, users and data to test against, and no admin bootstrap is needed. Note that
     it holds real client data, so treat its access the same way.
   - **Start empty.** From the repo root on your machine:
     `dotnet ef database update --project src/Platform.Infrastructure --startup-project src/Platform.Api --connection "<staging connection string>"`,
     then insert the first Administrator by hand (there is still no bootstrap mechanism -
     see CLAUDE.md "Known gaps"). The `Report_*` views and `Data_*` tables only appear once
     forms are created and published on staging.
3. **Set the three secret settings** in the table above, then Apply.
4. **Add two GitHub secrets** (repo > Settings > Secrets and variables > Actions):
   - `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING`: `asas-api-staging` > Overview > *Download
     publish profile*, paste the whole file's contents.
   - `AZURE_STATIC_WEB_APPS_API_TOKEN_STAGING`: `asas-web-staging` > Overview > *Manage
     deployment token*, copy the token.
5. **Create the `staging` branch** from `main` once the staging workflow files are on `main`,
   and push it. That runs both staging workflows.
6. **Check it**: open the staging site, sign in, open a form, submit a test record, attach
   a file (it should land in `attachments-staging`, not `attachments`).

## Day-to-day use

- Try a change on staging: push the branch to `staging`
  (`git push origin <branch>:staging --force-with-lease` if staging should become exactly
  that branch). Both staging workflows run; production is not touched.
- When it is good, merge to `main` as before - that is still the production deploy.
- Runtime note: Azure shows .NET 8 as reaching end of support on 10 Nov 2026 on App Service.
  Both apps run .NET 8; plan the move to .NET 10 (LTS), and try it on staging first.
