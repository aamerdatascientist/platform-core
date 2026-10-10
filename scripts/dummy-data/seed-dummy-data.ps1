# Loads the dummy data set (5 projects coded DUMMY-01..05, their zones/footings/floors,
# three months of daily progress reports, and matching stock inflow/outflow) into the seven
# live forms, through the normal submit endpoint - the same path a person filling in the
# form uses, so every row passes the same validation.
#
# The rows themselves live in dummy-rows.tsv (produced by generate_dummy_data.py from the
# live structure exported by export-live-structure.ps1). This script contains no data.
#
# SAFE BY DEFAULT: without -Apply it only reads. It logs in, checks that the live forms
# still have exactly the structure the data was generated for, and prints what it would
# write. Nothing is created until you run it again with -Apply.
#
# RESUMABLE: every row written is recorded in "Claude outputs\dummy-seed-ledger.tsv". If a
# run stops part-way, run the same command again - rows already written are skipped, never
# duplicated.
#
# REMOVING THE DATA: there is no delete-record feature in the app, so removal is by SQL -
# see cleanup-dummy-data.sql next to this script.
#
# Usage (backend running locally, from the repo root):
#   powershell -ExecutionPolicy Bypass -File scripts\dummy-data\seed-dummy-data.ps1           # dry run
#   powershell -ExecutionPolicy Bypass -File scripts\dummy-data\seed-dummy-data.ps1 -Apply    # writes

param(
    [string]$BaseUrl = "http://localhost:5080",
    [string]$Email,
    [switch]$Apply,
    # Test hook only: supply the password non-interactively. Leave unset in normal use.
    $PasswordForTesting = $null,
    # Where the ledger is kept. Defaults to "Claude outputs" at the repo root.
    $LedgerPath = $null
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$utf8 = New-Object System.Text.UTF8Encoding($false)
# Windows PowerShell 5.1 defaults: old TLS versions only, and a 100-continue handshake that
# adds a round trip to every POST.
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::Expect100Continue = $false

$rowsFile = Join-Path $PSScriptRoot "dummy-rows.tsv"
$expectedFile = Join-Path $PSScriptRoot "expected-structure.txt"
if (-not $LedgerPath) {
    $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $outDir = Join-Path $repoRoot "Claude outputs"
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
    $LedgerPath = Join-Path $outDir "dummy-seed-ledger.tsv"
}

# --- read the data set (explicit UTF-8: the rows carry Arabic text) -------------------------

$rows = New-Object System.Collections.Generic.List[object]
foreach ($line in [System.IO.File]::ReadAllLines($rowsFile, $utf8)) {
    if ($line.Length -eq 0) { continue }
    $parts = $line.Split("`t")
    if ($parts.Count -ne 3) { throw "Malformed line in dummy-rows.tsv: $($line.Substring(0, [Math]::Min(80, $line.Length)))" }
    $rows.Add([pscustomobject]@{ Key = $parts[0]; Form = $parts[1]; Body = $parts[2] })
}
$expected = @([System.IO.File]::ReadAllLines($expectedFile, $utf8) | Where-Object { $_.Length -gt 0 })
$formCodes = @($rows | ForEach-Object { $_.Form } | Select-Object -Unique)
Write-Host "Data set: $($rows.Count) rows across $($formCodes.Count) forms."
Write-Host "Target:   $BaseUrl"

# --- HTTP helpers ---------------------------------------------------------------------------

$script:accessToken = $null
$script:refreshToken = $null

function Read-ErrorBody($err) {
    if ($err.ErrorDetails -and $err.ErrorDetails.Message) { return $err.ErrorDetails.Message }
    try {
        $stream = $err.Exception.Response.GetResponseStream()
        $stream.Position = 0
        return (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd()
    }
    catch { return "" }
}

function Get-StatusCode($err) {
    try { return [int]$err.Exception.Response.StatusCode } catch { return 0 }
}

# One request. Returns the response body text, decoded as UTF-8 from the raw bytes - Windows
# PowerShell 5.1 can otherwise mis-decode a response and garble Arabic.
function Invoke-Api($method, $path, $jsonBody) {
    $params = @{ Uri = "$BaseUrl$path"; Method = $method; UseBasicParsing = $true; TimeoutSec = 120 }
    if ($script:accessToken) { $params.Headers = @{ Authorization = "Bearer $($script:accessToken)" } }
    if ($null -ne $jsonBody) {
        # Explicit UTF-8 bytes, never a string body (see CLAUDE.md's Invoke-RestMethod gotcha).
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes($jsonBody)
        $params.ContentType = "application/json; charset=utf-8"
    }
    $resp = Invoke-WebRequest @params
    $stream = $resp.RawContentStream
    $stream.Position = 0
    return (New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)).ReadToEnd()
}

function Update-Tokens($text) {
    $pair = ConvertFrom-Json $text
    $script:accessToken = $pair.accessToken
    $script:refreshToken = $pair.refreshToken
}

# Same as Invoke-Api, but renews the 30-minute access token once and retries on a 401.
function Invoke-ApiAuthed($method, $path, $jsonBody) {
    try { return Invoke-Api $method $path $jsonBody }
    catch {
        if ((Get-StatusCode $_) -ne 401) { throw }
        $refreshBody = (@{ refreshToken = $script:refreshToken } | ConvertTo-Json)
        $script:accessToken = $null
        Update-Tokens (Invoke-Api "Post" "/api/auth/refresh" $refreshBody)
        return Invoke-Api $method $path $jsonBody
    }
}

function Get-Json($path) {
    $parsed = ConvertFrom-Json (Invoke-ApiAuthed "Get" $path $null)
    return $parsed
}

# --- log in ---------------------------------------------------------------------------------

if (-not $Email) { $Email = Read-Host "Admin email" }
if ($PasswordForTesting) { $plain = [string]$PasswordForTesting }
else {
    $secure = Read-Host "Password" -AsSecureString
    $plain = (New-Object System.Net.NetworkCredential("", $secure)).Password
}
Update-Tokens (Invoke-Api "Post" "/api/auth/login" (@{ email = $Email; password = $plain } | ConvertTo-Json))
$plain = $null

# --- pre-flight 1: the live forms must have exactly the structure the data was built for ----

Write-Host "`nChecking the live form structure..."
$formIdByCode = @{}
$codeByFormId = @{}
foreach ($f in (Get-Json "/api/forms")) {
    $formIdByCode[$f.code] = $f.id
    $codeByFormId[$f.id] = $f.code
}
foreach ($code in $formCodes) {
    if (-not $formIdByCode.ContainsKey($code)) { throw "The live system has no form with code '$code'. Nothing was written." }
}

function Get-FieldSignature($formCode, $field) {
    $options = ""
    if ($field.optionsJson) {
        $parsed = ConvertFrom-Json $field.optionsJson
        $vals = New-Object System.Collections.Generic.List[string]
        foreach ($o in $parsed) { $vals.Add([string]$o.value) }
        $options = $vals -join ","
    }
    $target = ""
    if ($field.lookupFormDefinitionId) {
        if ($codeByFormId.ContainsKey($field.lookupFormDefinitionId)) { $target = $codeByFormId[$field.lookupFormDefinitionId] }
    }
    $vis = ""
    if ($field.visibleWhenFieldCode) {
        $parsed = ConvertFrom-Json $field.visibleWhenValuesJson
        $vals = New-Object System.Collections.Generic.List[string]
        foreach ($v in $parsed) { $vals.Add([string]$v) }
        $vis = "$($field.visibleWhenFieldCode)=$($vals -join ',')"
    }
    $dyn = ""
    if ($field.dynamicOptionsSourceFormDefinitionId) {
        $srcCode = "?"
        if ($codeByFormId.ContainsKey($field.dynamicOptionsSourceFormDefinitionId)) { $srcCode = $codeByFormId[$field.dynamicOptionsSourceFormDefinitionId] }
        $dyn = "$srcCode.$($field.dynamicOptionsSourceFieldCode)"
    }
    $req = "opt"
    if ($field.isRequired) { $req = "req" }
    $filter = ""
    if ($field.filterByFieldCode) { $filter = [string]$field.filterByFieldCode }
    return (@($formCode, $field.code, $field.fieldType, $req, $options, $target, $filter, $vis, $dyn) -join "|")
}

$live = New-Object System.Collections.Generic.List[string]
foreach ($code in $formCodes) {
    $def = Get-Json "/api/forms/$($formIdByCode[$code])"
    if (-not $def.publishedVersion) { throw "Form '$code' has no published version. Nothing was written." }
    foreach ($field in $def.publishedVersion.fields) {
        if ($field.isActive) { $live.Add((Get-FieldSignature $code $field)) }
    }
}
$expectedSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($e in $expected) { [void]$expectedSet.Add($e) }
$liveSet = New-Object System.Collections.Generic.HashSet[string]
foreach ($l in $live) { [void]$liveSet.Add($l) }
$missing = @($expected | Where-Object { -not $liveSet.Contains($_) })
$extra = @($live | Where-Object { -not $expectedSet.Contains($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
    Write-Host "`nThe live forms no longer match the structure this data was generated for:" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  expected, not live : $m" }
    foreach ($x in $extra) { Write-Host "  live, not expected : $x" }
    Write-Host "(format: form|field|type|required|options|lookup target|filter|visible when|dynamic source)"
    throw "Structure mismatch. Nothing was written. Re-run export-live-structure.ps1 and regenerate the data."
}
Write-Host "  OK - all $($expected.Count) live fields match."

# --- pre-flight 2: ledger and existing DUMMY- projects --------------------------------------

$ledger = @{}
if (Test-Path $LedgerPath) {
    foreach ($line in [System.IO.File]::ReadAllLines($LedgerPath, $utf8)) {
        if ($line.Length -eq 0) { continue }
        $parts = $line.Split("`t")
        $ledger[$parts[0]] = $parts[1]
    }
}

$projectRows = Get-Json "/api/forms/$($formIdByCode['projects'])/submissions?page=1&pageSize=200"
$liveDummyIds = @{}
foreach ($item in $projectRows.items) {
    $pc = [string]$item.values.project_code
    if ($pc.StartsWith("DUMMY-")) { $liveDummyIds[$item.id] = $pc }
}
$ledgerProjectIds = @{}
foreach ($k in $ledger.Keys) { if ($k.StartsWith("projects|")) { $ledgerProjectIds[$ledger[$k]] = $k } }
foreach ($id in $liveDummyIds.Keys) {
    if (-not $ledgerProjectIds.ContainsKey($id)) {
        throw "Project '$($liveDummyIds[$id])' already exists live but is not in this machine's ledger ($LedgerPath). Loading again would duplicate the data. Nothing was written."
    }
}
foreach ($id in $ledgerProjectIds.Keys) {
    if (-not $liveDummyIds.ContainsKey($id)) {
        throw "The ledger ($LedgerPath) lists $($ledgerProjectIds[$id]) but that project is no longer live - the dummy data was probably removed. Delete the ledger file, then run again. Nothing was written."
    }
}

# --- plan -----------------------------------------------------------------------------------

Write-Host "`nPlan:"
$pendingTotal = 0
foreach ($code in $formCodes) {
    $all = @($rows | Where-Object { $_.Form -eq $code })
    $done = @($all | Where-Object { $ledger.ContainsKey($_.Key) }).Count
    $pendingTotal += ($all.Count - $done)
    Write-Host ("  {0,-24} {1,4} rows   already written: {2,4}   to write: {3,4}" -f $code, $all.Count, $done, ($all.Count - $done))
}

if (-not $Apply) {
    Write-Host "`nDRY RUN - nothing was written. $pendingTotal rows would be written."
    Write-Host "Run again with -Apply to write them."
    return
}
if ($pendingTotal -eq 0) {
    Write-Host "`nEverything is already written. Nothing to do."
    return
}

# --- write ----------------------------------------------------------------------------------

Write-Host "`nWriting $pendingTotal rows..."
$evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
    param($m)
    $refKey = $m.Groups[1].Value
    if (-not $ledger.ContainsKey($refKey)) { throw "Row refers to '$refKey', which has not been written yet." }
    return $ledger[$refKey]
}
$written = 0
$started = Get-Date
foreach ($row in $rows) {
    if ($ledger.ContainsKey($row.Key)) { continue }
    $body = [regex]::Replace($row.Body, '@@ref:([^@]+)@@', $evaluator)
    try {
        $respText = Invoke-ApiAuthed "Post" "/api/forms/$($formIdByCode[$row.Form])/submissions" $body
    }
    catch {
        Write-Host "`nFAILED on row '$($row.Key)' (HTTP $(Get-StatusCode $_)):" -ForegroundColor Red
        Write-Host "  $(Read-ErrorBody $_)"
        Write-Host "  $($_.Exception.Message)"
        Write-Host "$written rows were written in this run and are recorded in the ledger."
        Write-Host "Fix the cause, then run the same command again - it continues from here without duplicating."
        throw "Stopped."
    }
    $newId = (ConvertFrom-Json $respText).id
    if (-not $newId) { throw "Row '$($row.Key)' was accepted but no id came back: $respText" }
    $ledger[$row.Key] = [string]$newId
    [System.IO.File]::AppendAllText($LedgerPath, "$($row.Key)`t$newId`n", $utf8)
    $written++
    if ($written % 50 -eq 0) {
        $rate = $written / ((Get-Date) - $started).TotalSeconds
        $left = [int](($pendingTotal - $written) / $rate)
        Write-Host ("  {0,4} / {1}   about {2} s left" -f $written, $pendingTotal, $left)
    }
}

# --- verify ---------------------------------------------------------------------------------

Write-Host "`nDone - $written rows written. Live row counts now:"
foreach ($code in $formCodes) {
    $page = Get-Json "/api/forms/$($formIdByCode[$code])/submissions?page=1&pageSize=1"
    $mine = @($rows | Where-Object { $_.Form -eq $code }).Count
    Write-Host ("  {0,-24} {1,5} total   ({2} of them dummy)" -f $code, $page.totalCount, $mine)
}
Write-Host "`nLedger: $LedgerPath"
