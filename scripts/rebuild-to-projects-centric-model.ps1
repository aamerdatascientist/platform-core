# Rebuilds the live environment around a proper Projects master-data table and makes
# zone/footing/floor genuinely per-project, per Aamer's decision on 2026-10-03 - see
# claude/daily-progress-dropdown-options-stakeholder-questions.md (Construction Software
# Claude.ai project) for why the old seeded dropdown options were placeholders, and his
# AskUserQuestion answers choosing "delete literally everything except the new form" and
# "redesign zone/footing/floor to be project-specific now".
#
# What this does, in order (the order is load-bearing - see the comments at each step):
#
#   1. Archives daily_progress_report's "project" field. Sounds backwards, but it's
#      required: DeleteFormCommand refuses to delete a form that's still the target of any
#      ACTIVE Lookup field anywhere - and the old "projects" form can't be deleted (step 2)
#      while this form (which we're keeping) still actively points at it. Archiving (not
#      deleting) makes the field inactive without touching its column or data, which is
#      exactly what unblocks the delete. It gets restored and repointed in step 4.
#   2. Deletes every other existing form (soft-delete, reversible - see CLAUDE.md's
#      partial-unique-index note for why this also frees codes like "projects" for reuse).
#      Old forms reference each other (e.g. daily-site-report/equipment-log/labor-log/
#      task-tracking all Lookup into "projects"), so this runs as a multi-pass retry loop:
#      whatever can't be deleted yet because something else still points at it just
#      succeeds on a later pass, once that something else is gone.
#   3. Creates a new, richer "Projects" form (code: projects - just freed by step 2) plus
#      three small per-project master-data forms: project_zones, project_footings,
#      project_floors.
#   4. Restores daily_progress_report's "project" field, then repoints it at the new
#      Projects form (metadata-only, always safe - same FieldType, just a different
#      target; the restore briefly leaves it pointed at the now-deleted old form, fixed by
#      the very next call in this script).
#   5. Converts zone/footing/floor from Dropdown to Lookup, each pointed at its matching
#      new master-data form, then sets a lookup-filter of "project" on each so the choices
#      narrow to whichever project was picked.
#   6. Seeds ONE demo project with 3 rows in each master-data form, clearly marked as
#      demo data - replace with the real stakeholder-sourced lists once available (see
#      the stakeholder-questions doc).
#
# Known, expected leftover: if a workflow was ever created against "stock-adjustment" on
# THIS environment (seed-stock-adjustment-workflow.ps1), that form - and anything it's the
# only remaining reason to keep, like "materials"/"locations" - CANNOT be deleted here.
# There is no delete-workflow endpoint in this codebase (a known, deliberate gap, not a
# bug - see CLAUDE.md). This script does not work around that; it tries every form, reports
# exactly which ones failed and why at the end, and moves on rather than aborting.
#
# Step 5 can also genuinely fail on its own: the app refuses a Dropdown -> Lookup
# conversion outright if ANY existing submission already has a non-GUID value sitting in
# that column (e.g. "zone_3" left over from your own testing of the branching feature). If
# that happens, this script prints the API's own error (which names the offending record
# Ids) and stops there rather than forcing or silently skipping it - tell Claude and it's a
# one-line SQL fix to clear those old test values, not a code change.
#
# Deliberately NOT touched: mep_room. It currently reuses floor_1..floor_6 values, but the
# agreed methodology defines MEP's real unit as "rooms within a floor" (~20/floor), not the
# floor itself - a genuine mismatch already flagged in the stakeholder-questions doc.
# That's a design decision for the stakeholder, not a mechanical field edit, so this script
# leaves mep_room exactly as it is.
#
# Prerequisites: a running API and a JWT for an Administrator account.
#
# Usage:
#   .\rebuild-to-projects-centric-model.ps1 -BaseUrl "https://..." -Token "eyJhbGc..."

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    # Explicit UTF-8 bytes, not a string -Body - Windows PowerShell 5.1 mangles Arabic
    # otherwise (see CLAUDE.md's Invoke-RestMethod gotcha).
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
}

function Invoke-JsonPut($uri, $json) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
}

function New-Form($code, $name, $moduleName, $description) {
    $json = @{ code = $code; name = $name; moduleName = $moduleName; description = $description } | ConvertTo-Json
    $resp = Invoke-JsonPost "$BaseUrl/api/forms" $json
    Write-Host "Created form '$name' -> $($resp.id)"
    return $resp.id
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
        visibleWhenFieldCode   = $null
        visibleWhenValuesJson  = $null
        filterByFieldCode      = $null
    } | ConvertTo-Json
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$formId/fields" $json
    return $resp.id
}

function Publish-Form($formId, $name) {
    Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/publish" -Method Post -Headers $headers | Out-Null
    Write-Host "Published '$name'"
}

function Options($pairs) {
    return ($pairs | ConvertTo-Json -Compress)
}

function Submit-Data($formId, $values) {
    $json = $values | ConvertTo-Json -Depth 5
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$formId/submissions" $json
    return $resp.id
}

# --- Step 0: locate daily_progress_report and its "project" field -----------

Write-Host "`n--- Step 0: locating daily_progress_report ---"
$allForms = Invoke-RestMethod -Uri "$BaseUrl/api/forms" -Headers $headers
$keep = $allForms | Where-Object { $_.code -eq "daily_progress_report" }
if (-not $keep) {
    throw "Could not find a form with code 'daily_progress_report' - aborting before changing anything."
}
Write-Host "Keeping: '$($keep.name)' ($($keep.id))"

$dpr = Invoke-RestMethod -Uri "$BaseUrl/api/forms/$($keep.id)" -Headers $headers
$projectField = $dpr.publishedVersion.fields | Where-Object { $_.code -eq "project" }
if (-not $projectField) {
    throw "daily_progress_report has no 'project' field - aborting."
}

# --- Step 1: archive the project field so "projects" becomes deletable ------

Write-Host "`n--- Step 1: archiving daily_progress_report.project (temporary) ---"
Invoke-RestMethod -Uri "$BaseUrl/api/forms/$($keep.id)/fields/$($projectField.id)/archive" -Method Post -Headers $headers | Out-Null
Write-Host "Archived. Data and column are untouched - this is reversible and gets restored in step 4."

# --- Step 2: delete every other form, retrying for cross-references --------

Write-Host "`n--- Step 2: deleting old forms ---"
$toDelete = $allForms | Where-Object { $_.code -ne "daily_progress_report" } | ForEach-Object {
    [PSCustomObject]@{ Id = $_.id; Name = $_.name; Code = $_.code; LastError = $null }
}

$maxPasses = 6
for ($pass = 1; $pass -le $maxPasses -and $toDelete.Count -gt 0; $pass++) {
    Write-Host "Pass $pass ($($toDelete.Count) remaining)..."
    $stillRemaining = @()
    $deletedThisPass = 0
    foreach ($f in $toDelete) {
        try {
            Invoke-RestMethod -Uri "$BaseUrl/api/forms/$($f.Id)" -Method Delete -Headers $headers | Out-Null
            Write-Host "  Deleted '$($f.Name)' ($($f.Code))"
            $deletedThisPass++
        }
        catch {
            $f.LastError = $_.Exception.Message
            $stillRemaining += $f
        }
    }
    $toDelete = $stillRemaining
    if ($deletedThisPass -eq 0) {
        Write-Host "No progress this pass - stopping retries."
        break
    }
}

if ($toDelete.Count -gt 0) {
    Write-Host "`nCould not delete $($toDelete.Count) form(s) - leaving them in place:"
    foreach ($f in $toDelete) {
        Write-Host "  '$($f.Name)' ($($f.Code)): $($f.LastError)"
    }
    Write-Host "(Most likely cause: a workflow attached to one of them - there's no delete-workflow" `
        "endpoint in this codebase yet, a known gap, not a bug. See the comment block at the top of this script.)"
}

# --- Step 3: new Projects form + three per-project master-data forms --------

Write-Host "`n--- Step 3: creating new master-data forms ---"

$projectsId = New-Form "projects" "المشاريع" "Operations" "Project master data - one row per construction project"
Add-Field $projectsId "project_code" "رمز المشروع" "ShortText" $true | Out-Null
Add-Field $projectsId "project_name" "اسم المشروع" "ShortText" $true | Out-Null
Add-Field $projectsId "client_name" "اسم العميل" "ShortText" $false | Out-Null
Add-Field $projectsId "city" "المدينة" "ShortText" $false | Out-Null
Add-Field $projectsId "project_type" "نوع المشروع" "Dropdown" $false (Options @(
        @{ value = "residential"; label = "سكني" }, @{ value = "commercial"; label = "تجاري" },
        @{ value = "infrastructure"; label = "بنية تحتية" }, @{ value = "industrial"; label = "صناعي" },
        @{ value = "other"; label = "أخرى" }
    )) | Out-Null
Add-Field $projectsId "status" "الحالة" "Dropdown" $true (Options @(
        @{ value = "planning"; label = "التخطيط" }, @{ value = "active"; label = "نشط" },
        @{ value = "on_hold"; label = "متوقف مؤقتاً" }, @{ value = "completed"; label = "مكتمل" },
        @{ value = "cancelled"; label = "ملغى" }
    )) | Out-Null
Add-Field $projectsId "project_manager" "مدير المشروع" "ShortText" $false | Out-Null
Add-Field $projectsId "total_floors" "عدد الطوابق" "Number" $false | Out-Null
Add-Field $projectsId "start_date" "تاريخ البدء" "DateTime" $false | Out-Null
Add-Field $projectsId "expected_completion" "تاريخ الانتهاء المتوقع" "DateTime" $false | Out-Null
Add-Field $projectsId "notes" "ملاحظات" "LongText" $false | Out-Null
Publish-Form $projectsId "المشاريع"

$zonesId = New-Form "project_zones" "مناطق المشروع" "Operations" "Excavation zones, per project"
Add-Field $zonesId "project" "المشروع" "Lookup" $true $null $projectsId | Out-Null
Add-Field $zonesId "zone_code" "رمز المنطقة" "ShortText" $true | Out-Null
Add-Field $zonesId "zone_label" "اسم المنطقة" "ShortText" $true | Out-Null
Publish-Form $zonesId "مناطق المشروع"

$footingsId = New-Form "project_footings" "قواعد المشروع" "Operations" "Foundation footings, per project"
Add-Field $footingsId "project" "المشروع" "Lookup" $true $null $projectsId | Out-Null
Add-Field $footingsId "footing_code" "رمز القاعدة" "ShortText" $true | Out-Null
Add-Field $footingsId "footing_label" "اسم القاعدة" "ShortText" $true | Out-Null
Publish-Form $footingsId "قواعد المشروع"

$floorsId = New-Form "project_floors" "طوابق المشروع" "Operations" "Structural floors, per project"
Add-Field $floorsId "project" "المشروع" "Lookup" $true $null $projectsId | Out-Null
Add-Field $floorsId "floor_code" "رمز الطابق" "ShortText" $true | Out-Null
Add-Field $floorsId "floor_label" "اسم الطابق" "ShortText" $true | Out-Null
Publish-Form $floorsId "طوابق المشروع"

# --- Step 4: restore + repoint daily_progress_report.project ----------------

Write-Host "`n--- Step 4: restoring and repointing daily_progress_report.project ---"
Invoke-RestMethod -Uri "$BaseUrl/api/forms/$($keep.id)/fields/$($projectField.id)/restore" -Method Post -Headers $headers | Out-Null
Invoke-JsonPut "$BaseUrl/api/forms/$($keep.id)/fields/$($projectField.id)/type" `
    (@{ newFieldType = "Lookup"; optionsJson = $null; lookupFormDefinitionId = $projectsId } | ConvertTo-Json) | Out-Null
Write-Host "Restored and repointed 'project' -> new Projects form."

# --- Step 5: convert zone/footing/floor to project-filtered Lookups ---------

Write-Host "`n--- Step 5: converting zone/footing/floor to Lookup ---"

function Find-Field($code) {
    $f = $dpr.publishedVersion.fields | Where-Object { $_.code -eq $code }
    if (-not $f) { throw "daily_progress_report has no field with code '$code' - can't convert it." }
    return $f
}

function Convert-DropdownToProjectLookup($code, $targetFormId) {
    $field = Find-Field $code
    try {
        Invoke-JsonPut "$BaseUrl/api/forms/$($keep.id)/fields/$($field.id)/type" `
            (@{ newFieldType = "Lookup"; optionsJson = $null; lookupFormDefinitionId = $targetFormId } | ConvertTo-Json) | Out-Null
    }
    catch {
        Write-Host "FAILED converting '$code' to Lookup - most likely an existing test submission still has a"
        Write-Host "non-GUID value (like 'zone_3') sitting in that column. API error:"
        Write-Host "  $($_.Exception.Message)"
        Write-Host "Tell Claude if you hit this - clearing the old test value is a one-line SQL fix, not a code change."
        throw
    }
    Invoke-JsonPut "$BaseUrl/api/forms/$($keep.id)/fields/$($field.id)/lookup-filter" `
        (@{ filterByFieldCode = "project" } | ConvertTo-Json) | Out-Null
    Write-Host "Converted '$code' -> Lookup, filtered by project."
}

Convert-DropdownToProjectLookup "zone" $zonesId
Convert-DropdownToProjectLookup "footing" $footingsId
Convert-DropdownToProjectLookup "floor" $floorsId

# --- Step 6: demo data (placeholder - replace with real stakeholder data) ---

Write-Host "`n--- Step 6: seeding one demo project ---"

$demoProjectId = Submit-Data $projectsId @{
    project_code = "DEMO-01"; project_name = "مشروع تجريبي"; status = "active"
}
Write-Host "Demo project -> $demoProjectId"

foreach ($i in 1..3) {
    Submit-Data $zonesId @{ project = $demoProjectId; zone_code = "zone_$i"; zone_label = "المنطقة $i" } | Out-Null
}
foreach ($i in 1..3) {
    Submit-Data $footingsId @{ project = $demoProjectId; footing_code = "footing_$i"; footing_label = "القاعدة $i" } | Out-Null
}
foreach ($i in 1..3) {
    Submit-Data $floorsId @{ project = $demoProjectId; floor_code = "floor_$i"; floor_label = "الطابق $i" } | Out-Null
}
Write-Host "Seeded 3 demo zones/footings/floors under the demo project."

Write-Host "`nDone."
Write-Host "In the live form: pick phase 'excavation'/'foundation'/'structural', then pick the demo project" `
    "('مشروع تجريبي') - zone/footing/floor should each narrow to just that project's 3 demo rows."
Write-Host "Delete the demo project ('DEMO-01') once real projects are seeded from the stakeholder's data."
