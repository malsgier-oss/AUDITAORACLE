# Launches Audita.msi with a UAC elevation prompt (Run as administrator).
# Double-click this file, or run: powershell -ExecutionPolicy Bypass -File .\Install-WorkAudit-Elevated.ps1

$ErrorActionPreference = "Stop"
$msi = Join-Path $PSScriptRoot "Audita.msi"
if (-not (Test-Path -LiteralPath $msi)) {
    Write-Error "Audita.msi not found next to this script: $msi"
    exit 1
}

Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i", $msi) -Verb RunAs
