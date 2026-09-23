# Creates and publishes the MEP daily-report form as f-006.
#
# Context: the MEP form was never actually created. The form occupying f-005 is a
# different, intentional project-intake form - this script never reads, writes, or
# publishes it, and refuses to run if f-006 somehow resolves to it.
#
# ---------------------------------------------------------------------------------------
# FIELD CODES ARE LOAD-BEARING - do not "tidy" them
#
# Only Arabic labels were specified for this form. The field CODES below are chosen to
# match exactly what seed-month-of-activity.ps1 submits for MEP rows:
#
#   report_date, project, floor, electrical_crew_count, electrical_area_sqm,
#   plumbing_crew_count, plumbing_area_sqm, hvac_crew_count, hvac_area_sqm,
#   is_blocked, blocked_details
#
# Change any of them here and that seed script's MEP submissions will be rejected by
# SubmissionValueValidator. Codes also become physical column names, so they must be
# Latin - Arabic in a Code is rejected outright (form.field.codeMustBeLatin).
#
# ---------------------------------------------------------------------------------------
# LABELS
#
# The three "m² completed today" labels are deliberately trade-name-first
# ("كهرباء - ...", "سباكة - ...", "تكييف - ..."). Written the other way round they share a
# 63-byte UTF-8 prefix and Postgres truncates all three reporting-view column aliases to
# the same identifier, which is what made CREATE VIEW fail on the original MEP form. These
# labels were checked: longest is 70 bytes, worst shared prefix between any two is 23
# bytes, so nothing collides even before BuildSafeDisplayAlias's hash fallback.
#
# Usage:
#   .\create-mep-form-f006.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..." `
#       -ProjectsFormId "<the projects form's id>"

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    # The Lookup target for the Project field. Unchanged by this script - it is only read,
    # and only to point field 2 at it, exactly as the other four daily forms do.
    [Parameter(Mandatory = $true)][string]$ProjectsFormId,
    [string]$FormCode = "f-006",
    [string]$FormName = "التقدم اليومي لأعمال الكهرباء والسباكة والتكييف",
    [string]$ModuleName = "النماذج اليومية"
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    # Explicit UTF-8 bytes + charset: PowerShell 5.1 silently turns Arabic in a string
    # -Body into '?' at send time.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
}

function Add-Field($formId, $code, $label, $fieldType, $isRequired, $optionsJson = $null, $lookupFormDefinitionId = $null) {
    $json = @{
        code                   = $code
        label                  = $label
        fieldType              = $fieldType
        isRequired             = $isRequired
        optionsJson            = $optionsJson
        lookupFormDefinitionId = $lookupFormDefinitionId
        validationRulesJson    = $null
    } | ConvertTo-Json
    Invoke-JsonPost "$BaseUrl/api/forms/$formId/fields" $json | Out-Null
    Write-Host "  + $code"
}

# --- Pre-flight -------------------------------------------------------------------------

$allForms = Invoke-RestMethod -Uri "$BaseUrl/api/forms" -Method Get -Headers $headers

$existing = $allForms | Where-Object { $_.code -eq $FormCode } | Select-Object -First 1
if ($existing) {
    throw "A form with code '$FormCode' already exists ($($existing.id), status $($existing.status)). Stopping rather than creating a duplicate or modifying it - inspect it first."
}

# Explicit guard: f-005 is a different-purpose form and must not be touched by this script.
$f005 = $allForms | Where-Object { $_.code -eq "f-005" } | Select-Object -First 1
if ($f005) {
    Write-Host "Note: f-005 exists ('$($f005.name)') and is deliberately left untouched by this script."
}

$projectsForm = $allForms | Where-Object { $_.id -eq $ProjectsFormId } | Select-Object -First 1
if ($null -eq $projectsForm) {
    throw "-ProjectsFormId '$ProjectsFormId' does not match any form. Field 2 is a Lookup and needs the real projects form id."
}
if ($projectsForm.status -ne "Published") {
    throw "The Lookup target '$($projectsForm.code)' is $($projectsForm.status), not Published - a Lookup to an unpublished form can't resolve."
}
Write-Host "Lookup target for المشروع: '$($projectsForm.code)' ($ProjectsFormId) - read only, not modified."
Write-Host ""

# --- Create -----------------------------------------------------------------------------

$createJson = @{
    code        = $FormCode
    name        = $FormName
    moduleName  = $ModuleName
    description = "Daily MEP (electrical / plumbing / HVAC) progress"
} | ConvertTo-Json

$formId = (Invoke-JsonPost "$BaseUrl/api/forms" $createJson).id
Write-Host "Created form '$FormName' [$FormCode] -> $formId"

$floorOptions = (@(
    @{ value = "floor_1"; label = "الطابق 1" }, @{ value = "floor_2"; label = "الطابق 2" },
    @{ value = "floor_3"; label = "الطابق 3" }, @{ value = "floor_4"; label = "الطابق 4" },
    @{ value = "floor_5"; label = "الطابق 5" }, @{ value = "floor_6"; label = "الطابق 6" }
) | ConvertTo-Json -Compress)

Add-Field $formId "report_date"           "تاريخ التقرير"                          "DateTime" $true
Add-Field $formId "project"               "المشروع"                                 "Lookup"   $true  $null $ProjectsFormId
Add-Field $formId "floor"                 "أي طابق يتم العمل عليه اليوم؟"           "Dropdown" $true  $floorOptions
Add-Field $formId "electrical_crew_count" "عدد عمالة الكهرباء الحاضرة اليوم"        "Number"   $false
Add-Field $formId "electrical_area_sqm"   "كهرباء - الأمتار المربعة المنجزة اليوم"  "Number"   $false
Add-Field $formId "plumbing_crew_count"   "عدد عمالة السباكة الحاضرة اليوم"         "Number"   $false
Add-Field $formId "plumbing_area_sqm"     "سباكة - الأمتار المربعة المنجزة اليوم"   "Number"   $false
Add-Field $formId "hvac_crew_count"       "عدد عمالة التكييف الحاضرة اليوم"         "Number"   $false
Add-Field $formId "hvac_area_sqm"         "تكييف - الأمتار المربعة المنجزة اليوم"   "Number"   $false
Add-Field $formId "is_blocked"            "هل واجه أي طاقم عائقًا اليوم؟"           "Boolean"  $true
Add-Field $formId "blocked_details"       "إذا كان كذلك، أي طاقم والسبب"            "LongText" $false

# --- Publish ----------------------------------------------------------------------------

Write-Host ""
Write-Host "Publishing..."
$published = Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/publish" -Method Post -Headers $headers

Write-Host ""
Write-Host "--- Done -------------------------------------------------------------------"
Write-Host "  Code:       $FormCode"
Write-Host "  Form ID:    $formId"
Write-Host "  Module:     $ModuleName"
Write-Host "  Version:    $($published.versionNumber)"
Write-Host "  Table:      $($published.tableName)"
Write-Host ""
Write-Host "Record the Form ID above - that is the real id to report back."
