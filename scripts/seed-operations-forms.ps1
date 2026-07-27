# Seeds all Phase 2 Operations forms via the API, in dependency order
# (Projects, Equipment, Trades first, then the four transactional forms).
#
# Prerequisites: same as seed-stock-management-forms.ps1 - a running API, a JWT, and
# the JsonStringEnumConverter fix already in Program.cs.
#
# Optional: pass -LocationsFormId if you have it (from the Stock Management seed run)
# to wire Projects.location as a real Lookup to Locations. If you don't have it handy,
# leave it out - the script skips that one field with a warning rather than failing.
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

# --- Master data ---------------------------------------------------------

$projectsId = New-Form "projects" "Projects" "Operations" "Project master data"
Add-Field $projectsId "project_code" "Project code" "ShortText" $true
Add-Field $projectsId "project_name" "Project name" "ShortText" $true
Add-Field $projectsId "status" "Status" "Dropdown" $true (Options @(
    @{ value = "planning"; label = "Planning" }, @{ value = "active"; label = "Active" },
    @{ value = "on_hold"; label = "On hold" }, @{ value = "completed"; label = "Completed" }
))
if ($LocationsFormId) {
    Add-Field $projectsId "location" "Location" "Lookup" $false $null $LocationsFormId
} else {
    Write-Host "WARNING: -LocationsFormId not supplied - skipping Projects.location field. Add it manually later if needed."
}
Add-Field $projectsId "start_date" "Start date" "DateTime" $false
Add-Field $projectsId "expected_completion" "Expected completion" "DateTime" $false
Publish-Form $projectsId "Projects"

$equipmentId = New-Form "equipment" "Equipment" "Operations" "Equipment and plant master data"
Add-Field $equipmentId "equipment_code" "Equipment code" "ShortText" $true
Add-Field $equipmentId "equipment_name" "Equipment name" "ShortText" $true
Add-Field $equipmentId "equipment_type" "Equipment type" "Dropdown" $true (Options @(
    @{ value = "heavy_machinery"; label = "Heavy machinery" }, @{ value = "power_tools"; label = "Power tools" },
    @{ value = "vehicles"; label = "Vehicles" }, @{ value = "generators"; label = "Generators" },
    @{ value = "other"; label = "Other" }
))
Add-Field $equipmentId "status" "Status" "Dropdown" $true (Options @(
    @{ value = "available"; label = "Available" }, @{ value = "in_use"; label = "In use" },
    @{ value = "under_maintenance"; label = "Under maintenance" }, @{ value = "out_of_service"; label = "Out of service" }
))
Publish-Form $equipmentId "Equipment"

$tradesId = New-Form "trades" "Trades" "Operations" "Labour trade categories"
Add-Field $tradesId "trade_name" "Trade name" "ShortText" $true
Publish-Form $tradesId "Trades"

# --- Transactional forms ---------------------------------------------------

$dailyReportId = New-Form "daily-site-report" "Daily site report" "Operations" "End-of-day site summary"
Add-Field $dailyReportId "report_date" "Report date" "DateTime" $true
Add-Field $dailyReportId "project" "Project" "Lookup" $true $null $projectsId
Add-Field $dailyReportId "weather" "Weather" "Dropdown" $true (Options @(
    @{ value = "clear"; label = "Clear" }, @{ value = "cloudy"; label = "Cloudy" },
    @{ value = "rain"; label = "Rain" }, @{ value = "extreme_heat"; label = "Extreme heat" },
    @{ value = "sandstorm"; label = "Sandstorm" }, @{ value = "other"; label = "Other" }
))
Add-Field $dailyReportId "crew_count" "Crew count" "Number" $true
Add-Field $dailyReportId "work_summary" "Work summary" "LongText" $true
Add-Field $dailyReportId "issues_delays" "Issues / delays" "LongText" $false
Add-Field $dailyReportId "site_photo" "Site photo" "Attachment" $false
Add-Field $dailyReportId "submitted_by" "Submitted by" "ShortText" $true
Publish-Form $dailyReportId "Daily site report"

$equipmentLogId = New-Form "equipment-log" "Equipment log" "Operations" "Daily equipment usage"
Add-Field $equipmentLogId "log_date" "Log date" "DateTime" $true
Add-Field $equipmentLogId "equipment" "Equipment" "Lookup" $true $null $equipmentId
Add-Field $equipmentLogId "project" "Project" "Lookup" $true $null $projectsId
Add-Field $equipmentLogId "operator" "Operator" "ShortText" $true
Add-Field $equipmentLogId "hours_used" "Hours used" "Decimal" $true
Add-Field $equipmentLogId "fuel_consumed" "Fuel consumed" "Decimal" $false
Add-Field $equipmentLogId "status_at_end_of_day" "Status at end of day" "Dropdown" $true (Options @(
    @{ value = "operational"; label = "Operational" }, @{ value = "needs_maintenance"; label = "Needs maintenance" },
    @{ value = "broken_down"; label = "Broken down" }
))
Add-Field $equipmentLogId "maintenance_notes" "Maintenance notes" "LongText" $false
Publish-Form $equipmentLogId "Equipment log"

$laborLogId = New-Form "labor-log" "Labor log" "Operations" "Daily headcount and hours by trade"
Add-Field $laborLogId "log_date" "Log date" "DateTime" $true
Add-Field $laborLogId "project" "Project" "Lookup" $true $null $projectsId
Add-Field $laborLogId "trade" "Trade" "Lookup" $true $null $tradesId
Add-Field $laborLogId "headcount" "Headcount" "Number" $true
Add-Field $laborLogId "hours_worked" "Hours worked" "Decimal" $true
Add-Field $laborLogId "supervisor" "Supervisor" "ShortText" $true
Add-Field $laborLogId "notes" "Notes" "LongText" $false
Publish-Form $laborLogId "Labor log"

$taskTrackingId = New-Form "task-tracking" "Task tracking" "Operations" "Work items assigned against a project"
Add-Field $taskTrackingId "task_reference" "Task reference" "ShortText" $true
Add-Field $taskTrackingId "project" "Project" "Lookup" $true $null $projectsId
Add-Field $taskTrackingId "task_description" "Task description" "LongText" $true
Add-Field $taskTrackingId "assigned_to" "Assigned to" "ShortText" $true
Add-Field $taskTrackingId "priority" "Priority" "Dropdown" $true (Options @(
    @{ value = "low"; label = "Low" }, @{ value = "medium"; label = "Medium" },
    @{ value = "high"; label = "High" }, @{ value = "urgent"; label = "Urgent" }
))
Add-Field $taskTrackingId "status" "Status" "Dropdown" $true (Options @(
    @{ value = "not_started"; label = "Not started" }, @{ value = "in_progress"; label = "In progress" },
    @{ value = "blocked"; label = "Blocked" }, @{ value = "completed"; label = "Completed" }
))
Add-Field $taskTrackingId "due_date" "Due date" "DateTime" $false
Add-Field $taskTrackingId "completed_date" "Completed date" "DateTime" $false
Publish-Form $taskTrackingId "Task tracking"

Write-Host ""
Write-Host "All Operations forms created and published."
