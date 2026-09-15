# One-off remediation: corrects structural_progress_daily's delay_reason dropdown if
# Form 4 was already published (via seed-daily-report-forms.ps1) before its options were
# fixed from Form 3's set (weather/labor/materials/equipment/access/other) to its own
# (weather/labor/materials/equipment/formwork availability/other).
#
# Updated to use the real field-editing API. There is now a proper "change this field's
# type/options" endpoint, so this is a single in-place metadata update rather than the
# open-a-draft / remove-the-field / re-add-it / publish dance this script used to do. The
# column and its data are untouched, and - the part that actually went wrong last time - the
# field keeps its position on the form instead of being pushed to the end.
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
Write-Host "Updating 'delay_reason' options in place..."

# One call, no draft cycle, no remove-and-re-add. The change-type endpoint takes the field's
# EXISTING type plus new options, so this is a pure metadata update: the delay_reason column
# and every value already recorded in it are untouched, and the field keeps its position on
# the form. That position is the bit that used to break - the old version of this script
# removed the field and re-added it, which pushed it to the end of the form because
# DisplayOrder was assigned from the field count at the time of the add.
$changeTypeJson = @{
    newFieldType           = "Dropdown"
    optionsJson            = $correctOptionsJson
    lookupFormDefinitionId = $null
} | ConvertTo-Json

$bytes = [System.Text.Encoding]::UTF8.GetBytes($changeTypeJson)
Invoke-RestMethod -Uri "$BaseUrl/api/forms/$FormId/fields/$($currentField.id)/type" -Method Put `
    -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8" | Out-Null

Write-Host "Done - structural_progress_daily's delay_reason now has: توفر الشدة الخشبية (in place of الوصول)."
