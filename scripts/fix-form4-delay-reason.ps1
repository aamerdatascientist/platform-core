# One-off remediation: corrects structural_progress_daily's delay_reason dropdown if
# Form 4 was already published (via seed-daily-report-forms.ps1) before its options were
# fixed from Form 3's set (weather/labor/materials/equipment/access/other) to its own
# (weather/labor/materials/equipment/formwork availability/other).
#
# There's no "edit a field in place" API - Code becomes a physical column, and
# FieldDefinition is otherwise immutable once created (see FieldDefinition.cs) - so the
# real mechanism here is the same one the Form Builder UI would use: open a new draft
# version, remove the old field, add it back with corrected options, publish. The
# physical column itself (delay_reason, still a Dropdown/text column) doesn't change -
# only the OptionsJson metadata does, so this doesn't touch or lose any existing
# submissions' data for every field except delay_reason itself. Any submission that
# stored the now-removed "access" value keeps that raw string in its row - it just won't
# match any current option's label in the UI anymore, so check for that before running
# this against a form with real data.
#
# Side effect worth knowing about: DisplayOrder is assigned as "current field count" at
# AddField time (see FormVersion.AddField), and removing a field doesn't renumber the
# ones after it. So the re-added delay_reason lands at the END of the field order (after
# formwork_shortage/photo) instead of back in its original spot before them - a cosmetic
# field-ordering change in the form UI, not a data problem. There's no reorder API today;
# fix it by hand afterward (Form Builder UI, once it supports reordering) if it matters.
#
# Usage:
#   .\fix-form4-delay-reason.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..." -FormId "<structural_progress_daily's form id>"

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$FormId
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
}

$correctValues = @("weather", "labor_shortage", "material_shortage", "equipment_shortage", "formwork_availability", "other")
$correctOptionsJson = (@(
    @{ value = "weather"; label = "الطقس" }, @{ value = "labor_shortage"; label = "نقص العمالة" },
    @{ value = "material_shortage"; label = "المواد" }, @{ value = "equipment_shortage"; label = "المعدات" },
    @{ value = "formwork_availability"; label = "توفر الشدة الخشبية" }, @{ value = "other"; label = "أخرى" }
) | ConvertTo-Json -Compress)

$form = Invoke-RestMethod -Uri "$BaseUrl/api/forms/$FormId" -Headers $headers
$currentField = ($form.publishedVersion.fields + $form.draftVersion.fields) | Where-Object { $_.code -eq "delay_reason" } | Select-Object -First 1

if ($null -eq $currentField) {
    Write-Host "No 'delay_reason' field found on form $FormId - nothing to fix."
    exit 0
}

# Comparing parsed values rather than the raw optionsJson strings - ConvertTo-Json's
# \uXXXX-escaped output for Arabic never matches the already-unescaped text Invoke-
# RestMethod hands back from the API's response, even when the underlying data is
# identical, so a raw string comparison here would always (harmlessly, but confusingly)
# report a mismatch.
$currentValues = @(($currentField.optionsJson | ConvertFrom-Json) | ForEach-Object { $_.value })
if (@(Compare-Object $currentValues $correctValues -SyncWindow 0).Count -eq 0) {
    Write-Host "'delay_reason' already has the correct options - nothing to fix."
    exit 0
}

Write-Host "Current options: $($currentField.optionsJson)"
Write-Host "Opening a new draft version..."
Invoke-RestMethod -Uri "$BaseUrl/api/forms/$FormId/versions" -Method Post -Headers $headers | Out-Null

$draftForm = Invoke-RestMethod -Uri "$BaseUrl/api/forms/$FormId" -Headers $headers
$draftField = $draftForm.draftVersion.fields | Where-Object { $_.code -eq "delay_reason" }
if ($null -eq $draftField) {
    throw "Draft version has no 'delay_reason' field - was it removed already? Check the form manually before re-running."
}

Write-Host "Removing the old 'delay_reason' field ($($draftField.id))..."
Invoke-RestMethod -Uri "$BaseUrl/api/forms/$FormId/fields/$($draftField.id)" -Method Delete -Headers $headers | Out-Null

Write-Host "Re-adding 'delay_reason' with corrected options..."
$addFieldJson = @{
    code                   = "delay_reason"
    label                  = "السبب"
    fieldType              = "Dropdown"
    isRequired             = $false
    optionsJson            = $correctOptionsJson
    lookupFormDefinitionId = $null
    validationRulesJson    = $null
} | ConvertTo-Json
Invoke-JsonPost "$BaseUrl/api/forms/$FormId/fields" $addFieldJson | Out-Null

Write-Host "Publishing the corrected version..."
Invoke-RestMethod -Uri "$BaseUrl/api/forms/$FormId/publish" -Method Post -Headers $headers | Out-Null

Write-Host "Done - structural_progress_daily's delay_reason now has: توفر الشدة الخشبية (in place of الوصول)."
