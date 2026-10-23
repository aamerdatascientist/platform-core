# Sends the staging Web App's publish profile to GitHub as the
# AZURE_WEBAPP_PUBLISH_PROFILE_STAGING secret, then deletes the downloaded file.
# Download it first: asas-api-staging > Overview > ... > Download publish profile.
#   powershell -ExecutionPolicy Bypass -File scripts/staging/set-publish-profile-secret.ps1

$ErrorActionPreference = "Stop"
$downloads = Join-Path $env:USERPROFILE "Downloads"
$file = Get-ChildItem -Path $downloads -Filter "asas-api-staging*.PublishSettings" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $file) { throw "No asas-api-staging*.PublishSettings in $downloads. Download it first." }

$content = Get-Content -Path $file.FullName -Raw
if ($content -notmatch "<publishData>") { throw "$($file.Name) doesn't look like a publish profile." }

$content | gh secret set AZURE_WEBAPP_PUBLISH_PROFILE_STAGING
if ($LASTEXITCODE -ne 0) { throw "gh secret set failed (exit $LASTEXITCODE). The file was kept." }

Remove-Item -Path $file.FullName
Write-Host "Secret AZURE_WEBAPP_PUBLISH_PROFILE_STAGING set from $($file.Name); the file was deleted."
