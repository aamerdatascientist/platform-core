# Seeds the 5 Arabic daily-report forms via the API, then submits sample rows for two
# different projects into each one so Project-scoping (the "Project" Lookup filter added
# to GetFormSubmissionsQuery/FormView) can be verified end-to-end: filtering by Project A
# should never surface Project B's rows, and vice versa.
#
# Field/dropdown-option LABELS are Arabic; every form Code, field Code, and dropdown
# option value stays English/ASCII, same convention as seed-operations-forms.ps1 - those
# become real SQL identifiers/values via DynamicSchemaService and must never be
# translated.
#
# Technical notes carried over from the pre-build check for this task:
#   - FieldType has no calculated/formula type and no Multi-select type. None of these 5
#     forms need a formula field. Form 1's one multi-select ("which checklist items closed
#     today?") is built as one Boolean field per item instead of a single multi-select
#     field - it fits the existing schema exactly and keeps each item independently
#     queryable/reportable.
#   - Attachment fields (the "photo" fields on forms 2-4) are declared here but no file is
#     uploaded by this script - file upload is a separate two-step API (create the record,
#     then POST to the Files endpoint with the real RecordId), which no existing seed
#     script demonstrates either. Upload real photos through the UI after seeding.
#   - Form 4's delay-reason dropdown options weren't separately specified in the original
#     brief (it said "alongside weather/headcount/m²/delay/reason/photo" without restating
#     the option list) - reused Form 3's labor/material/equipment/access/other set for
#     consistency. Flagged here as the one judgment call in this script; change
#     $structuralDelayReasons below if a different set is wanted.
#
# Prerequisites: a running API, a JWT, and the Projects form from
# seed-operations-forms.ps1 already published (pass its form Id as -ProjectsFormId).
#
# Usage:
#   .\seed-daily-report-forms.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..." -ProjectsFormId "3f2e..."

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$ProjectsFormId
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    # See seed-operations-forms.ps1 for why this can't just be Invoke-RestMethod -Body
    # $json directly - Windows PowerShell 5.1 mangles non-ASCII (Arabic) characters in a
    # string body unless it's sent as explicit UTF-8 bytes.
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

function Options($pairs) {
    return ($pairs | ConvertTo-Json -Compress)
}

function Submit-Data($formId, $values) {
    $json = $values | ConvertTo-Json -Depth 5
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$formId/submissions" $json
    return $resp.id
}

$floorOptions = Options @(
    @{ value = "floor_1"; label = "الطابق 1" }, @{ value = "floor_2"; label = "الطابق 2" },
    @{ value = "floor_3"; label = "الطابق 3" }, @{ value = "floor_4"; label = "الطابق 4" },
    @{ value = "floor_5"; label = "الطابق 5" }, @{ value = "floor_6"; label = "الطابق 6" }
)

# --- Two sample projects to demonstrate scoping -----------------------------

Write-Host "Submitting two sample projects for scoping verification..."
$projectA = Submit-Data $ProjectsFormId @{
    project_code = "PRJ-2026-DR1"; project_name = "مشروع الاختبار أ - الواحة"; status = "active"
    start_date = "2026-06-01"; expected_completion = "2027-06-01"
}
$projectB = Submit-Data $ProjectsFormId @{
    project_code = "PRJ-2026-DR2"; project_name = "مشروع الاختبار ب - النخيل"; status = "active"
    start_date = "2026-06-01"; expected_completion = "2027-06-01"
}
Write-Host "  Project A -> $projectA"
Write-Host "  Project B -> $projectB"

# --- Form 1: سجل الأعمال الورقية والتعبئة اليومي ----------------------------

$paperworkId = New-Form "paperwork_mobilization_daily" "سجل الأعمال الورقية والتعبئة اليومي" "Daily Reports" "Daily paperwork and mobilization checklist"
Add-Field $paperworkId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $paperworkId "project" "المشروع" "Lookup" $true $null $ProjectsFormId
Add-Field $paperworkId "closed_survey_soil_report" "المسح وتقرير التربة" "Boolean" $false
Add-Field $paperworkId "closed_permit_request" "طلب الترخيص" "Boolean" $false
Add-Field $paperworkId "closed_municipal_approval" "الموافقة البلدية" "Boolean" $false
Add-Field $paperworkId "closed_subcontractor_agreements" "اتفاقيات المقاولين الفرعيين" "Boolean" $false
Add-Field $paperworkId "closed_material_preorder" "الطلب المسبق للمواد" "Boolean" $false
Add-Field $paperworkId "closed_site_mobilization" "تعبئة الموقع" "Boolean" $false
Add-Field $paperworkId "closed_other" "أخرى" "Boolean" $false
Add-Field $paperworkId "has_pending_item" "هل يوجد بند معلّق حاليًا بانتظار جهة خارجية؟" "Boolean" $true
Add-Field $paperworkId "pending_authority" "إذا كان معلّقًا، ما الجهة؟" "ShortText" $false
Publish-Form $paperworkId "سجل الأعمال الورقية والتعبئة اليومي"

Submit-Data $paperworkId @{
    report_date = "2026-09-10"; project = $projectA
    closed_survey_soil_report = $true; closed_permit_request = $true; closed_municipal_approval = $false
    closed_subcontractor_agreements = $false; closed_material_preorder = $false; closed_site_mobilization = $false; closed_other = $false
    has_pending_item = $true; pending_authority = "الأمانة العامة لمنطقة الرياض"
} | Out-Null
Submit-Data $paperworkId @{
    report_date = "2026-09-10"; project = $projectB
    closed_survey_soil_report = $true; closed_permit_request = $true; closed_municipal_approval = $true
    closed_subcontractor_agreements = $true; closed_material_preorder = $false; closed_site_mobilization = $false; closed_other = $false
    has_pending_item = $false; pending_authority = $null
} | Out-Null
Write-Host "  2 sample rows submitted (1 per project)."

# --- Form 2: التقدم اليومي للحفر والدعم -------------------------------------

$excavationId = New-Form "excavation_shoring_daily" "التقدم اليومي للحفر والدعم" "Daily Reports" "Daily excavation and shoring progress"
Add-Field $excavationId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $excavationId "project" "المشروع" "Lookup" $true $null $ProjectsFormId
Add-Field $excavationId "weather" "حالة الطقس" "Dropdown" $true (Options @(
    @{ value = "clear"; label = "صافٍ" }, @{ value = "rain"; label = "ممطر" },
    @{ value = "extreme_heat"; label = "حر شديد" }, @{ value = "sandstorm"; label = "عاصفة رملية" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $excavationId "crew_count" "عدد العمالة الحاضرة اليوم" "Number" $true
Add-Field $excavationId "area_progress_sqm" "الأمتار المربعة المحفورة/المدعومة اليوم" "Number" $true
Add-Field $excavationId "had_delay" "هل حدث تأخير أو تعطيل اليوم؟" "Boolean" $true
Add-Field $excavationId "delay_reason" "السبب" "Dropdown" $false (Options @(
    @{ value = "weather"; label = "الطقس" }, @{ value = "equipment"; label = "المعدات" },
    @{ value = "access"; label = "الوصول" }, @{ value = "utility_conflict"; label = "تعارض المرافق" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $excavationId "photo" "صورة لوجه الحفر اليوم" "Attachment" $false
Publish-Form $excavationId "التقدم اليومي للحفر والدعم"

Submit-Data $excavationId @{
    report_date = "2026-09-10"; project = $projectA; weather = "clear"
    crew_count = 18; area_progress_sqm = 240; had_delay = $false; delay_reason = $null
} | Out-Null
Submit-Data $excavationId @{
    report_date = "2026-09-10"; project = $projectB; weather = "sandstorm"
    crew_count = 9; area_progress_sqm = 60; had_delay = $true; delay_reason = "weather"
} | Out-Null
Write-Host "  2 sample rows submitted (1 per project)."

# --- Form 3: التقدم اليومي للأساسات ------------------------------------------

$foundationReasons = Options @(
    @{ value = "labor_shortage"; label = "نقص العمالة" }, @{ value = "material_shortage"; label = "نقص المواد" },
    @{ value = "equipment_shortage"; label = "نقص المعدات" }, @{ value = "access"; label = "الوصول" },
    @{ value = "other"; label = "أخرى" }
)

$foundationId = New-Form "foundation_progress_daily" "التقدم اليومي للأساسات" "Daily Reports" "Daily foundation works progress"
Add-Field $foundationId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $foundationId "project" "المشروع" "Lookup" $true $null $ProjectsFormId
Add-Field $foundationId "weather" "حالة الطقس" "Dropdown" $true (Options @(
    @{ value = "clear"; label = "صافٍ" }, @{ value = "rain"; label = "ممطر" },
    @{ value = "extreme_heat"; label = "حر شديد" }, @{ value = "sandstorm"; label = "عاصفة رملية" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $foundationId "crew_count" "عدد العمالة العاملة على الأساسات اليوم" "Number" $true
Add-Field $foundationId "area_progress_sqm" "الأمتار المربعة المنفذة من الأساسات اليوم" "Number" $true
Add-Field $foundationId "had_delay" "هل حدث تأخير أو تعطيل اليوم؟" "Boolean" $true
Add-Field $foundationId "delay_reason" "السبب" "Dropdown" $false $foundationReasons
Add-Field $foundationId "photo" "صورة لتقدم الأساسات اليوم" "Attachment" $false
Publish-Form $foundationId "التقدم اليومي للأساسات"

Submit-Data $foundationId @{
    report_date = "2026-09-10"; project = $projectA; weather = "clear"
    crew_count = 22; area_progress_sqm = 180; had_delay = $false; delay_reason = $null
} | Out-Null
Submit-Data $foundationId @{
    report_date = "2026-09-10"; project = $projectB; weather = "clear"
    crew_count = 14; area_progress_sqm = 95; had_delay = $true; delay_reason = "material_shortage"
} | Out-Null
Write-Host "  2 sample rows submitted (1 per project)."

# --- Form 4: التقدم اليومي للهيكل الإنشائي (6 طوابق) -------------------------

# See the header note: this reuses Form 3's reason set - the original brief didn't
# restate delay-reason options for this form specifically.
$structuralDelayReasons = $foundationReasons

$structuralId = New-Form "structural_progress_daily" "التقدم اليومي للهيكل الإنشائي (6 طوابق)" "Daily Reports" "Daily structural works progress"
Add-Field $structuralId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $structuralId "project" "المشروع" "Lookup" $true $null $ProjectsFormId
Add-Field $structuralId "floor" "أي طابق يتم العمل عليه اليوم؟" "Dropdown" $true $floorOptions
Add-Field $structuralId "weather" "حالة الطقس" "Dropdown" $true (Options @(
    @{ value = "clear"; label = "صافٍ" }, @{ value = "rain"; label = "ممطر" },
    @{ value = "extreme_heat"; label = "حر شديد" }, @{ value = "sandstorm"; label = "عاصفة رملية" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $structuralId "crew_count" "عدد العمالة العاملة على الهيكل الإنشائي اليوم" "Number" $true
Add-Field $structuralId "area_progress_sqm" "الأمتار المربعة المنفذة من الهيكل الإنشائي اليوم" "Number" $true
Add-Field $structuralId "had_delay" "هل حدث تأخير أو تعطيل اليوم؟" "Boolean" $true
Add-Field $structuralId "delay_reason" "السبب" "Dropdown" $false $structuralDelayReasons
Add-Field $structuralId "formwork_shortage" "هل هناك عائق في توفر الشدة الخشبية يعيق عمل اليوم؟" "Boolean" $true
Add-Field $structuralId "photo" "صورة لتقدم الهيكل الإنشائي اليوم" "Attachment" $false
Publish-Form $structuralId "التقدم اليومي للهيكل الإنشائي (6 طوابق)"

Submit-Data $structuralId @{
    report_date = "2026-09-10"; project = $projectA; floor = "floor_2"; weather = "clear"
    crew_count = 30; area_progress_sqm = 310; had_delay = $false; delay_reason = $null; formwork_shortage = $false
} | Out-Null
Submit-Data $structuralId @{
    report_date = "2026-09-10"; project = $projectB; floor = "floor_1"; weather = "clear"
    crew_count = 12; area_progress_sqm = 90; had_delay = $true; delay_reason = "equipment_shortage"; formwork_shortage = $true
} | Out-Null
Write-Host "  2 sample rows submitted (1 per project)."

# --- Form 5: التقدم اليومي لأعمال الكهرباء والسباكة والتكييف -----------------

$mepId = New-Form "mep_progress_daily" "التقدم اليومي لأعمال الكهرباء والسباكة والتكييف" "Daily Reports" "Daily MEP works progress"
Add-Field $mepId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $mepId "project" "المشروع" "Lookup" $true $null $ProjectsFormId
Add-Field $mepId "floor" "أي طابق يتم العمل عليه اليوم؟" "Dropdown" $true $floorOptions
Add-Field $mepId "electrical_crew_count" "عدد عمالة الكهرباء اليوم" "Number" $false
Add-Field $mepId "electrical_area_sqm" "الأمتار المربعة المنفذة من أعمال الكهرباء اليوم" "Number" $false
Add-Field $mepId "plumbing_crew_count" "عدد عمالة السباكة اليوم" "Number" $false
Add-Field $mepId "plumbing_area_sqm" "الأمتار المربعة المنفذة من أعمال السباكة اليوم" "Number" $false
Add-Field $mepId "hvac_crew_count" "عدد عمالة التكييف اليوم" "Number" $false
Add-Field $mepId "hvac_area_sqm" "الأمتار المربعة المنفذة من أعمال التكييف اليوم" "Number" $false
Add-Field $mepId "is_blocked" "هل هناك ما يعيق العمل اليوم؟" "Boolean" $true
Add-Field $mepId "blocked_details" "إذا كان الأمر كذلك، أي طاقم ولماذا؟" "LongText" $false
Publish-Form $mepId "التقدم اليومي لأعمال الكهرباء والسباكة والتكييف"

Submit-Data $mepId @{
    report_date = "2026-09-10"; project = $projectA; floor = "floor_1"
    electrical_crew_count = 8; electrical_area_sqm = 120; plumbing_crew_count = 6; plumbing_area_sqm = 90
    hvac_crew_count = 4; hvac_area_sqm = 60; is_blocked = $false; blocked_details = $null
} | Out-Null
Submit-Data $mepId @{
    report_date = "2026-09-10"; project = $projectB; floor = "floor_1"
    electrical_crew_count = 3; electrical_area_sqm = 30; plumbing_crew_count = 0; plumbing_area_sqm = 0
    hvac_crew_count = 0; hvac_area_sqm = 0; is_blocked = $true; blocked_details = "طاقم السباكة لم يصل بسبب تأخر تصريح الدخول"
} | Out-Null
Write-Host "  2 sample rows submitted (1 per project)."

Write-Host ""
Write-Host "Done. Form Ids:"
Write-Host "  paperwork_mobilization_daily -> $paperworkId"
Write-Host "  excavation_shoring_daily     -> $excavationId"
Write-Host "  foundation_progress_daily    -> $foundationId"
Write-Host "  structural_progress_daily    -> $structuralId"
Write-Host "  mep_progress_daily           -> $mepId"
