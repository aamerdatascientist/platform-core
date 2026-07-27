# Seeds all Phase 1 Stock Management forms via the API, in dependency order
# (Materials and Locations first, since the transactional forms Lookup into them).
#
# Prerequisites:
#   1. The API is running and reachable at $baseUrl.
#   2. A Role exists in the database (see the SQL snippet in the README/chat - there's
#      no seed data yet, so the very first user registration needs a real RoleId).
#   3. You've registered a user against that role and logged in to get a JWT.
#   4. Program.cs has the JsonStringEnumConverter fix - otherwise send integers
#      (ShortText=0, LongText=1, Number=2, Decimal=3, Boolean=4, DateTime=5,
#      Dropdown=6, Lookup=7, Attachment=8) instead of the string names used below.
#
# Usage:
#   .\seed-stock-management-forms.ps1 -BaseUrl "http://localhost:5080" -Token "eyJhbGc..."

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token
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
    # $pairs is a list of @{ value = "x"; label = "X" } hashtables
    return ($pairs | ConvertTo-Json -Compress)
}

# --- Master data ---------------------------------------------------------

$materialsId = New-Form "materials" "Materials" "StockManagement" "Material master data"
Add-Field $materialsId "item_code" "Item code" "ShortText" $true
Add-Field $materialsId "description" "Description" "ShortText" $true
Add-Field $materialsId "category" "Category" "Dropdown" $true (Options @(
    @{ value = "cement_concrete"; label = "Cement & concrete" },
    @{ value = "steel_rebar"; label = "Steel & rebar" },
    @{ value = "aggregates"; label = "Aggregates" },
    @{ value = "timber"; label = "Timber" },
    @{ value = "electrical"; label = "Electrical" },
    @{ value = "plumbing"; label = "Plumbing" },
    @{ value = "finishing"; label = "Finishing" },
    @{ value = "tools_equipment"; label = "Tools & equipment" },
    @{ value = "safety_ppe"; label = "Safety/PPE" },
    @{ value = "other"; label = "Other" }
))
Add-Field $materialsId "unit_of_measure" "Unit of measure" "Dropdown" $true (Options @(
    @{ value = "kg"; label = "kg" }, @{ value = "ton"; label = "Ton" },
    @{ value = "m3"; label = "m3" }, @{ value = "m2"; label = "m2" },
    @{ value = "lm"; label = "Linear metre" }, @{ value = "l"; label = "Litre" },
    @{ value = "piece"; label = "Piece" }, @{ value = "bag"; label = "Bag" },
    @{ value = "roll"; label = "Roll" }, @{ value = "box"; label = "Box" }
))
Add-Field $materialsId "notes" "Notes" "LongText" $false
Publish-Form $materialsId "Materials"

$locationsId = New-Form "locations" "Locations" "StockManagement" "Warehouse and site store master data"
Add-Field $locationsId "location_name" "Location name" "ShortText" $true
Add-Field $locationsId "location_type" "Location type" "Dropdown" $true (Options @(
    @{ value = "central_warehouse"; label = "Central warehouse" },
    @{ value = "site_store"; label = "Site store" },
    @{ value = "other"; label = "Other" }
))
Add-Field $locationsId "address" "Address" "LongText" $false
Add-Field $locationsId "project_reference" "Project reference" "ShortText" $false
Publish-Form $locationsId "Locations"

# --- Transactional forms ---------------------------------------------------

$goodsReceiptId = New-Form "goods-receipt" "Goods receipt" "StockManagement" "Materials arriving from a supplier"
Add-Field $goodsReceiptId "grn_reference" "GRN reference" "ShortText" $true
Add-Field $goodsReceiptId "supplier_name" "Supplier name" "ShortText" $true
Add-Field $goodsReceiptId "po_reference" "PO reference" "ShortText" $false
Add-Field $goodsReceiptId "material" "Material" "Lookup" $true $null $materialsId
Add-Field $goodsReceiptId "quantity_received" "Quantity received" "Decimal" $true
Add-Field $goodsReceiptId "location" "Location" "Lookup" $true $null $locationsId
Add-Field $goodsReceiptId "delivery_note_number" "Delivery note number" "ShortText" $false
Add-Field $goodsReceiptId "received_date" "Received date" "DateTime" $true
Add-Field $goodsReceiptId "condition" "Condition" "Dropdown" $true (Options @(
    @{ value = "good"; label = "Good" }, @{ value = "damaged"; label = "Damaged" },
    @{ value = "partial"; label = "Partial/short" }
))
Add-Field $goodsReceiptId "delivery_note_photo" "Delivery note photo" "Attachment" $false
Add-Field $goodsReceiptId "remarks" "Remarks" "LongText" $false
Publish-Form $goodsReceiptId "Goods receipt"

$materialIssueId = New-Form "material-issue" "Material issue" "StockManagement" "Materials going out to a project or crew"
Add-Field $materialIssueId "issue_reference" "Issue reference" "ShortText" $true
Add-Field $materialIssueId "material" "Material" "Lookup" $true $null $materialsId
Add-Field $materialIssueId "quantity_issued" "Quantity issued" "Decimal" $true
Add-Field $materialIssueId "from_location" "From location" "Lookup" $true $null $locationsId
Add-Field $materialIssueId "issued_to" "Issued to" "ShortText" $true
Add-Field $materialIssueId "project_work_order" "Project / work order" "ShortText" $false
Add-Field $materialIssueId "requested_by" "Requested by" "ShortText" $true
Add-Field $materialIssueId "issued_date" "Issued date" "DateTime" $true
Add-Field $materialIssueId "purpose" "Purpose" "LongText" $false
Publish-Form $materialIssueId "Material issue"

$stockTransferId = New-Form "stock-transfer" "Stock transfer" "StockManagement" "Materials moving between locations"
Add-Field $stockTransferId "transfer_reference" "Transfer reference" "ShortText" $true
Add-Field $stockTransferId "material" "Material" "Lookup" $true $null $materialsId
Add-Field $stockTransferId "quantity" "Quantity" "Decimal" $true
Add-Field $stockTransferId "from_location" "From location" "Lookup" $true $null $locationsId
Add-Field $stockTransferId "to_location" "To location" "Lookup" $true $null $locationsId
Add-Field $stockTransferId "transferred_by" "Transferred by" "ShortText" $true
Add-Field $stockTransferId "transfer_date" "Transfer date" "DateTime" $true
Add-Field $stockTransferId "reason" "Reason" "LongText" $false
Publish-Form $stockTransferId "Stock transfer"

$stockAdjustmentId = New-Form "stock-adjustment" "Stock adjustment" "StockManagement" "Corrections for damage, loss, or count discrepancies"
Add-Field $stockAdjustmentId "material" "Material" "Lookup" $true $null $materialsId
Add-Field $stockAdjustmentId "location" "Location" "Lookup" $true $null $locationsId
Add-Field $stockAdjustmentId "quantity_adjusted" "Quantity adjusted" "Decimal" $true
Add-Field $stockAdjustmentId "reason" "Reason" "Dropdown" $true (Options @(
    @{ value = "damage"; label = "Damage" }, @{ value = "loss_theft"; label = "Loss/theft" },
    @{ value = "count_correction"; label = "Count correction" }, @{ value = "expiry"; label = "Expiry" },
    @{ value = "other"; label = "Other" }
))
Add-Field $stockAdjustmentId "adjusted_by" "Adjusted by" "ShortText" $true
Add-Field $stockAdjustmentId "adjustment_date" "Adjustment date" "DateTime" $true
Add-Field $stockAdjustmentId "supporting_evidence" "Supporting evidence" "Attachment" $false
Add-Field $stockAdjustmentId "remarks" "Remarks" "LongText" $false
Publish-Form $stockAdjustmentId "Stock adjustment"

$physicalCountId = New-Form "physical-stock-count" "Physical stock count" "StockManagement" "Reconciliation of system quantity vs. actual"
Add-Field $physicalCountId "material" "Material" "Lookup" $true $null $materialsId
Add-Field $physicalCountId "location" "Location" "Lookup" $true $null $locationsId
Add-Field $physicalCountId "system_quantity" "System quantity" "Decimal" $true
Add-Field $physicalCountId "counted_quantity" "Counted quantity" "Decimal" $true
Add-Field $physicalCountId "counted_by" "Counted by" "ShortText" $true
Add-Field $physicalCountId "count_date" "Count date" "DateTime" $true
Add-Field $physicalCountId "remarks" "Remarks" "LongText" $false
Publish-Form $physicalCountId "Physical stock count"

Write-Host ""
Write-Host "All 7 Stock Management forms created and published."
