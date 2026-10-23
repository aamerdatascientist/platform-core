# Reports how long Postgres itself takes on the app's queries (read-only, changes nothing).
#
# How to use:
#   1. Railway > abundant-flow > Postgres (or Postgres-staging) > Variables >
#      DATABASE_PUBLIC_URL > copy.
#   2. From the repo root:
#        powershell -ExecutionPolicy Bypass -File scripts/db/query-timing.ps1
#
# The URL is read from the clipboard so the password never appears on a command line or in
# the console. The session is read-only (default_transaction_read_only), so it is safe to
# run against production.

param([string]$PgBin = "C:\Program Files\PostgreSQL\18\bin")

$ErrorActionPreference = "Stop"

$url = (Get-Clipboard -Raw)
if ($url) { $url = $url.Trim() }
if (-not $url -or $url -notmatch '^postgres(ql)?://([^:]+):([^@]+)@([^:/]+):(\d+)/([^?\s]+)') {
    throw "The clipboard does not hold a postgresql:// URL. Copy DATABASE_PUBLIC_URL from Railway first."
}
$user = $Matches[2]; $password = [Uri]::UnescapeDataString($Matches[3])
$dbHost = $Matches[4]; $port = $Matches[5]; $database = $Matches[6]
Write-Host "Database: ${dbHost}:${port}/$database (read-only session)"

$env:PGPASSWORD = $password
$env:PGSSLMODE = "require"
try {
    & (Join-Path $PgBin "psql.exe") --host $dbHost --port $port --username $user --dbname $database `
        -X -v ON_ERROR_STOP=1 -f (Join-Path $PSScriptRoot "query-timing.sql")
    if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)." }
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
Set-Clipboard -Value " "
Write-Host ""
Write-Host "Done. The clipboard was cleared."
