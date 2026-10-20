# Sets the "required" flag and value rules on the live forms, through the API's
# PUT /api/forms/{id}/fields/{fieldId}/rules (added 2026-10-20). Without -Apply it only
# prints what it would change; with -Apply it makes the changes. Safe to run more than once.
#
# What it sets:
#   daily-progress-report
#     report_date            required, no future dates
#     crew_count             required, at least 0
#     milestone, zone, footing, floor, mep_trade, mep_room
#                            required (each is shown only for its own phase, and only
#                            required when shown) - without these a report doesn't count
#                            toward the dashboard's work-complete figure
#     delay_cause            required when "had a delay" is yes
#     problem_description    required when "had a problem" is yes
#   stock-inflow, stock-outflow
#     date                   required, no future dates
#     quantity               required, more than 0
#   projects
#     total_floors           optional, at least 0
#
# It also rewrites any visibility condition stored as a bare JSON string ("true") as a
# JSON array (["true"]), the documented shape. The server reads both since this release;
# this only tidies the stored data.
#
# Usage (from the repo root):
#   .\scripts\go-live\apply-field-rules.ps1 -BaseUrl "https://<api host>"           # preview
#   .\scripts\go-live\apply-field-rules.ps1 -BaseUrl "https://<api host>" -Apply    # change

param(
    [string]$BaseUrl = "http://localhost:5080",
    [string]$Email,
    [switch]$Apply
)

$ErrorActionPreference = "Stop"

if (-not $Email) { $Email = Read-Host "Admin email" }
$secure = Read-Host "Password" -AsSecureString
$plain = [System.Net.NetworkCredential]::new("", $secure).Password
$loginBytes = [System.Text.Encoding]::UTF8.GetBytes((@{ email = $Email; password = $plain } | ConvertTo-Json))
$login = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -Body $loginBytes -ContentType "application/json; charset=utf-8"
$plain = $null; $loginBytes = $null
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

function Get-Json($uri) {
    $resp = Invoke-WebRequest -Uri $uri -Headers $headers -UseBasicParsing
    $stream = $resp.RawContentStream
    $stream.Position = 0
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    return ($reader.ReadToEnd() | ConvertFrom-Json)
}

function Send-Json($method, $uri, $body) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Compress))
    Invoke-RestMethod -Uri $uri -Method $method -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8" | Out-Null
}

# form code -> field code -> wanted rules. Min/Max $null = no limit.
$wanted = @{
    "daily-progress-report" = @{
        "report_date"         = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $true }
        "crew_count"          = @{ isRequired = $true;  min = 0;     max = $null; minExclusive = $false; noFutureDates = $false }
        "milestone"           = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "zone"                = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "footing"             = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "floor"               = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "mep_trade"           = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "mep_room"            = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "delay_cause"         = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
        "problem_description" = @{ isRequired = $true;  min = $null; max = $null; minExclusive = $false; noFutureDates = $false }
    }
    "stock-inflow" = @{
        "date"     = @{ isRequired = $true; min = $null; max = $null; minExclusive = $false; noFutureDates = $true }
        "quantity" = @{ isRequired = $true; min = 0;     max = $null; minExclusive = $true;  noFutureDates = $false }
    }
    "stock-outflow" = @{
        "date"     = @{ isRequired = $true; min = $null; max = $null; minExclusive = $false; noFutureDates = $true }
        "quantity" = @{ isRequired = $true; min = 0;     max = $null; minExclusive = $true;  noFutureDates = $false }
    }
    "projects" = @{
        "total_floors" = @{ isRequired = $false; min = 0; max = $null; minExclusive = $false; noFutureDates = $false }
    }
}

$mode = if ($Apply) { "APPLYING" } else { "PREVIEW (nothing is changed - add -Apply to make these changes)" }
Write-Host "`n$mode`n"

$forms = @(Get-Json "$BaseUrl/api/forms")
$changed = 0
foreach ($summary in $forms) {
    $definition = Get-Json "$BaseUrl/api/forms/$($summary.id)"
    if (-not $definition.publishedVersion) { continue }
    $fields = @($definition.publishedVersion.fields | Where-Object { $_.isActive })
    $formRules = $wanted[$summary.code]

    foreach ($field in $fields) {
        # 1. Required + value rules
        if ($formRules -and $formRules.ContainsKey($field.code)) {
            $rule = $formRules[$field.code]
            $existing = @{}
            if ($field.validationRulesJson) { $existing = $field.validationRulesJson | ConvertFrom-Json }
            $body = [ordered]@{
                isRequired    = $rule.isRequired
                min           = $rule.min
                max           = $rule.max
                minExclusive  = $rule.minExclusive
                # keep any pattern already set on a text field
                regex         = if ($existing.regex) { $existing.regex } else { $null }
                noFutureDates = $rule.noFutureDates
            }
            Write-Host ("  {0,-24} {1,-20} required={2} min={3} more-than={4} no-future={5}" -f `
                $summary.code, $field.code, $rule.isRequired, $rule.min, $rule.minExclusive, $rule.noFutureDates)
            if ($Apply) { Send-Json "Put" "$BaseUrl/api/forms/$($summary.id)/fields/$($field.id)/rules" $body }
            $changed++
        }

        # 2. Visibility condition stored as a bare string -> array
        if ($field.visibleWhenFieldCode -and $field.visibleWhenValuesJson -and -not $field.visibleWhenValuesJson.TrimStart().StartsWith("[")) {
            $value = $field.visibleWhenValuesJson | ConvertFrom-Json
            $array = "[" + (ConvertTo-Json ([string]$value) -Compress) + "]"
            Write-Host ("  {0,-24} {1,-20} visibility {2} -> {3}" -f $summary.code, $field.code, $field.visibleWhenValuesJson, $array)
            if ($Apply) {
                Send-Json "Put" "$BaseUrl/api/forms/$($summary.id)/fields/$($field.id)/visibility" @{
                    visibleWhenFieldCode  = $field.visibleWhenFieldCode
                    visibleWhenValuesJson = $array
                }
            }
            $changed++
        }
    }

    if ($formRules) {
        foreach ($code in $formRules.Keys) {
            if (-not ($fields | Where-Object { $_.code -eq $code })) {
                Write-Warning "$($summary.code): no active field '$code' - skipped"
            }
        }
    }
}

Write-Host "`n$changed change(s) $(if ($Apply) { 'made' } else { 'to make' })."
