# READ-ONLY. Exports the live structure the dummy-data seed has to match: every form's
# definition exactly as the API serves it (fields, types, options, visibility conditions,
# lookup targets/filters, dynamic-options sources), plus each form's current row count and
# the first rows of each form (so existing master data such as projects/zones/footings/
# floors is known before anything new is generated). Sends only GET requests after login -
# it creates, changes and deletes nothing.
#
# Output: "Claude outputs\live-structure.json" at the repo root. That folder is untracked;
# do not commit the file (the repo is public).
#
# Usage (backend running locally, from the repo root):
#   .\scripts\export-live-structure.ps1
#   .\scripts\export-live-structure.ps1 -BaseUrl "https://<api host>"

param(
    [string]$BaseUrl = "http://localhost:5080",
    [string]$Email,
    $Token = $null
)

$ErrorActionPreference = "Stop"

if (-not $Token) {
    if (-not $Email) { $Email = Read-Host "Admin email" }
    $secure = Read-Host "Password" -AsSecureString
    $plain = [System.Net.NetworkCredential]::new("", $secure).Password
    $loginJson = @{ email = $Email; password = $plain } | ConvertTo-Json
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($loginJson)
    $login = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -Body $bytes -ContentType "application/json; charset=utf-8"
    $Token = $login.accessToken
    $plain = $null; $loginJson = $null; $bytes = $null
}
$headers = @{ Authorization = "Bearer $Token" }

# Invoke-WebRequest + explicit UTF-8 decode of the raw bytes, not Invoke-RestMethod: Windows
# PowerShell 5.1 can mis-decode a response body, which would garble the Arabic labels.
function Get-Json($uri) {
    $resp = Invoke-WebRequest -Uri $uri -Headers $headers -UseBasicParsing
    $stream = $resp.RawContentStream
    $stream.Position = 0
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    return ($reader.ReadToEnd() | ConvertFrom-Json)
}

$forms = @(Get-Json "$BaseUrl/api/forms")
Write-Host "Found $($forms.Count) forms."

$result = @()
foreach ($f in $forms) {
    $definition = Get-Json "$BaseUrl/api/forms/$($f.id)"
    $rows = $null
    $rowError = $null
    try { $rows = Get-Json "$BaseUrl/api/forms/$($f.id)/submissions?page=1&pageSize=200" }
    catch { $rowError = $_.Exception.Message }
    $count = if ($rows) { $rows.totalCount } else { "?" }
    Write-Host ("  {0,-28} rows: {1}" -f $f.code, $count)
    $result += [pscustomobject]@{
        listEntry  = $f
        definition = $definition
        totalRows  = $count
        firstRows  = if ($rows) { $rows.items } else { $null }
        rowError   = $rowError
    }
}

$outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "Claude outputs"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$outFile = Join-Path $outDir "live-structure.json"
$json = [pscustomobject]@{ exportedAtUtc = (Get-Date).ToUniversalTime().ToString("o"); baseUrl = $BaseUrl; forms = $result } |
    ConvertTo-Json -Depth 30
[System.IO.File]::WriteAllText($outFile, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "`nWrote $outFile"
