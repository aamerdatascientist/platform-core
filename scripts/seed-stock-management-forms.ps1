# Seeds all Phase 1 Stock Management forms via the API, in dependency order
# (Materials and Locations first, since the transactional forms Lookup into them),
# then submits realistic sample data rows into every form, again in dependency order
# (master data submitted before the transactional forms that Lookup into it).
#
# Field/dropdown-option LABELS are Arabic (real Saudi construction-site terminology).
# Every form Code, field Code, and dropdown option value is unchanged English/ASCII -
# those become real SQL identifiers/values via DynamicSchemaService and must never be
# translated (an Arabic Code previously caused a real validation crash during the RTL
# work - see CLAUDE.md).
#
# Prerequisites:
#   1. The API is running and reachable at $baseUrl.
#   2. A Role exists in the database (see the SQL snippet in the README/chat - there's
#      no seed data yet, so the very first user registration needs a real RoleId).
#   3. You've registered a user against that role and logged in to get a JWT.
#   4. Program.cs has the JsonStringEnumConverter fix - otherwise send integers
#      (ShortText=0, LongText=1, Number=2, Decimal=3, Boolean=4, DateTime=5,
#      Dropdown=6, Lookup=7, Attachment=8) instead of the string names used below.
#   5. If running under Windows PowerShell 5.1 (not PowerShell 7+), save/open this file
#      as UTF-8 explicitly - older PowerShell doesn't always default to UTF-8, which can
#      mangle the Arabic text on read. PowerShell 7+ handles this correctly by default.
#
# Usage:
#   .\seed-stock-management-forms.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..."

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Invoke-JsonPost($uri, $json) {
    # Invoke-RestMethod's -Body, given a plain [string], does NOT reliably send it as
    # UTF-8 - Windows PowerShell 5.1 encodes a string body using the system's default
    # (non-UTF-8) codepage, silently replacing every Arabic character with '?' at the
    # point the request is sent, even though the script's own source parses correctly
    # (a separate, already-fixed issue - see the UTF-8 BOM note above). Confirmed as the
    # actual root cause: the seed data landed as literal '?' characters in Postgres
    # itself, not corrupted by anything downstream. Every JSON POST in this script goes
    # through here so the fix - explicit UTF-8 bytes, explicit charset - only needs to
    # exist in one place.
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
    # $pairs is a list of @{ value = "x"; label = "X" } hashtables - value is the stored
    # Code-like identifier (unchanged), label is what the user sees (Arabic). This isn't
    # an HTTP body itself - it becomes the optionsJson field VALUE, sent through
    # Invoke-JsonPost by whichever Add-Field call uses it - so no separate encoding fix
    # needed here.
    return ($pairs | ConvertTo-Json -Compress)
}

function Submit-Data($formId, $values) {
    # $values keys are field Codes (unchanged English) - only the data itself is Arabic
    # where relevant (descriptions, names, notes). Returns the new record's Id (a real
    # GUID from the API response), so later forms can Lookup into it for real instead
    # of a fabricated one.
    $json = $values | ConvertTo-Json -Depth 5
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$formId/submissions" $json
    return $resp.id
}

# --- Master data (forms) --------------------------------------------------

$materialsId = New-Form "materials" "المواد" "StockManagement" "Material master data"
Add-Field $materialsId "item_code" "رمز الصنف" "ShortText" $true
Add-Field $materialsId "description" "الوصف" "ShortText" $true
Add-Field $materialsId "category" "الفئة" "Dropdown" $true (Options @(
    @{ value = "cement_concrete"; label = "الخرسانة والإسمنت" },
    @{ value = "steel_rebar"; label = "حديد التسليح" },
    @{ value = "aggregates"; label = "الركام" },
    @{ value = "timber"; label = "الأخشاب" },
    @{ value = "electrical"; label = "الكهرباء" },
    @{ value = "plumbing"; label = "السباكة" },
    @{ value = "finishing"; label = "التشطيبات" },
    @{ value = "tools_equipment"; label = "الأدوات والمعدات" },
    @{ value = "safety_ppe"; label = "معدات السلامة الشخصية" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $materialsId "unit_of_measure" "وحدة القياس" "Dropdown" $true (Options @(
    @{ value = "kg"; label = "كجم" }, @{ value = "ton"; label = "طن" },
    @{ value = "m3"; label = "م3" }, @{ value = "m2"; label = "م2" },
    @{ value = "lm"; label = "متر طولي" }, @{ value = "l"; label = "لتر" },
    @{ value = "piece"; label = "قطعة" }, @{ value = "bag"; label = "كيس" },
    @{ value = "roll"; label = "لفة" }, @{ value = "box"; label = "صندوق" }
))
Add-Field $materialsId "notes" "ملاحظات" "LongText" $false
Publish-Form $materialsId "المواد"

$locationsId = New-Form "locations" "المواقع" "StockManagement" "Warehouse and site store master data"
Add-Field $locationsId "location_name" "اسم الموقع" "ShortText" $true
Add-Field $locationsId "location_type" "نوع الموقع" "Dropdown" $true (Options @(
    @{ value = "central_warehouse"; label = "المستودع المركزي" },
    @{ value = "site_store"; label = "مستودع الموقع" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $locationsId "address" "العنوان" "LongText" $false
Add-Field $locationsId "project_reference" "رقم المشروع المرجعي" "ShortText" $false
Publish-Form $locationsId "المواقع"

# --- Master data (sample rows) --------------------------------------------

Write-Host ""
Write-Host "Submitting sample materials..."
$matSteel12    = Submit-Data $materialsId @{ item_code = "STL-RBR-12"; description = "حديد تسليح قطر 12 مم"; category = "steel_rebar"; unit_of_measure = "ton"; notes = "" }
$matSteel16    = Submit-Data $materialsId @{ item_code = "STL-RBR-16"; description = "حديد تسليح قطر 16 مم"; category = "steel_rebar"; unit_of_measure = "ton"; notes = "" }
$matCement     = Submit-Data $materialsId @{ item_code = "CEM-OPC-50"; description = "إسمنت بورتلاندي عادي - كيس 50 كجم"; category = "cement_concrete"; unit_of_measure = "bag"; notes = "يُحفظ في مكان جاف بعيداً عن الرطوبة" }
$matConcreteMix = Submit-Data $materialsId @{ item_code = "RMX-C30"; description = "خرسانة جاهزة درجة C30"; category = "cement_concrete"; unit_of_measure = "m3"; notes = "" }
$matSand       = Submit-Data $materialsId @{ item_code = "AGG-SAND"; description = "رمل ناعم"; category = "aggregates"; unit_of_measure = "m3"; notes = "" }
$matGravel     = Submit-Data $materialsId @{ item_code = "AGG-GRAVEL"; description = "بحص (زلط) خشن"; category = "aggregates"; unit_of_measure = "m3"; notes = "" }
$matPlywood    = Submit-Data $materialsId @{ item_code = "TMB-PLY-18"; description = "ألواح خشب أبلكاش سماكة 18 مم"; category = "timber"; unit_of_measure = "piece"; notes = "" }
$matCable      = Submit-Data $materialsId @{ item_code = "ELE-CABLE-2.5"; description = "كابل كهربائي نحاس مقطع 2.5 مم"; category = "electrical"; unit_of_measure = "roll"; notes = "" }
$matConduit    = Submit-Data $materialsId @{ item_code = "ELE-CONDUIT-20"; description = "مواسير كهرباء PVC قطر 20 مم"; category = "electrical"; unit_of_measure = "piece"; notes = "" }
$matPvcPipe    = Submit-Data $materialsId @{ item_code = "PLM-PIPE-PVC4"; description = "مواسير صرف PVC قطر 4 بوصة"; category = "plumbing"; unit_of_measure = "lm"; notes = "" }
$matTile       = Submit-Data $materialsId @{ item_code = "FIN-TILE-CER"; description = "بلاط سيراميك للأرضيات"; category = "finishing"; unit_of_measure = "m2"; notes = "" }
$matPaint      = Submit-Data $materialsId @{ item_code = "FIN-PAINT-EM"; description = "دهان إيمولشن داخلي"; category = "finishing"; unit_of_measure = "l"; notes = "" }
$matDrill      = Submit-Data $materialsId @{ item_code = "TL-DRILL-01"; description = "مثقاب كهربائي"; category = "tools_equipment"; unit_of_measure = "piece"; notes = "" }
$matHelmet     = Submit-Data $materialsId @{ item_code = "PPE-HELMET"; description = "خوذة سلامة"; category = "safety_ppe"; unit_of_measure = "piece"; notes = "" }
Write-Host "  14 materials submitted."

Write-Host "Submitting sample locations..."
$locCentral = Submit-Data $locationsId @{ location_name = "المستودع المركزي - الرياض"; location_type = "central_warehouse"; address = "الرياض، المنطقة الصناعية الثانية"; project_reference = "" }
$locSite1   = Submit-Data $locationsId @{ location_name = "مستودع الموقع - مشروع أبراج الواحة"; location_type = "site_store"; address = "طريق الملك فهد، الرياض"; project_reference = "PRJ-2026-014" }
$locSite2   = Submit-Data $locationsId @{ location_name = "مستودع الموقع - مشروع مجمع النخيل التجاري"; location_type = "site_store"; address = "طريق الأمير محمد بن سلمان، الرياض"; project_reference = "PRJ-2026-021" }
Write-Host "  3 locations submitted."

# --- Transactional forms (forms) ------------------------------------------

$goodsReceiptId = New-Form "goods-receipt" "استلام البضائع" "StockManagement" "Materials arriving from a supplier"
Add-Field $goodsReceiptId "grn_reference" "رقم إذن الاستلام" "ShortText" $true
Add-Field $goodsReceiptId "supplier_name" "اسم المورد" "ShortText" $true
Add-Field $goodsReceiptId "po_reference" "رقم أمر الشراء" "ShortText" $false
Add-Field $goodsReceiptId "material" "المادة" "Lookup" $true $null $materialsId
Add-Field $goodsReceiptId "quantity_received" "الكمية المستلمة" "Decimal" $true
Add-Field $goodsReceiptId "location" "الموقع" "Lookup" $true $null $locationsId
Add-Field $goodsReceiptId "delivery_note_number" "رقم مذكرة التسليم" "ShortText" $false
Add-Field $goodsReceiptId "received_date" "تاريخ الاستلام" "DateTime" $true
Add-Field $goodsReceiptId "condition" "الحالة" "Dropdown" $true (Options @(
    @{ value = "good"; label = "جيدة" }, @{ value = "damaged"; label = "تالفة" },
    @{ value = "partial"; label = "جزئية/ناقصة" }
))
Add-Field $goodsReceiptId "delivery_note_photo" "صورة مذكرة التسليم" "Attachment" $false
Add-Field $goodsReceiptId "remarks" "ملاحظات" "LongText" $false
Publish-Form $goodsReceiptId "استلام البضائع"

$materialIssueId = New-Form "material-issue" "صرف المواد" "StockManagement" "Materials going out to a project or crew"
Add-Field $materialIssueId "issue_reference" "رقم إذن الصرف" "ShortText" $true
Add-Field $materialIssueId "material" "المادة" "Lookup" $true $null $materialsId
Add-Field $materialIssueId "quantity_issued" "الكمية المصروفة" "Decimal" $true
Add-Field $materialIssueId "from_location" "من موقع" "Lookup" $true $null $locationsId
Add-Field $materialIssueId "issued_to" "المستلم" "ShortText" $true
Add-Field $materialIssueId "project_work_order" "المشروع / أمر العمل" "ShortText" $false
Add-Field $materialIssueId "requested_by" "طلب بواسطة" "ShortText" $true
Add-Field $materialIssueId "issued_date" "تاريخ الصرف" "DateTime" $true
Add-Field $materialIssueId "purpose" "الغرض" "LongText" $false
Publish-Form $materialIssueId "صرف المواد"

$stockTransferId = New-Form "stock-transfer" "تحويل المخزون" "StockManagement" "Materials moving between locations"
Add-Field $stockTransferId "transfer_reference" "رقم إذن التحويل" "ShortText" $true
Add-Field $stockTransferId "material" "المادة" "Lookup" $true $null $materialsId
Add-Field $stockTransferId "quantity" "الكمية" "Decimal" $true
Add-Field $stockTransferId "from_location" "من موقع" "Lookup" $true $null $locationsId
Add-Field $stockTransferId "to_location" "إلى موقع" "Lookup" $true $null $locationsId
Add-Field $stockTransferId "transferred_by" "تم التحويل بواسطة" "ShortText" $true
Add-Field $stockTransferId "transfer_date" "تاريخ التحويل" "DateTime" $true
Add-Field $stockTransferId "reason" "السبب" "LongText" $false
Publish-Form $stockTransferId "تحويل المخزون"

$stockAdjustmentId = New-Form "stock-adjustment" "تسوية المخزون" "StockManagement" "Corrections for damage, loss, or count discrepancies"
Add-Field $stockAdjustmentId "material" "المادة" "Lookup" $true $null $materialsId
Add-Field $stockAdjustmentId "location" "الموقع" "Lookup" $true $null $locationsId
Add-Field $stockAdjustmentId "quantity_adjusted" "الكمية المعدَّلة" "Decimal" $true
Add-Field $stockAdjustmentId "reason" "السبب" "Dropdown" $true (Options @(
    @{ value = "damage"; label = "تلف" }, @{ value = "loss_theft"; label = "فقدان/سرقة" },
    @{ value = "count_correction"; label = "تصحيح جرد" }, @{ value = "expiry"; label = "انتهاء الصلاحية" },
    @{ value = "other"; label = "أخرى" }
))
Add-Field $stockAdjustmentId "adjusted_by" "تمت التسوية بواسطة" "ShortText" $true
Add-Field $stockAdjustmentId "adjustment_date" "تاريخ التسوية" "DateTime" $true
Add-Field $stockAdjustmentId "supporting_evidence" "المستندات المؤيدة" "Attachment" $false
Add-Field $stockAdjustmentId "remarks" "ملاحظات" "LongText" $false
Publish-Form $stockAdjustmentId "تسوية المخزون"

$physicalCountId = New-Form "physical-stock-count" "الجرد الفعلي للمخزون" "StockManagement" "Reconciliation of system quantity vs. actual"
Add-Field $physicalCountId "material" "المادة" "Lookup" $true $null $materialsId
Add-Field $physicalCountId "location" "الموقع" "Lookup" $true $null $locationsId
Add-Field $physicalCountId "system_quantity" "الكمية بالنظام" "Decimal" $true
Add-Field $physicalCountId "counted_quantity" "الكمية المجرودة" "Decimal" $true
Add-Field $physicalCountId "counted_by" "تم الجرد بواسطة" "ShortText" $true
Add-Field $physicalCountId "count_date" "تاريخ الجرد" "DateTime" $true
Add-Field $physicalCountId "remarks" "ملاحظات" "LongText" $false
Publish-Form $physicalCountId "الجرد الفعلي للمخزون"

# --- Transactional forms (sample rows) ------------------------------------

Write-Host ""
Write-Host "Submitting sample goods receipts..."
Submit-Data $goodsReceiptId @{
    grn_reference = "GRN-2026-0501"; supplier_name = "شركة الراجحي للحديد والصلب"; po_reference = "PO-2026-0117"
    material = $matSteel12; quantity_received = 25; location = $locCentral
    delivery_note_number = "DN-88231"; received_date = "2026-08-10"; condition = "good"
    remarks = "تم الفحص والاستلام بدون ملاحظات"
} | Out-Null
Submit-Data $goodsReceiptId @{
    grn_reference = "GRN-2026-0502"; supplier_name = "مصنع اليمامة للإسمنت"; po_reference = "PO-2026-0122"
    material = $matCement; quantity_received = 500; location = $locCentral
    delivery_note_number = "DN-88304"; received_date = "2026-08-12"; condition = "good"
    remarks = ""
} | Out-Null
Submit-Data $goodsReceiptId @{
    grn_reference = "GRN-2026-0503"; supplier_name = "مؤسسة النقل السريع للركام"; po_reference = "PO-2026-0130"
    material = $matSand; quantity_received = 40; location = $locSite2
    delivery_note_number = "DN-88350"; received_date = "2026-08-14"; condition = "partial"
    remarks = "نقص طفيف في الكمية عن الفاتورة"
} | Out-Null
Write-Host "  3 goods receipts submitted."

Write-Host "Submitting sample material issues..."
Submit-Data $materialIssueId @{
    issue_reference = "ISS-2026-0301"; material = $matSteel12; quantity_issued = 10; from_location = $locCentral
    issued_to = "فريق حدادة التسليح"; project_work_order = "PRJ-2026-014"; requested_by = "محمد العتيبي"
    issued_date = "2026-08-15"; purpose = "تسليح أعمدة الطابق الثالث"
} | Out-Null
Submit-Data $materialIssueId @{
    issue_reference = "ISS-2026-0302"; material = $matCement; quantity_issued = 150; from_location = $locCentral
    issued_to = "طاقم الصب"; project_work_order = "PRJ-2026-014"; requested_by = "سعود القحطاني"
    issued_date = "2026-08-16"; purpose = "صب سقف الطابق الثاني"
} | Out-Null
Submit-Data $materialIssueId @{
    issue_reference = "ISS-2026-0303"; material = $matGravel; quantity_issued = 20; from_location = $locSite2
    issued_to = "فريق الخرسانة"; project_work_order = "PRJ-2026-021"; requested_by = "فهد الدوسري"
    issued_date = "2026-08-17"; purpose = "أعمال صب أساسات المبنى الإداري"
} | Out-Null
Write-Host "  3 material issues submitted."

Write-Host "Submitting sample stock transfers..."
Submit-Data $stockTransferId @{
    transfer_reference = "TRF-2026-0101"; material = $matPlywood; quantity = 30
    from_location = $locCentral; to_location = $locSite2
    transferred_by = "عبدالله المطيري"; transfer_date = "2026-08-11"; reason = "نقل مخزون لدعم أعمال الموقع"
} | Out-Null
Submit-Data $stockTransferId @{
    transfer_reference = "TRF-2026-0102"; material = $matCable; quantity = 15
    from_location = $locCentral; to_location = $locSite1
    transferred_by = "خالد الشهري"; transfer_date = "2026-08-13"; reason = "تجهيز أعمال الكهرباء بالموقع"
} | Out-Null
Write-Host "  2 stock transfers submitted."

Write-Host "Submitting sample stock adjustments..."
Submit-Data $stockAdjustmentId @{
    material = $matSand; location = $locSite2; quantity_adjusted = -2; reason = "count_correction"
    adjusted_by = "ياسر الحربي"; adjustment_date = "2026-08-18"; remarks = "فرق في الجرد الشهري"
} | Out-Null
Submit-Data $stockAdjustmentId @{
    material = $matCement; location = $locCentral; quantity_adjusted = -5; reason = "damage"
    adjusted_by = "ناصر العنزي"; adjustment_date = "2026-08-19"; remarks = "أكياس تالفة بسبب الرطوبة"
} | Out-Null
Write-Host "  2 stock adjustments submitted."

Write-Host "Submitting sample physical stock counts..."
Submit-Data $physicalCountId @{
    material = $matSteel16; location = $locCentral; system_quantity = 80; counted_quantity = 78
    counted_by = "بندر آل سعود"; count_date = "2026-08-20"; remarks = "جرد دوري شهري"
} | Out-Null
Submit-Data $physicalCountId @{
    material = $matCement; location = $locCentral; system_quantity = 345; counted_quantity = 345
    counted_by = "تركي الغامدي"; count_date = "2026-08-20"; remarks = ""
} | Out-Null
Write-Host "  2 physical stock counts submitted."

Write-Host ""
Write-Host "All 7 Stock Management forms created, published, and seeded with sample data."
