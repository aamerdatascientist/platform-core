# Seeds the single, phase-branching "Daily Progress Report" form - the conditional-field
# design from claude/daily-progress-conditional-form-design.md (see the Construction
# Software Claude.ai project), built on top of the new conditional-visibility feature
# (FieldDefinition.VisibleWhenFieldCode/VisibleWhenValuesJson, PUT
# /api/forms/{id}/fields/{fieldId}/visibility).
#
# One form, not five: a Phase dropdown branches the rest of the form. Every phase gets the
# identical 3-question shape (unit selector, problems-today, plan-completed-today) - only
# the unit selector's field/options differ per phase, and only the unit selector is
# genuinely conditional. "Any problems today?" and "Was all of today's planned work
# completed?" apply the same way regardless of phase, so they're ordinary always-visible
# fields, not five duplicated conditional ones.
#
# Field/dropdown-option LABELS are Arabic; every form Code, field Code, and dropdown
# option value stays English/ASCII - same convention as seed-daily-report-forms.ps1.
# Placeholder unit counts (9 zones / 12 footings / 6 floors / 4 milestones) are from
# claude/daily-progress-calculation-method-v3.md - swap for the real excavation zone
# grid, footing schedule, and permit milestone list once available.
#
# Prerequisites: a running API, a JWT, and the Projects form from
# seed-operations-forms.ps1 already published (pass its form Id as -ProjectsFormId).
#
# Usage:
#   .\seed-daily-progress-report-form.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..." -ProjectsFormId "3f2e..."

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$ProjectsFormId
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    # Explicit UTF-8 bytes, not a string -Body - see seed-operations-forms.ps1 for why
    # Windows PowerShell 5.1 mangles Arabic characters otherwise.
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

# visibleWhenFieldCode/visibleWhenValuesJson are the new bit - every other field (the
# universal header fields, the two always-visible questions) just omits them, same as
# every existing seed script's Add-Field calls.
function Add-Field(
    $formId, $code, $label, $fieldType, $isRequired, $optionsJson = $null, $lookupFormDefinitionId = $null,
    $visibleWhenFieldCode = $null, $visibleWhenValuesJson = $null
) {
    $json = @{
        code                   = $code
        label                   = $label
        fieldType               = $fieldType
        isRequired              = $isRequired
        optionsJson             = $optionsJson
        lookupFormDefinitionId  = $lookupFormDefinitionId
        validationRulesJson     = $null
        visibleWhenFieldCode    = $visibleWhenFieldCode
        visibleWhenValuesJson   = $visibleWhenValuesJson
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

# --- The form itself --------------------------------------------------------

$formId = New-Form "daily_progress_report" "تقرير التقدم اليومي" "Daily Reports" `
    "نموذج واحد موحد لتتبع التقدم اليومي حسب المرحلة - يتغير جزء الأسئلة بحسب المرحلة المختارة"

# Universal header fields, every phase.
Add-Field $formId "report_date" "تاريخ التقرير" "DateTime" $true | Out-Null
Add-Field $formId "project" "المشروع" "Lookup" $true $null $ProjectsFormId | Out-Null
Add-Field $formId "crew_count" "عدد العمالة الحاضرة اليوم" "Number" $true | Out-Null
$weatherOptions = Options @(
    @{ value = "clear"; label = "صافٍ" }, @{ value = "rain"; label = "ممطر" },
    @{ value = "extreme_heat"; label = "حر شديد" }, @{ value = "sandstorm"; label = "عاصفة رملية" },
    @{ value = "other"; label = "أخرى" }
)
Add-Field $formId "weather" "حالة الطقس" "Dropdown" $true $weatherOptions | Out-Null
Add-Field $formId "had_delay" "هل حدث تأخير أو تعطيل اليوم؟" "Boolean" $true | Out-Null
$delayCauseFieldId = Add-Field $formId "delay_cause" "سبب التأخير" "ShortText" $false
# Conditional-visibility demo #1: delay_cause only matters when had_delay is true - safe
# edit, applied after creation via the PUT .../visibility endpoint (the same thing the
# Form Builder's "Only show when..." panel calls), exactly as it would work post-hoc.
Invoke-JsonPut "$BaseUrl/api/forms/$formId/fields/$delayCauseFieldId/visibility" `
    (@{ visibleWhenFieldCode = "had_delay"; visibleWhenValuesJson = (Options @("true")) } | ConvertTo-Json) | Out-Null
Add-Field $formId "site_photo" "صورة من الموقع" "Attachment" $false | Out-Null

# The branching field. Every unit-selector field below is only visible when Phase matches.
$phaseOptions = Options @(
    @{ value = "paperwork"; label = "الأعمال الورقية" }, @{ value = "excavation"; label = "الحفر" },
    @{ value = "foundation"; label = "الأساسات" }, @{ value = "structural"; label = "الهيكل الإنشائي" },
    @{ value = "mep"; label = "الأعمال الكهروميكانيكية" }
)
Add-Field $formId "phase" "المرحلة" "Dropdown" $true $phaseOptions | Out-Null

function Add-ConditionalField($code, $label, $fieldType, $optionsJson, $phaseValue) {
    $fieldId = Add-Field $formId $code $label $fieldType $false $optionsJson
    Invoke-JsonPut "$BaseUrl/api/forms/$formId/fields/$fieldId/visibility" `
        (@{ visibleWhenFieldCode = "phase"; visibleWhenValuesJson = (Options @($phaseValue)) } | ConvertTo-Json) | Out-Null
    return $fieldId
}

# Paperwork - 4 placeholder milestones (claude/daily-progress-calculation-method-v3.md).
$milestoneOptions = Options @(
    @{ value = "design_approval"; label = "اعتماد التصاميم" }, @{ value = "permit_application"; label = "تقديم طلب الترخيص" },
    @{ value = "utility_approvals"; label = "موافقات الخدمات" }, @{ value = "final_permit"; label = "إصدار الترخيص النهائي" }
)
Add-ConditionalField "milestone" "المعلم" "Dropdown" $milestoneOptions "paperwork" | Out-Null

# Excavation - 9 placeholder zones.
$zoneOptions = Options @(1..9 | ForEach-Object { @{ value = "zone_$_"; label = "المنطقة $_" } })
Add-ConditionalField "zone" "المنطقة" "Dropdown" $zoneOptions "excavation" | Out-Null

# Foundation - 12 placeholder footings.
$footingOptions = Options @(1..12 | ForEach-Object { @{ value = "footing_$_"; label = "القاعدة $_" } })
Add-ConditionalField "footing" "القاعدة" "Dropdown" $footingOptions "foundation" | Out-Null

# Structural - 6 floors.
$floorOptions = Options @(1..6 | ForEach-Object { @{ value = "floor_$_"; label = "الطابق $_" } })
Add-ConditionalField "floor" "الطابق" "Dropdown" $floorOptions "structural" | Out-Null

# MEP - trade + room/floor. Two separate conditional fields, both gated on phase=mep.
$tradeOptions = Options @(
    @{ value = "electrical"; label = "الكهرباء" }, @{ value = "plumbing"; label = "السباكة" }, @{ value = "hvac"; label = "التكييف" }
)
Add-ConditionalField "mep_trade" "التخصص" "Dropdown" $tradeOptions "mep" | Out-Null
$mepRoomOptions = Options @(1..6 | ForEach-Object { @{ value = "floor_$_"; label = "الطابق $_" } })
Add-ConditionalField "mep_room" "الطابق/الغرفة" "Dropdown" $mepRoomOptions "mep" | Out-Null

# Universal, always-visible questions - same shape regardless of which phase is selected.
Add-Field $formId "has_problem_today" "هل توجد أي مشاكل اليوم؟" "Boolean" $true | Out-Null
$problemDescFieldId = Add-Field $formId "problem_description" "وصف المشكلة" "LongText" $false
# Conditional-visibility demo #2: same pattern as delay_cause above - only show the
# free-text description once has_problem_today is actually true.
Invoke-JsonPut "$BaseUrl/api/forms/$formId/fields/$problemDescFieldId/visibility" `
    (@{ visibleWhenFieldCode = "has_problem_today"; visibleWhenValuesJson = (Options @("true")) } | ConvertTo-Json) | Out-Null
Add-Field $formId "plan_completed_today" "هل تم إنجاز كل العمل المخطط له اليوم؟" "Boolean" $true | Out-Null

Publish-Form $formId "تقرير التقدم اليومي"

Write-Host ""
Write-Host "Done. Try it: pick different 'phase' values in the Form Builder preview or the" `
    "real form - only the matching unit-selector field should appear each time."
