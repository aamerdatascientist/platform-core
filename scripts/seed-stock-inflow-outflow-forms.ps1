# Creates the two-form Stock Module Aamer asked for (2026-10-04): "Stock Inflow" (receiving/
# buying stock and storing it) and "Stock Outflow" (issuing it back out). Field list is his,
# refined through a short Q&A - see claude/stock-module-inflow-outflow-design.md in the
# "Construction Software" Claude.ai project for the full writeup. Short version of the
# decisions that shape this script:
#
#   - "Recorded by"/"Issued by" were dropped entirely - every submission on every form
#     already automatically records who submitted it and when (CreatedByUserId/CreatedAtUtc,
#     surfaced as "Submitted By"/"Submitted At" in the reporting view), so there's nothing to
#     add as a real field.
#   - Outflow's "Material"/"Unit"/"Issued from" (storage location) all needed to be "whatever
#     was actually entered on Inflow" - not a fixed list, and not a separate master-data form
#     either (Aamer confirmed Inflow's own entries ARE the list). That's a genuinely new
#     platform capability, not something the existing Lookup feature did: a Dropdown whose
#     OPTIONS are the live, distinct values of another form's field, instead of a fixed
#     OptionsJson list or a Lookup's whole target-form ROWS. See
#     FieldDefinition.DynamicOptionsSourceFormDefinitionId/DynamicOptionsSourceFieldCode on
#     the backend (new this session) - Add-Field's last two parameters wire it from here.
#   - Outflow's separate "Issued to" (a destination project) was dropped - just one "project"
#     field on Outflow, same meaning as Inflow's.
#   - Material (Inflow) and Reason (Outflow) both use the existing "dropdown + other opens a
#     text box" branching feature (VisibleWhenFieldCode/VisibleWhenValuesJson) - nothing new
#     needed there.
#
# ORDER MATTERS: Inflow has to be created AND PUBLISHED before Outflow, because Outflow's
# dynamic-options fields are validated against Inflow's ACTIVE, PUBLISHED fields at the
# moment they're added (same cross-aggregate check the Lookup-filter feature already used).
#
# Prerequisites: a running API (migrated - see 20261004120000_AddFieldDynamicOptionsSource)
# and a JWT for an Administrator account. Also assumes a "projects" form already exists
# (created by rebuild-to-projects-centric-model.ps1) - this script looks it up by code
# rather than taking its Id as a parameter.
#
# Usage:
#   .\seed-stock-inflow-outflow-forms.ps1 -BaseUrl "https://..." -Token "eyJhbGc..."

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

function New-Form($code, $name, $moduleName, $description) {
    $json = @{ code = $code; name = $name; moduleName = $moduleName; description = $description } | ConvertTo-Json
    $resp = Invoke-JsonPost "$BaseUrl/api/forms" $json
    Write-Host "Created form '$name' -> $($resp.id)"
    return $resp.id
}

function Options($pairs) {
    # ConvertTo-Json's well-known gotcha: a single-element array is unwrapped to a bare
    # scalar instead of a one-item JSON array (e.g. @("other") -> "other", not ["other"]).
    # Every OptionsJson call here has several options so it's never hit in practice, but
    # VisibleWhenValuesJson calls below pass exactly one allowed value ("other") - without
    # this guard that would come out as a bare JSON string, which
    # JsonSerializer.Deserialize<List<string>> on the backend can't parse as a list at all
    # (fails closed - see SubmissionValueValidator.IsFieldVisible), silently breaking the
    # branching feature's required-ness enforcement.
    $json = $pairs | ConvertTo-Json -Compress
    if ($pairs.Count -eq 1) { $json = "[$json]" }
    return $json
}

# Full field-add helper (unlike the older scripts' Add-Field, this exposes every parameter
# AddFieldDefinitionCommand takes, including the two brand-new dynamic-options-source ones -
# named parameters throughout so call sites below stay readable despite the long list).
function Add-Field {
    param(
        [Parameter(Mandatory = $true)][string]$FormId,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$FieldType,
        [Parameter(Mandatory = $true)][bool]$IsRequired,
        [string]$OptionsJson = $null,
        [string]$LookupFormDefinitionId = $null,
        [string]$VisibleWhenFieldCode = $null,
        [string]$VisibleWhenValuesJson = $null,
        [string]$DynamicOptionsSourceFormDefinitionId = $null,
        [string]$DynamicOptionsSourceFieldCode = $null
    )
    $json = @{
        code                                 = $Code
        label                                = $Label
        fieldType                            = $FieldType
        isRequired                           = $IsRequired
        optionsJson                          = $OptionsJson
        lookupFormDefinitionId               = $LookupFormDefinitionId
        validationRulesJson                  = $null
        visibleWhenFieldCode                 = $VisibleWhenFieldCode
        visibleWhenValuesJson                = $VisibleWhenValuesJson
        filterByFieldCode                    = $null
        dynamicOptionsSourceFormDefinitionId  = $DynamicOptionsSourceFormDefinitionId
        dynamicOptionsSourceFieldCode         = $DynamicOptionsSourceFieldCode
    } | ConvertTo-Json
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$FormId/fields" $json
    Write-Host "  + $Code ($FieldType)"
    return $resp.id
}

function Publish-Form($formId, $name) {
    Invoke-RestMethod -Uri "$BaseUrl/api/forms/$formId/publish" -Method Post -Headers $headers | Out-Null
    Write-Host "Published '$name'"
}

# --- Step 0: locate the "projects" form (Lookup target for both forms' Project field) ------

Write-Host "`n--- Step 0: locating 'projects' ---"
$allForms = Invoke-RestMethod -Uri "$BaseUrl/api/forms" -Headers $headers
$projectsForm = $allForms | Where-Object { $_.code -eq "projects" }
if (-not $projectsForm) {
    throw "Could not find a form with code 'projects' - run rebuild-to-projects-centric-model.ps1 first."
}
$projectsId = $projectsForm.id
Write-Host "Found 'projects' -> $projectsId"

# --- Step 1: Stock Inflow ------------------------------------------------------------------

Write-Host "`n--- Step 1: creating 'Stock Inflow' ---"
$inflowId = New-Form "stock-inflow" "وارد المخزون" "StockManagement" "Receiving/buying stock and storing it"

Add-Field -FormId $inflowId -Code "date" -Label "التاريخ" -FieldType "DateTime" -IsRequired $true | Out-Null
Add-Field -FormId $inflowId -Code "project" -Label "المشروع" -FieldType "Lookup" -IsRequired $true `
    -LookupFormDefinitionId $projectsId | Out-Null

$materialOptionsJson = Options @(
    @{ value = "rebar"; label = "حديد تسليح" }, @{ value = "cement"; label = "أسمنت" },
    @{ value = "blocks"; label = "طوب" }, @{ value = "ready_mix"; label = "خرسانة جاهزة" },
    @{ value = "sand"; label = "رمل" }, @{ value = "gravel"; label = "ركام" },
    @{ value = "wood"; label = "خشب" }, @{ value = "pipes"; label = "أنابيب" },
    @{ value = "other"; label = "أخرى" }
)
Add-Field -FormId $inflowId -Code "material" -Label "المادة" -FieldType "Dropdown" -IsRequired $true `
    -OptionsJson $materialOptionsJson | Out-Null
# Branching: only shown (and only required) when material = "other" - same mechanism as
# Daily Progress Report's Phase-driven fields, see FormRenderer.isFieldVisible /
# SubmissionValueValidator.IsFieldVisible.
Add-Field -FormId $inflowId -Code "material_other" -Label "مادة أخرى (حدد)" -FieldType "ShortText" -IsRequired $true `
    -VisibleWhenFieldCode "material" -VisibleWhenValuesJson (Options @("other")) | Out-Null

Add-Field -FormId $inflowId -Code "quantity" -Label "الكمية" -FieldType "Decimal" -IsRequired $true | Out-Null

$unitOptionsJson = Options @(
    @{ value = "bag"; label = "كيس" }, @{ value = "ton"; label = "طن" },
    @{ value = "kg"; label = "كجم" }, @{ value = "cubic_meter"; label = "م³" },
    @{ value = "piece"; label = "قطعة" }, @{ value = "roll"; label = "لفة" },
    @{ value = "box"; label = "صندوق" }, @{ value = "liter"; label = "لتر" }
)
Add-Field -FormId $inflowId -Code "unit" -Label "الوحدة" -FieldType "Dropdown" -IsRequired $true `
    -OptionsJson $unitOptionsJson | Out-Null

# Free text, deliberately NOT a Lookup - Aamer confirmed this should just be whatever's typed
# here, with Outflow reading the live distinct values back dynamically (step 2 below).
Add-Field -FormId $inflowId -Code "storage_location" -Label "موقع التخزين" -FieldType "ShortText" -IsRequired $true | Out-Null

Add-Field -FormId $inflowId -Code "attachment" -Label "صورة الفاتورة" -FieldType "Attachment" -IsRequired $true | Out-Null
Add-Field -FormId $inflowId -Code "notes" -Label "ملاحظات" -FieldType "LongText" -IsRequired $false | Out-Null

Publish-Form $inflowId "وارد المخزون"

# --- Step 2: Stock Outflow (material/unit/issued_from sourced live from Inflow) -----------

Write-Host "`n--- Step 2: creating 'Stock Outflow' ---"
$outflowId = New-Form "stock-outflow" "صادر المخزون" "StockManagement" "Issuing stock back out"

Add-Field -FormId $outflowId -Code "date" -Label "التاريخ" -FieldType "DateTime" -IsRequired $true | Out-Null
Add-Field -FormId $outflowId -Code "project" -Label "المشروع" -FieldType "Lookup" -IsRequired $true `
    -LookupFormDefinitionId $projectsId | Out-Null

# Dynamic dropdowns: no OptionsJson at all - the option list is computed live from Inflow's
# matching field (distinct, non-blank submitted values). This is the new capability; the
# backend cross-checks at Add-Field time that "stock-inflow" actually has an active field
# with each of these codes, which is exactly why Inflow had to be published first.
Add-Field -FormId $outflowId -Code "material" -Label "المادة" -FieldType "Dropdown" -IsRequired $true `
    -DynamicOptionsSourceFormDefinitionId $inflowId -DynamicOptionsSourceFieldCode "material" | Out-Null
Add-Field -FormId $outflowId -Code "quantity" -Label "الكمية" -FieldType "Decimal" -IsRequired $true | Out-Null
Add-Field -FormId $outflowId -Code "unit" -Label "الوحدة" -FieldType "Dropdown" -IsRequired $true `
    -DynamicOptionsSourceFormDefinitionId $inflowId -DynamicOptionsSourceFieldCode "unit" | Out-Null
Add-Field -FormId $outflowId -Code "issued_from" -Label "المصدر (موقع التخزين)" -FieldType "Dropdown" -IsRequired $true `
    -DynamicOptionsSourceFormDefinitionId $inflowId -DynamicOptionsSourceFieldCode "storage_location" | Out-Null

$reasonOptionsJson = Options @(
    @{ value = "used_in_construction"; label = "استخدام في الموقع" },
    @{ value = "transferred"; label = "نقل لموقع آخر" },
    @{ value = "returned_to_supplier"; label = "إعادة للمورد" },
    @{ value = "damaged_or_wasted"; label = "تالف أو هالك" },
    @{ value = "other"; label = "أخرى" }
)
Add-Field -FormId $outflowId -Code "reason" -Label "السبب" -FieldType "Dropdown" -IsRequired $true `
    -OptionsJson $reasonOptionsJson | Out-Null
Add-Field -FormId $outflowId -Code "reason_other" -Label "سبب آخر (حدد)" -FieldType "ShortText" -IsRequired $true `
    -VisibleWhenFieldCode "reason" -VisibleWhenValuesJson (Options @("other")) | Out-Null

Publish-Form $outflowId "صادر المخزون"

Write-Host "`nDone."
Write-Host "Inflow  -> $inflowId (stock-inflow)"
Write-Host "Outflow -> $outflowId (stock-outflow)"
Write-Host "Submit a real Inflow row (material/unit/storage location), then open Outflow -" `
    "its Material/Unit/Issued-from dropdowns should immediately offer exactly what you just typed."
