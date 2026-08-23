# Seeds all Phase 2 Operations forms via the API, in dependency order
# (Projects, Equipment, Trades first, then the four transactional forms), then submits
# realistic sample data rows into every form, again master data before the
# transactional forms that Lookup into it.
#
# Field/dropdown-option LABELS are Arabic (real Saudi construction-site terminology).
# Every form Code, field Code, and dropdown option value is unchanged English/ASCII -
# those become real SQL identifiers/values via DynamicSchemaService and must never be
# translated (an Arabic Code previously caused a real validation crash during the RTL
# work - see CLAUDE.md).
#
# Prerequisites: same as seed-stock-management-forms.ps1 - a running API, a JWT, and
# the JsonStringEnumConverter fix already in Program.cs.
#
# Optional: pass -LocationsFormId if you have it (from the Stock Management seed run)
# to wire Projects.location as a real Lookup to Locations. If you don't have it handy,
# leave it out - the script skips that one field with a warning rather than failing.
# NOTE: even when -LocationsFormId is supplied, the sample Project rows below leave
# `location` unset - actual Locations record IDs only exist inside
# seed-stock-management-forms.ps1's own run and aren't returned/exposed to this script,
# and the field is optional, so leaving it blank here is honest rather than fabricating
# a GUID. Add real location links to Projects by hand afterward if you want them.
#
# Usage:
#   .\seed-operations-forms.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..." -LocationsFormId "3f2e..."

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $false)][string]$LocationsFormId
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function New-Form($code, $name, $moduleName, $description) {
    $body = @{ code = $code; name = $name; moduleName = $moduleName; description = $description } | ConvertTo-Json
    $resp = Invoke-RestMethod -Uri "$BaseUrl/api/forms" -Method Post -Headers $headers -Body $body -ContentType "application/json"
    Write-Host "Created form '$name' -> $($resp.id)"
    return $resp.id
}

function Add-Field($formId, $code, $label, $fieldType, $isRequired, $optionsJson = $null, $lookupFormDefinitionId = $null) {
    $body = @{
        code                    = $code
        label                   = $label
        fieldType               = $fieldType
        isRequired              = $isRequired
        optionsJson             = $optionsJson
        lookupFormDefinitionId  = $lookupFormDefinitionId
        validationRulesJson     = $null
    } | ConvertTo-Json
    Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/fields" -Method Post -Headers $headers -Body $body -ContentType "application/json" | Out-Null
}

function Publish-Form($formId, $name) {
    Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/publish" -Method Post -Headers $headers | Out-Null
    Write-Host "Published '$name'"
}

function Options($pairs) {
    return ($pairs | ConvertTo-Json -Compress)
}

function Submit-Data($formId, $values) {
    # $values keys are field Codes (unchanged English) - only the data itself is Arabic
    # where relevant (names, summaries, notes). Returns the new record's Id (a real
    # GUID from the API response), so later forms can Lookup into it for real instead
    # of a fabricated one.
    $body = $values | ConvertTo-Json -Depth 5
    $resp = Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/submissions" -Method Post -Headers $headers -Body $body -ContentType "application/json"
    return $resp.id
}

# --- Master data (forms) ---------------------------------------------------

$projectsId = New-Form "projects" "المشاريع" "Operations" "Project master data"
Add-Field $projectsId "project_code" "رمز المشروع" "ShortText" $true
Add-Field $projectsId "project_name" "اسم المشروع" "ShortText" $true
Add-Field $projectsId "status" "الحالة" "Dropdown" $true (Options @(
    @{ value = "planning"; label = "التخطيط" }, @{ value = "active"; label = "نشط" },
    @{ value = "on_hold"; label = "متوقف مؤقتاً" }, @{ value = "completed"; label = "مكتمل" }
))
if ($LocationsFormId) {
    Add-Field $projectsId "location" "الموقع" "Lookup" $false $null $LocationsFormId
} else {
    Write-Host "WARNING: -LocationsFormId not supplied - skipping Projects.location field. Add it manually later if needed."
}
Add-Field $projectsId "start_date" "تاريخ البدء" "DateTime" $false
Add-Field $projectsId "expected_completion" "تاريخ الانتهاء المتوقع" "DateTime" $false
Publish-Form $projectsId "المشاريع"

$equipmentId = New-Form "equipment" "المعدات" "Operations" "Equipment and plant master data"
Add-Field $equipmentId "equipment_code" "رمز المعدة" "ShortText" $true
Add-Field $equipmentId "equipment_name" "اسم المعدة" "ShortText" $true
Add-Field $equipmentId "equipment_type" "نوع المعدة" "Dropdown" $true (Options @(
    @{ value = "heavy_machinery"; label = "المعدات الثقيلة" }, @{ value = "power_tools"; label = "الأدوات الكهربائية" },
    @{ value = "vehicles"; label = "المركبات" }, @{ value = "generators"; label = "المولدات" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $equipmentId "status" "الحالة" "Dropdown" $true (Options @(
    @{ value = "available"; label = "متاحة" }, @{ value = "in_use"; label = "قيد الاستخدام" },
    @{ value = "under_maintenance"; label = "تحت الصيانة" }, @{ value = "out_of_service"; label = "خارج الخدمة" }
))
Publish-Form $equipmentId "المعدات"

$tradesId = New-Form "trades" "المهن الحرفية" "Operations" "Labour trade categories"
Add-Field $tradesId "trade_name" "اسم المهنة" "ShortText" $true
Publish-Form $tradesId "المهن الحرفية"

# --- Master data (sample rows) ---------------------------------------------

Write-Host ""
Write-Host "Submitting sample projects..."
$projWaha = Submit-Data $projectsId @{
    project_code = "PRJ-2026-014"; project_name = "أبراج الواحة السكنية"; status = "active"
    start_date = "2026-03-01"; expected_completion = "2027-06-30"
}
$projNakheel = Submit-Data $projectsId @{
    project_code = "PRJ-2026-021"; project_name = "مجمع النخيل التجاري"; status = "active"
    start_date = "2026-05-15"; expected_completion = "2027-12-31"
}
$projSchool = Submit-Data $projectsId @{
    project_code = "PRJ-2025-098"; project_name = "مدرسة الأمل الابتدائية"; status = "completed"
    start_date = "2025-02-01"; expected_completion = "2026-01-15"
}
Write-Host "  3 projects submitted."

Write-Host "Submitting sample equipment..."
$eqExcavator = Submit-Data $equipmentId @{ equipment_code = "EQP-EXC-01"; equipment_name = "حفارة كاتربيلر 320"; equipment_type = "heavy_machinery"; status = "available" }
$eqCrane     = Submit-Data $equipmentId @{ equipment_code = "EQP-CRN-01"; equipment_name = "رافعة برجية"; equipment_type = "heavy_machinery"; status = "in_use" }
$eqGenerator = Submit-Data $equipmentId @{ equipment_code = "EQP-GEN-01"; equipment_name = "مولد كهرباء 100 كيلوواط"; equipment_type = "generators"; status = "in_use" }
$eqMixer     = Submit-Data $equipmentId @{ equipment_code = "EQP-MIX-01"; equipment_name = "خلاطة خرسانة صغيرة"; equipment_type = "heavy_machinery"; status = "under_maintenance" }
$eqDrillRig  = Submit-Data $equipmentId @{ equipment_code = "EQP-DRL-02"; equipment_name = "مثقاب أرضي"; equipment_type = "power_tools"; status = "available" }
Write-Host "  5 equipment items submitted."

Write-Host "Submitting sample trades..."
$tradeSteelFixer  = Submit-Data $tradesId @{ trade_name = "حداد مسلح" }
$tradeCarpenter   = Submit-Data $tradesId @{ trade_name = "نجار مسلح" }
$tradePlumber     = Submit-Data $tradesId @{ trade_name = "سباك" }
$tradeElectrician = Submit-Data $tradesId @{ trade_name = "كهربائي" }
$tradeMason       = Submit-Data $tradesId @{ trade_name = "عامل بناء" }
Write-Host "  5 trades submitted."

# --- Transactional forms (forms) --------------------------------------------

$dailyReportId = New-Form "daily-site-report" "التقرير اليومي للموقع" "Operations" "End-of-day site summary"
Add-Field $dailyReportId "report_date" "تاريخ التقرير" "DateTime" $true
Add-Field $dailyReportId "project" "المشروع" "Lookup" $true $null $projectsId
Add-Field $dailyReportId "weather" "حالة الطقس" "Dropdown" $true (Options @(
    @{ value = "clear"; label = "صافٍ" }, @{ value = "cloudy"; label = "غائم" },
    @{ value = "rain"; label = "ممطر" }, @{ value = "extreme_heat"; label = "حر شديد" },
    @{ value = "sandstorm"; label = "عاصفة رملية" }, @{ value = "other"; label = "أخرى" }
))
Add-Field $dailyReportId "crew_count" "عدد العمالة" "Number" $true
Add-Field $dailyReportId "work_summary" "ملخص الأعمال" "LongText" $true
Add-Field $dailyReportId "issues_delays" "المشاكل / التأخيرات" "LongText" $false
Add-Field $dailyReportId "site_photo" "صورة الموقع" "Attachment" $false
Add-Field $dailyReportId "submitted_by" "مُعِد التقرير" "ShortText" $true
Publish-Form $dailyReportId "التقرير اليومي للموقع"

$equipmentLogId = New-Form "equipment-log" "سجل تشغيل المعدات" "Operations" "Daily equipment usage"
Add-Field $equipmentLogId "log_date" "تاريخ السجل" "DateTime" $true
Add-Field $equipmentLogId "equipment" "المعدة" "Lookup" $true $null $equipmentId
Add-Field $equipmentLogId "project" "المشروع" "Lookup" $true $null $projectsId
Add-Field $equipmentLogId "operator" "المشغل" "ShortText" $true
Add-Field $equipmentLogId "hours_used" "ساعات التشغيل" "Decimal" $true
Add-Field $equipmentLogId "fuel_consumed" "الوقود المستهلك" "Decimal" $false
Add-Field $equipmentLogId "status_at_end_of_day" "الحالة نهاية اليوم" "Dropdown" $true (Options @(
    @{ value = "operational"; label = "تعمل بكفاءة" }, @{ value = "needs_maintenance"; label = "تحتاج صيانة" },
    @{ value = "broken_down"; label = "معطلة" }
))
Add-Field $equipmentLogId "maintenance_notes" "ملاحظات الصيانة" "LongText" $false
Publish-Form $equipmentLogId "سجل تشغيل المعدات"

$laborLogId = New-Form "labor-log" "سجل العمالة" "Operations" "Daily headcount and hours by trade"
Add-Field $laborLogId "log_date" "تاريخ السجل" "DateTime" $true
Add-Field $laborLogId "project" "المشروع" "Lookup" $true $null $projectsId
Add-Field $laborLogId "trade" "المهنة" "Lookup" $true $null $tradesId
Add-Field $laborLogId "headcount" "عدد العمال" "Number" $true
Add-Field $laborLogId "hours_worked" "ساعات العمل" "Decimal" $true
Add-Field $laborLogId "supervisor" "المشرف" "ShortText" $true
Add-Field $laborLogId "notes" "ملاحظات" "LongText" $false
Publish-Form $laborLogId "سجل العمالة"

$taskTrackingId = New-Form "task-tracking" "متابعة المهام" "Operations" "Work items assigned against a project"
Add-Field $taskTrackingId "task_reference" "رقم المهمة" "ShortText" $true
Add-Field $taskTrackingId "project" "المشروع" "Lookup" $true $null $projectsId
Add-Field $taskTrackingId "task_description" "وصف المهمة" "LongText" $true
Add-Field $taskTrackingId "assigned_to" "المسؤول عن التنفيذ" "ShortText" $true
Add-Field $taskTrackingId "priority" "الأولوية" "Dropdown" $true (Options @(
    @{ value = "low"; label = "منخفضة" }, @{ value = "medium"; label = "متوسطة" },
    @{ value = "high"; label = "عالية" }, @{ value = "urgent"; label = "عاجلة" }
))
Add-Field $taskTrackingId "status" "الحالة" "Dropdown" $true (Options @(
    @{ value = "not_started"; label = "لم تبدأ" }, @{ value = "in_progress"; label = "قيد التنفيذ" },
    @{ value = "blocked"; label = "معلّقة" }, @{ value = "completed"; label = "مكتملة" }
))
Add-Field $taskTrackingId "due_date" "تاريخ الاستحقاق" "DateTime" $false
Add-Field $taskTrackingId "completed_date" "تاريخ الإنجاز" "DateTime" $false
Publish-Form $taskTrackingId "متابعة المهام"

# --- Transactional forms (sample rows) --------------------------------------

Write-Host ""
Write-Host "Submitting sample daily site reports..."
Submit-Data $dailyReportId @{
    report_date = "2026-08-20"; project = $projWaha; weather = "clear"; crew_count = 24
    work_summary = "تم صب خرسانة أعمدة الطابق الثالث والاستمرار في أعمال حديد التسليح للسقف"
    issues_delays = ""; submitted_by = "محمد العتيبي"
} | Out-Null
Submit-Data $dailyReportId @{
    report_date = "2026-08-21"; project = $projWaha; weather = "extreme_heat"; crew_count = 18
    work_summary = "أعمال تركيب الطوب في الواجهة الشرقية واستكمال أعمال السباكة الداخلية"
    issues_delays = "تأخر وصول شحنة الحديد بسبب الازدحام المروري"; submitted_by = "سعود القحطاني"
} | Out-Null
Submit-Data $dailyReportId @{
    report_date = "2026-08-21"; project = $projNakheel; weather = "sandstorm"; crew_count = 10
    work_summary = "أعمال حفر الأساسات متوقفة جزئياً بسبب العاصفة الرملية، مع استكمال أعمال السور المؤقت"
    issues_delays = "توقف العمل لمدة 3 ساعات بسبب سوء الرؤية"; submitted_by = "فهد الدوسري"
} | Out-Null
Write-Host "  3 daily site reports submitted."

Write-Host "Submitting sample equipment log entries..."
Submit-Data $equipmentLogId @{
    log_date = "2026-08-20"; equipment = $eqExcavator; project = $projWaha; operator = "عبدالرحمن الزهراني"
    hours_used = 8; fuel_consumed = 45; status_at_end_of_day = "operational"; maintenance_notes = ""
} | Out-Null
Submit-Data $equipmentLogId @{
    log_date = "2026-08-20"; equipment = $eqCrane; project = $projWaha; operator = "ماجد السبيعي"
    hours_used = 9; fuel_consumed = 0; status_at_end_of_day = "operational"; maintenance_notes = ""
} | Out-Null
Submit-Data $equipmentLogId @{
    log_date = "2026-08-21"; equipment = $eqGenerator; project = $projNakheel; operator = "سلطان الرشيدي"
    hours_used = 6; fuel_consumed = 30; status_at_end_of_day = "needs_maintenance"
    maintenance_notes = "ضوضاء غير معتادة أثناء التشغيل، يحتاج فحص فني"
} | Out-Null
Write-Host "  3 equipment log entries submitted."

Write-Host "Submitting sample labor log entries..."
Submit-Data $laborLogId @{
    log_date = "2026-08-20"; project = $projWaha; trade = $tradeSteelFixer; headcount = 12; hours_worked = 8
    supervisor = "محمد العتيبي"; notes = "أعمال تسليح أعمدة الطابق الثالث"
} | Out-Null
Submit-Data $laborLogId @{
    log_date = "2026-08-20"; project = $projWaha; trade = $tradeCarpenter; headcount = 8; hours_worked = 8
    supervisor = "سعود القحطاني"; notes = ""
} | Out-Null
Submit-Data $laborLogId @{
    log_date = "2026-08-21"; project = $projNakheel; trade = $tradeMason; headcount = 6; hours_worked = 7
    supervisor = "فهد الدوسري"; notes = "بناء جدران السور المحيط بالموقع"
} | Out-Null
Write-Host "  3 labor log entries submitted."

Write-Host "Submitting sample tasks..."
Submit-Data $taskTrackingId @{
    task_reference = "TSK-2026-0045"; project = $projWaha; task_description = "فحص جودة صب خرسانة أعمدة الطابق الثالث"
    assigned_to = "فريق ضمان الجودة"; priority = "high"; status = "in_progress"; due_date = "2026-08-22"
} | Out-Null
Submit-Data $taskTrackingId @{
    task_reference = "TSK-2026-0046"; project = $projWaha; task_description = "طلب توريد إضافي لحديد التسليح قطر 16 مم"
    assigned_to = "قسم المشتريات"; priority = "urgent"; status = "not_started"; due_date = "2026-08-24"
} | Out-Null
Submit-Data $taskTrackingId @{
    task_reference = "TSK-2026-0038"; project = $projNakheel; task_description = "استكمال أعمال حفر الأساسات"
    assigned_to = "فريق الحفر"; priority = "medium"; status = "blocked"; due_date = "2026-08-25"
} | Out-Null
Write-Host "  3 tasks submitted."

Write-Host ""
Write-Host "All Operations forms created, published, and seeded with sample data."
