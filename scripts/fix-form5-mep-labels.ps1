# One-off remediation: mep_progress_daily (Form 5) never successfully published, because
# three of its field labels shared a 63-byte common prefix (see DynamicSchemaService's new
# BuildSafeDisplayAlias / CLAUDE.md's known-gotchas list) - Postgres silently truncates any
# identifier, including a quoted reporting-view column alias, to 63 bytes, so all three
# collided into one identifier and CREATE VIEW failed with a duplicate-column error at
# publish time.
#
# Two independent fixes together resolve this:
#   1. DynamicSchemaService.RefreshReportingViewAsync now hash-disambiguates any label that
#      would collide after truncation (general fix - protects every form, not just this one).
#   2. The three MEP labels themselves are also reworded (trade name first) so they're
#      distinct well before 63 bytes, for a readable reporting-view column name rather than
#      a hash-suffixed one.
#
# Since Form 5 never successfully published, formDefinition.TableName is still null in the
# live database - nothing was ever actually marked Published, so there's no submitted data
# to lose. (EnsureTableForPublishedVersionAsync DOES run and commit before
# RefreshReportingViewAsync, so a physical Data_MepProgressDaily table may already exist
# from the failed attempt - that's fine: its create-or-alter logic checks the real Postgres
# catalog, not the FormDefinition row, so it detects and reuses that table rather than
# erroring on a second attempt.) This script deletes the broken (unpublished) form
# definition outright and recreates it from scratch with the corrected labels, rather than
# patching fields in place - safe specifically because nothing was ever published, per
# DeleteFormCommand's own "TableName is null -> hard delete, nothing to preserve" logic.
# It refuses to delete anything if the form turns out to already be published (unexpected
# given the diagnosis, but checked rather than assumed).
#
# Forms 1-4 are untouched by this script - they published and seeded correctly already.
#
# Usage:
#   .\fix-form5-mep-labels.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..." `
#       -ProjectsFormId "<projects form id>" -ProjectAId "<Project A id from the original run>" `
#       -ProjectBId "<Project B id from the original run>"

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$ProjectsFormId,
    [Parameter(Mandatory = $true)][string]$ProjectAId,
    [Parameter(Mandatory = $true)][string]$ProjectBId
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
}

function New-Form($code, $name, $moduleName, $description) {
    $json = @{ code = $code; name = $name; moduleName = $moduleName; description = $description } | ConvertTo-Json
    $resp = Invoke-JsonPost "$BaseUrl/api/forms" $json
    Write-Host "Created form '$name' -> $($resp.id)"
    return $resp.id
}

function Add-Field($formId, $code, $label, $fieldType, $isRequired, $optionsJson = $null, $lookupFormDefinitionId = $null) {
    $json = @{
        code                    = $code
        label                   = $label
        fieldType               = $fieldType
        isRequired              = $isRequired
        optionsJson             = $optionsJson
        lookupFormDefinitionId  = $lookupFormDefinitionId
        validationRulesJson     = $null
    } | ConvertTo-Json
    Invoke-JsonPost "$BaseUrl/api/forms/$formId/fields" $json | Out-Null
}

function Publish-Form($formId, $name) {
    Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/publish" -Method Post -Headers $headers | Out-Null
    Write-Host "Published '$name'"
}

function Submit-Data($formId, $values) {
    $json = $values | ConvertTo-Json -Depth 5
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$formId/submissions" $json
    return $resp.id
}

# --- Find and remove the broken form, if it exists --------------------------

$existingForms = Invoke-RestMethod -Uri "$BaseUrl/api/forms?moduleName=Daily%20Reports" -Headers $headers
$existing = $existingForms | Where-Object { $_.code -eq "mep_progress_daily" }

if ($existing) {
    $fullExisting = Invoke-RestMethod -Uri "$BaseUrl/api/forms/$($existing.id)" -Headers $headers
    if ($fullExisting.status -eq "Published" -or $fullExisting.tableName) {
        throw "mep_progress_daily ($($existing.id)) already shows as published (status=$($fullExisting.status), tableName=$($fullExisting.tableName)) - that contradicts the 'never successfully published' diagnosis this script assumes. Stopping without deleting anything; check the form manually."
    }
    Write-Host "Found the broken, unpublished mep_progress_daily ($($existing.id)) - deleting it..."
    Invoke-RestMethod -Uri "$BaseUrl/api/forms/$($existing.id)" -Method Delete -Headers $headers | Out-Null
} else {
    Write-Host "No existing mep_progress_daily form found - creating fresh."
}

# --- Recreate with the corrected labels --------------------------------------

$floorOptions = (@(
    @{ value = "floor_1"; label = "الطابق 1" }, @{ value = "floor_2"; label = "الطابق 2" },
    @{ value = "floor_3"; label = "الطابق 3" }, @{ value = "floor_4"; label = "الطابق 4" },
    @{ value = "floor_5"; label = "الطابق 5" }, @{ value = "floor_6"; label = "الطابق 6" }
) | ConvertTo-Json -Compress)

$mepId = New-Form "mep_progress_daily" "التقدم اليومي لأعمال الكهرباء والسباكة والتكييف" "Daily Reports" "Daily MEP works progress"
Add-Field $mepId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $mepId "project" "المشروع" "Lookup" $true $null $ProjectsFormId
Add-Field $mepId "floor" "أي طابق يتم العمل عليه اليوم؟" "Dropdown" $true $floorOptions
Add-Field $mepId "electrical_crew_count" "عدد عمالة الكهرباء اليوم" "Number" $false
Add-Field $mepId "electrical_area_sqm" "كهرباء - الأمتار المربعة المنجزة اليوم" "Number" $false
Add-Field $mepId "plumbing_crew_count" "عدد عمالة السباكة اليوم" "Number" $false
Add-Field $mepId "plumbing_area_sqm" "سباكة - الأمتار المربعة المنجزة اليوم" "Number" $false
Add-Field $mepId "hvac_crew_count" "عدد عمالة التكييف اليوم" "Number" $false
Add-Field $mepId "hvac_area_sqm" "تكييف - الأمتار المربعة المنجزة اليوم" "Number" $false
Add-Field $mepId "is_blocked" "هل هناك ما يعيق العمل اليوم؟" "Boolean" $true
Add-Field $mepId "blocked_details" "إذا كان الأمر كذلك، أي طاقم ولماذا؟" "LongText" $false
Publish-Form $mepId "التقدم اليومي لأعمال الكهرباء والسباكة والتكييف"

Submit-Data $mepId @{
    report_date = "2026-09-10"; project = $ProjectAId; floor = "floor_1"
    electrical_crew_count = 8; electrical_area_sqm = 120; plumbing_crew_count = 6; plumbing_area_sqm = 90
    hvac_crew_count = 4; hvac_area_sqm = 60; is_blocked = $false; blocked_details = $null
} | Out-Null
Submit-Data $mepId @{
    report_date = "2026-09-10"; project = $ProjectBId; floor = "floor_1"
    electrical_crew_count = 3; electrical_area_sqm = 30; plumbing_crew_count = 0; plumbing_area_sqm = 0
    hvac_crew_count = 0; hvac_area_sqm = 0; is_blocked = $true; blocked_details = "طاقم السباكة لم يصل بسبب تأخر تصريح الدخول"
} | Out-Null
Write-Host "  2 sample rows submitted (1 per project)."
Write-Host ""
Write-Host "Done. mep_progress_daily -> $mepId"
