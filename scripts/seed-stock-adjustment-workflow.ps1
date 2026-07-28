# Creates the Draft -> Pending approval -> Approved/Rejected workflow on Stock Adjustment,
# gated to a single role, and publishes it. Run this against your REAL Azure database -
# the one Claude Code built in its own Docker sandbox doesn't exist here.
#
# Prerequisites: the API is running, you have a fresh JWT (tokens expire - get a new one
# via POST /api/auth/login if this fails with a 401), and you know the real Stock Adjustment
# form's ID (from your earlier seed-stock-management-forms.ps1 output) and a Role ID to
# gate approvals to (the Administrator role you created earlier).
#
# Usage:
#   .\seed-stock-adjustment-workflow.ps1 -BaseUrl "http://localhost:5080" -Token "eyJ..." `
#       -StockAdjustmentFormId "guid-here" -ApproverRoleId "guid-here"

param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$StockAdjustmentFormId,
    [Parameter(Mandatory = $true)][string]$ApproverRoleId
)

$ErrorActionPreference = "Stop"
$headers = @{ Authorization = "Bearer $Token" }

function Post($path, $body) {
    $json = $body | ConvertTo-Json
    return Invoke-RestMethod -Uri "$BaseUrl$path" -Method Post -Headers $headers -Body $json -ContentType "application/json"
}

Write-Host "Creating workflow definition..."
$workflow = Post "/api/workflows" @{
    code = "stock-adjustment-approval"
    name = "Stock Adjustment Approval"
    formDefinitionId = $StockAdjustmentFormId
}
$workflowId = $workflow.id
Write-Host "  -> $workflowId"

Write-Host "Adding states..."
$draft = Post "/api/workflows/$workflowId/states" @{ code = "draft"; label = "Draft"; isInitial = $true; isFinal = $false }
$pending = Post "/api/workflows/$workflowId/states" @{ code = "pending-approval"; label = "Pending approval"; isInitial = $false; isFinal = $false }
$approved = Post "/api/workflows/$workflowId/states" @{ code = "approved"; label = "Approved"; isInitial = $false; isFinal = $true }
$rejected = Post "/api/workflows/$workflowId/states" @{ code = "rejected"; label = "Rejected"; isInitial = $false; isFinal = $true }
Write-Host "  draft=$($draft.id) pending=$($pending.id) approved=$($approved.id) rejected=$($rejected.id)"

Write-Host "Adding transitions..."
Post "/api/workflows/$workflowId/transitions" @{
    code = "submit-for-approval"; label = "Submit for approval"
    fromStateId = $draft.id; toStateId = $pending.id
    allowedRoleIds = @($ApproverRoleId)
} | Out-Null

Post "/api/workflows/$workflowId/transitions" @{
    code = "approve"; label = "Approve"
    fromStateId = $pending.id; toStateId = $approved.id
    allowedRoleIds = @($ApproverRoleId)
} | Out-Null

Post "/api/workflows/$workflowId/transitions" @{
    code = "reject"; label = "Reject"
    fromStateId = $pending.id; toStateId = $rejected.id
    allowedRoleIds = @($ApproverRoleId)
} | Out-Null

Write-Host "Publishing..."
Invoke-RestMethod -Uri "$BaseUrl/api/workflows/$workflowId/publish" -Method Post -Headers $headers | Out-Null

Write-Host ""
Write-Host "Workflow published. Workflow definition ID: $workflowId"
Write-Host "Next: submit a real record to Stock Adjustment, then GET /api/records/{recordId}/workflow"
Write-Host "to see it start at 'draft', then POST to /api/records/{recordId}/workflow/transitions"
Write-Host "with { \"transitionCode\": \"submit-for-approval\" } and then \"approve\" to walk it through."
