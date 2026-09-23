# Generates one connected month of realistic daily-report activity across all 5 Daily
# Reports forms, for a single project, and submits it through the API.
#
# Loop-driven: every row is computed from the day index and a seeded RNG, not written out
# as a literal. Re-running is safe - see "Resume" below.
#
# ---------------------------------------------------------------------------------------
# RESUME / DON'T-DUPLICATE BEHAVIOUR (read this before running)
#
# This script checks the live data BEFORE writing anything:
#   1. It finds or creates the project record, and says which it did.
#   2. For each of the 5 forms it reads the existing submissions and prints the row count
#      and which report_date values are already present.
#   3. It then generates only the dates that are MISSING. A date already submitted for a
#      form is skipped, and the skip is reported.
# So a partially-seeded database is picked up from where it actually stopped, and a fully
# seeded one results in zero writes rather than a duplicate month.
#
# Run it with -DryRun first. That performs the full pre-flight and prints every row it
# WOULD submit, without POSTing anything - worth doing at least once, because this script
# has never been executed by its author (Claude Code's sandbox has no PowerShell and no
# route to the live database; see CLAUDE.md).
#
# ---------------------------------------------------------------------------------------
# WINDOW
#
# 28 working days, Fridays excluded, ending on the last working day before 2026-09-23:
#   2026-08-22 -> 2026-09-22
# Every form's "day 1" is the first day of that window; each form then runs for its own
# number of days from there. The per-form day numbers in the spec (e.g. "permit ~day 8")
# are relative to that shared day 1.
#
# ---------------------------------------------------------------------------------------
# JUDGEMENT CALLS - all flagged rather than silently decided:
#
# * area_progress_sqm is FieldType.Number, i.e. an INTEGER column. Targets like 16.1 and
#   26.3 m²/day can't be stored per-row, so each day gets an integer drawn from the stated
#   variance band; the band's mean is what lands on the target across the month.
# * Form 3's delay causes were specified as (weather, material shortage, crew shortage),
#   but the foundation form's delay_reason dropdown has no "weather" option - its values
#   are labor_shortage / material_shortage / equipment_shortage / access / other. The
#   weather-driven delay day therefore submits "other" AND sets that day's weather to a
#   bad-weather value, so the cause is still legible. Change $Form3WeatherDelayReason if
#   you'd rather it were something else.
# * Form 4 never uses "formwork_availability" as a delay reason, because the spec fixes the
#   formwork blocker to No every day and the two together would contradict each other.
# * MEP crew sizes weren't specified (only m² targets were), so each trade gets 6-9 people.

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$ProjectsFormId,
    [string]$ProjectName = "مبنى سكني - 6 طوابق",
    [string]$ProjectCode = "PRJ-2026-RES6",
    [datetime]$WindowEnd = "2026-09-22",
    [int]$WindowDays = 28,
    [int]$Seed = 20260922,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }
$rng = [System.Random]::new($Seed)

$Form3WeatherDelayReason = "other"

# --- HTTP helpers (same UTF-8 pattern as every other seed script here) -------------------

function Invoke-JsonPost($uri, $json) {
    # Windows PowerShell 5.1 does not reliably send a string -Body as UTF-8; it mangles
    # Arabic into '?' at send time. Explicit bytes + explicit charset, always.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $bytes -ContentType "application/json; charset=utf-8"
}

function Get-Json($uri) {
    return Invoke-RestMethod -Uri $uri -Method Get -Headers $headers
}

function Submit-Row($formId, $values) {
    $json = $values | ConvertTo-Json -Depth 5
    if ($DryRun) { return $null }
    $resp = Invoke-JsonPost "$BaseUrl/api/forms/$formId/submissions" $json
    return $resp.id
}

function Get-AllSubmissions($formId) {
    $all = @()
    $page = 1
    while ($true) {
        $resp = Get-Json "$BaseUrl/api/forms/$formId/submissions?page=$page&pageSize=200"
        if ($null -eq $resp.items -or $resp.items.Count -eq 0) { break }
        $all += $resp.items
        if ($all.Count -ge $resp.totalCount) { break }
        $page++
    }
    return $all
}

function Get-ExistingDates($submissions) {
    $dates = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($row in $submissions) {
        $raw = $row.values.report_date
        if ($raw) { [void]$dates.Add(([datetime]$raw).ToString('yyyy-MM-dd')) }
    }
    return $dates
}

# --- Working-day window -----------------------------------------------------------------

function Get-WorkingDays([datetime]$endInclusive, [int]$count) {
    $days = New-Object System.Collections.ArrayList
    $cur = $endInclusive
    while ($days.Count -lt $count) {
        # DayOfWeek Friday is the single weekly non-working day for this site.
        if ($cur.DayOfWeek -ne [System.DayOfWeek]::Friday) { [void]$days.Insert(0, $cur) }
        $cur = $cur.AddDays(-1)
    }
    return $days
}

$window = Get-WorkingDays $WindowEnd $WindowDays
function DayOf([int]$n) { return $window[$n - 1].ToString('yyyy-MM-dd') }   # 1-based

Write-Host ""
Write-Host "Window: $($window[0].ToString('yyyy-MM-dd')) -> $($window[-1].ToString('yyyy-MM-dd')) ($WindowDays working days, Fridays excluded)"
if ($DryRun) { Write-Host "DRY RUN - nothing will be submitted." -ForegroundColor Yellow }
Write-Host ""

# --- Random helpers ---------------------------------------------------------------------

function RandBetween([int]$minInclusive, [int]$maxInclusive) {
    return $rng.Next($minInclusive, $maxInclusive + 1)
}

function PickWeather() {
    # Mostly clear, occasional extreme heat. Sandstorm is placed deliberately, not randomly.
    $roll = $rng.Next(0, 100)
    if ($roll -lt 72) { return "clear" }
    elseif ($roll -lt 92) { return "extreme_heat" }
    else { return "rain" }
}

# --- Pre-flight: resolve the 5 forms ----------------------------------------------------

$formCodes = [ordered]@{
    paperwork   = "paperwork_mobilization_daily"
    excavation  = "excavation_shoring_daily"
    foundation  = "foundation_progress_daily"
    structural  = "structural_progress_daily"
    mep         = "mep_progress_daily"
}

$allForms = Get-Json "$BaseUrl/api/forms"
$forms = [ordered]@{}
foreach ($key in $formCodes.Keys) {
    $code = $formCodes[$key]
    $match = $allForms | Where-Object { $_.code -eq $code } | Select-Object -First 1
    if ($null -eq $match) {
        throw "Form '$code' not found. Run seed-daily-report-forms.ps1 first - this script only adds data, it does not create forms."
    }
    if ($match.status -ne "Published") {
        throw "Form '$code' is $($match.status), not Published - submissions would be rejected. Publish it first."
    }
    $forms[$key] = $match.id
}

# --- Pre-flight: find or create the project ---------------------------------------------

Write-Host "--- Pre-flight -------------------------------------------------------------"

$projectRows = Get-AllSubmissions $ProjectsFormId
$existingProject = $projectRows | Where-Object { $_.values.project_name -eq $ProjectName } | Select-Object -First 1

if ($existingProject) {
    $projectId = $existingProject.id
    Write-Host "Project: FOUND existing '$ProjectName' -> $projectId (reusing, not creating a second one)"
} elseif ($DryRun) {
    $projectId = "00000000-0000-0000-0000-000000000000"
    Write-Host "Project: not found - would CREATE '$ProjectName' (dry run, using a placeholder id)"
} else {
    $projectId = Submit-Row $ProjectsFormId @{
        project_code        = $ProjectCode
        project_name        = $ProjectName
        status              = "active"
        start_date          = $window[0].ToString('yyyy-MM-dd')
        expected_completion = $window[-1].AddMonths(8).ToString('yyyy-MM-dd')
    }
    Write-Host "Project: CREATED '$ProjectName' -> $projectId"
}

# --- Pre-flight: existing rows per form -------------------------------------------------

$existingDates = [ordered]@{}
foreach ($key in $forms.Keys) {
    $rows = Get-AllSubmissions $forms[$key]
    $dates = Get-ExistingDates $rows
    $existingDates[$key] = $dates
    Write-Host ("  {0,-12} existing rows: {1,4}   distinct report dates: {2,4}" -f $formCodes[$key], $rows.Count, $dates.Count)
}
Write-Host ""

# Tracks what this run actually wrote, so the closing summary reflects reality rather than
# what was planned - a resumed run writes fewer rows than it generates.
$submitted = [ordered]@{}
$skipped = [ordered]@{}
foreach ($key in $forms.Keys) { $submitted[$key] = 0; $skipped[$key] = 0 }

function Send-DayRow($key, $dayNumber, $values) {
    $date = DayOf $dayNumber
    if ($existingDates[$key].Contains($date)) {
        $script:skipped[$key]++
        return
    }
    $values['report_date'] = $date
    $values['project'] = $projectId
    Submit-Row $forms[$key] $values | Out-Null
    $script:submitted[$key]++
    if ($DryRun) {
        $shown = ($values.GetEnumerator() | Where-Object { $_.Key -ne 'project' } |
                  ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '
        Write-Host "    [$($formCodes[$key])] $shown"
    }
}

# --- Form 1: Paperwork & mobilisation, days 1-26 ----------------------------------------
# Each checklist item flips to Yes on exactly the day it closes, No on every other day.

Write-Host "Form 1 - paperwork_mobilization_daily (26 days)..."
$milestones = @{
    closed_survey_soil_report       = 3
    closed_permit_request           = 8
    closed_subcontractor_agreements = 10
    closed_material_preorder        = 12
    closed_municipal_approval       = 15
    closed_site_mobilization        = 20
    closed_other                    = 22
}

for ($d = 1; $d -le 26; $d++) {
    $pending = ($d -le 14)
    $row = [ordered]@{
        has_pending_item  = $pending
        pending_authority = $(if ($pending) { "البلدية" } else { $null })
    }
    foreach ($field in $milestones.Keys) { $row[$field] = ($milestones[$field] -eq $d) }
    Send-DayRow 'paperwork' $d $row
}

# --- Form 2: Excavation & shoring, days 1-18 --------------------------------------------
# Two delay days: one sandstorm, one equipment failure, both with sharply reduced output.

Write-Host "Form 2 - excavation_shoring_daily (18 days)..."
$excavationSandstormDay = 6
$excavationEquipmentDay = 13

for ($d = 1; $d -le 18; $d++) {
    $isSandstorm = ($d -eq $excavationSandstormDay)
    $isEquipment = ($d -eq $excavationEquipmentDay)
    $hadDelay = $isSandstorm -or $isEquipment

    $weather = if ($isSandstorm) { "sandstorm" } else { PickWeather }
    $reason = if ($isSandstorm) { "weather" } elseif ($isEquipment) { "equipment" } else { $null }
    $area = if ($hadDelay) { RandBetween 5 10 } else { RandBetween 20 30 }

    Send-DayRow 'excavation' $d ([ordered]@{
        weather           = $weather
        crew_count        = RandBetween 4 6
        area_progress_sqm = $area
        had_delay         = $hadDelay
        delay_reason      = $reason
    })
}

# --- Form 3: Foundations, days 1-28 -----------------------------------------------------
# Three delay days: weather-driven, material shortage, crew shortage.

Write-Host "Form 3 - foundation_progress_daily (28 days)..."
$foundationDelays = @{
    7  = $Form3WeatherDelayReason
    16 = "material_shortage"
    23 = "labor_shortage"
}

for ($d = 1; $d -le 28; $d++) {
    $hadDelay = $foundationDelays.ContainsKey($d)
    $reason = if ($hadDelay) { $foundationDelays[$d] } else { $null }

    # The weather-caused delay day also gets bad weather, so the "other" reason above still
    # reads as weather-driven to anyone looking at the row.
    $weather = if ($d -eq 7) { "rain" } else { PickWeather }
    $area = if ($hadDelay) { RandBetween 5 11 } else { RandBetween 12 20 }

    Send-DayRow 'foundation' $d ([ordered]@{
        weather           = $weather
        crew_count        = RandBetween 16 20
        area_progress_sqm = $area
        had_delay         = $hadDelay
        delay_reason      = $reason
    })
}

# --- Form 4: Structural frame, days 1-26 ------------------------------------------------
# Floor 1 for the first 17 days, floor 2 thereafter. Formwork blocker is No every day.

Write-Host "Form 4 - structural_progress_daily (26 days)..."
$structuralFloor2From = 18
$structuralDelays = @{
    9  = "weather"
    19 = "equipment_shortage"
    24 = "labor_shortage"
}

for ($d = 1; $d -le 26; $d++) {
    $hadDelay = $structuralDelays.ContainsKey($d)
    $reason = if ($hadDelay) { $structuralDelays[$d] } else { $null }
    $weather = if ($d -eq 9) { "extreme_heat" } else { PickWeather }
    $area = if ($hadDelay) { RandBetween 10 18 } else { RandBetween 20 32 }

    Send-DayRow 'structural' $d ([ordered]@{
        floor             = $(if ($d -ge $structuralFloor2From) { "floor_2" } else { "floor_1" })
        weather           = $weather
        crew_count        = RandBetween 12 16
        area_progress_sqm = $area
        had_delay         = $hadDelay
        delay_reason      = $reason
        formwork_shortage = $false
    })
}

# --- Form 5: MEP, days 17-22 ------------------------------------------------------------
# Nothing before day 17 is missing data - floor 1 isn't structurally ready until then, so
# there is genuinely nothing to report.

Write-Host "Form 5 - mep_progress_daily (6 days, from day 17)..."
$mepStart = 17
$mepDays = 6

for ($i = 0; $i -lt $mepDays; $i++) {
    $d = $mepStart + $i
    Send-DayRow 'mep' $d ([ordered]@{
        floor                 = "floor_1"
        electrical_crew_count = RandBetween 6 9
        electrical_area_sqm   = RandBetween 65 85
        plumbing_crew_count   = RandBetween 6 9
        plumbing_area_sqm     = RandBetween 65 85
        hvac_crew_count       = RandBetween 6 9
        hvac_area_sqm         = RandBetween 65 85
        is_blocked            = $false
        blocked_details       = $null
    })
}

# --- Summary ----------------------------------------------------------------------------

Write-Host ""
Write-Host "--- Summary ----------------------------------------------------------------"
Write-Host "Project: $ProjectName  ($projectId)"
$total = 0
foreach ($key in $forms.Keys) {
    Write-Host ("  {0,-32} submitted: {1,3}   skipped (already present): {2,3}" -f $formCodes[$key], $submitted[$key], $skipped[$key])
    $total += $submitted[$key]
}
Write-Host ""
if ($DryRun) {
    Write-Host "DRY RUN complete - $total rows would have been submitted. Re-run without -DryRun to write them." -ForegroundColor Yellow
} else {
    Write-Host "Done - $total rows submitted."
}
