# Fills the STAGING database with a copy of a production dump, then puts the staging API's
# connection string on the clipboard, ready to paste into Azure.
#
# How to use:
#   1. Railway > abundant-flow > Postgres-staging > Variables > DATABASE_PUBLIC_URL > copy.
#   2. From the repo root:
#        powershell -ExecutionPolicy Bypass -File scripts\staging\restore-staging-db.ps1
#      (optional: -DumpPath <file>; default is the newest asas-backup-*.dump in the repo root)
#   3. Paste the clipboard into asas-api-staging > Environment variables >
#      ConnectionStrings__PostgresConnection.
#
# The URL is read from the clipboard so the password never appears on a command line or in
# the console. The script refuses to run unless the URL points at the staging host below -
# it can never touch the production database. Existing staging data is replaced.
#
# Sign-in sessions copied from production (RefreshTokens) are deleted after the restore.
# Users, roles and passwords are the same as production's at the time of the dump.

param(
    [string]$DumpPath = $null,
    [string]$StagingHostPort = "altaria.proxy.rlwy.net:48044",
    [string]$PgBin = "C:\Program Files\PostgreSQL\18\bin"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if (-not $DumpPath) {
    $latest = Get-ChildItem -Path $repoRoot -Filter "asas-backup-*.dump" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "No asas-backup-*.dump in $repoRoot. Pass -DumpPath." }
    $DumpPath = $latest.FullName
}
Write-Host "Dump: $DumpPath"

$url = (Get-Clipboard -Raw)
if ($url) { $url = $url.Trim() }
if (-not $url -or $url -notmatch '^postgres(ql)?://([^:]+):([^@]+)@([^:/]+):(\d+)/([^?\s]+)') {
    throw "The clipboard does not hold a postgresql:// URL. Copy DATABASE_PUBLIC_URL from Postgres-staging first."
}
$user = $Matches[2]; $password = [Uri]::UnescapeDataString($Matches[3])
$dbHost = $Matches[4]; $port = $Matches[5]; $database = $Matches[6]

if ("${dbHost}:${port}" -ne $StagingHostPort) {
    throw "The URL points at ${dbHost}:${port}, not the staging database ($StagingHostPort). Nothing was changed."
}
Write-Host "Target: ${dbHost}:${port}/$database as $user (staging)"

$env:PGPASSWORD = $password
$env:PGSSLMODE = "require"
try {
    # --no-owner/--no-privileges: production's objects belong to the asas_app role, which
    # does not exist on staging. --clean --if-exists makes re-running the script safe.
    & (Join-Path $PgBin "pg_restore.exe") --host $dbHost --port $port --username $user --dbname $database `
        --no-owner --no-privileges --clean --if-exists --single-transaction $DumpPath
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed (exit $LASTEXITCODE)." }

    # SQL goes through a file: Windows PowerShell 5.1 strips the double quotes around
    # Postgres identifiers when passing -c arguments to a native program.
    & (Join-Path $PgBin "psql.exe") --host $dbHost --port $port --username $user --dbname $database `
        -v ON_ERROR_STOP=1 -f (Join-Path $PSScriptRoot "after-restore.sql")
    if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)." }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

# Same shape as production's connection string.
"Host=$dbHost;Port=$port;Database=$database;Username=$user;Password=$password;SSL Mode=Require;Trust Server Certificate=true" | Set-Clipboard
Write-Host ""
Write-Host "Restore done. The staging API connection string is now on the clipboard."
Write-Host "Paste it into asas-api-staging > Environment variables > ConnectionStrings__PostgresConnection."
