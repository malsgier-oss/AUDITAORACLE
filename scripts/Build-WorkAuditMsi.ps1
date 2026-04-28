# Builds a self-contained WorkAudit.msi (WiX) for copy/deploy to other PCs.
# Requires: .NET SDK, WiX pulled in via NuGet (no separate WiX install needed).
#
# Usage:
#   .\scripts\Build-WorkAuditMsi.ps1
#   .\scripts\Build-WorkAuditMsi.ps1 -SkipPublishApp   # use existing artifacts\msi-app
#   .\scripts\Build-WorkAuditMsi.ps1 -CopyToDeploy    # also copy MSI under deploy\

param(
    [switch] $SkipPublishApp,
    [switch] $CopyToDeploy
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$installerProj = Join-Path $root "installer\WorkAudit.Installer.wixproj"
if (-not (Test-Path $installerProj)) {
    throw "Installer project not found: $installerProj"
}

$args = @("build", $installerProj, "-c", "Release")
if ($SkipPublishApp) {
    $args += "-p:SkipPublishApp=true"
}

Write-Host "dotnet $($args -join ' ')"
& dotnet @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$msi = Join-Path $root "installer\bin\Release\Audita.msi"
if (-not (Test-Path $msi)) {
    throw "MSI not found at $msi"
}

Write-Host "MSI: $msi"

$verifyScript = Join-Path $root "scripts\Verify-WorkAuditMsiVersion.ps1"
if (Test-Path $verifyScript) {
    & $verifyScript -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($CopyToDeploy) {
    $destDir = Join-Path $root "deploy"
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    Copy-Item -LiteralPath $msi -Destination (Join-Path $destDir "Audita.msi") -Force
    Write-Host "Copied to $(Join-Path $destDir 'Audita.msi')"
}
