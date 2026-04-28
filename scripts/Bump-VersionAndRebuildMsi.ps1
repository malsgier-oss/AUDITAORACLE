# Bumps the patch version in build\WorkAudit.Version.props (e.g. 1.0.0 -> 1.0.1), then rebuilds Audita.msi.
# Use when you need a *new* MSI for upgrade installs (same UpgradeCode, higher version).
#
# Usage:
#   .\scripts\Bump-VersionAndRebuildMsi.ps1
#   .\scripts\Bump-VersionAndRebuildMsi.ps1 -SkipPublishApp
#   .\scripts\Bump-VersionAndRebuildMsi.ps1 -WhatIf

param(
    [switch] $SkipPublishApp,
    [switch] $WhatIf
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$propsPath = Join-Path $root "build\WorkAudit.Version.props"
if (-not (Test-Path $propsPath)) { throw "Missing $propsPath" }

$raw = Get-Content -LiteralPath $propsPath -Raw
if ($raw -notmatch '<Version>\s*([^<\s]+)\s*</Version>') {
    throw "Could not parse <Version> in WorkAudit.Version.props"
}
$oldThree = $matches[1].Trim()
$v = [version]($oldThree + ".0")
$newMajor = $v.Major
$newMinor = $v.Minor
$newPatch = $v.Build + 1
$newThree = "$newMajor.$newMinor.$newPatch"
$newFour = "$newMajor.$newMinor.$newPatch.0"

Write-Host "Version bump: $oldThree -> $newThree (Assembly/File: $newFour)"

if ($WhatIf) { exit 0 }

$raw2 = [regex]::Replace($raw, '(<Version>\s*)[^<]+(\s*</Version>)', "`${1}$newThree`${2}", 1)
$raw2 = [regex]::Replace($raw2, '(<AssemblyVersion>\s*)[^<]+(\s*</AssemblyVersion>)', "`${1}$newFour`${2}", 1)
$raw2 = [regex]::Replace($raw2, '(<FileVersion>\s*)[^<]+(\s*</FileVersion>)', "`${1}$newFour`${2}", 1)
[System.IO.File]::WriteAllText($propsPath, $raw2, [System.Text.UTF8Encoding]::new($false))

Write-Host "Updated $propsPath"

$buildArgs = @("build", (Join-Path $root "installer\WorkAudit.Installer.wixproj"), "-c", "Release")
if ($SkipPublishApp) { $buildArgs += "-p:SkipPublishApp=true" }

Push-Location $root
try {
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

$msi = Join-Path $root "installer\bin\Release\Audita.msi"
Write-Host "Done: $msi"

$verifyScript = Join-Path $root "scripts\Verify-WorkAuditMsiVersion.ps1"
if (Test-Path $verifyScript) {
    & $verifyScript -Configuration Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
