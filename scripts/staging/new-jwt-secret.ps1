# Puts a new random JWT signing secret (48 random bytes, base64 - 64 characters) on the
# clipboard without printing it. Paste it into asas-api-staging > Environment variables >
# Jwt__Secret. Never reuse production's value: a different secret means a token issued by
# one environment is never accepted by the other.
#   powershell -ExecutionPolicy Bypass -File scripts\staging\new-jwt-secret.ps1

$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes) | Set-Clipboard
Write-Host "A new JWT secret is on the clipboard (64 characters). Paste it into Jwt__Secret."
