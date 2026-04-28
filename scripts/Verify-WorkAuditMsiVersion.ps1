# Compares ProductVersion in installer\bin\<Configuration>\Audita.msi to <Version> in build\WorkAudit.Version.props.
# Run after building the MSI (e.g. from Build-WorkAuditMsi.ps1 or manually).
#
# Usage:
#   .\scripts\Verify-WorkAuditMsiVersion.ps1
#   .\scripts\Verify-WorkAuditMsiVersion.ps1 -Configuration Debug

param(
    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$propsPath = Join-Path $root "build\WorkAudit.Version.props"
$msiPath = Join-Path $root "installer\bin\$Configuration\Audita.msi"

if (-not (Test-Path $propsPath)) { throw "Missing $propsPath" }
if (-not (Test-Path -LiteralPath $msiPath)) { throw "MSI not found: $msiPath (build the installer first)." }
$msiPath = (Resolve-Path -LiteralPath $msiPath).Path

$raw = Get-Content -LiteralPath $propsPath -Raw
if ($raw -notmatch '<Version>\s*([^<\s]+)\s*</Version>') {
    throw "Could not parse <Version> in $propsPath"
}
$expectedThree = $matches[1].Trim()

function Get-MsiPropertyValue {
    param([string] $Path, [string] $Name)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $installer.OpenDatabase($Path, 0)
    if (-not $db) { throw "OpenDatabase failed for: $Path" }
    $view = $db.OpenView("SELECT Value FROM Property WHERE Property='$Name'")
    [void]$view.Execute($null)
    $rec = $view.Fetch()
    if (-not $rec) {
        $view.Close()
        return $null
    }
    try {
        $v = $rec.StringData(1)
        if ($null -eq $v) { return $null }
        if ($v -is [System.Array]) { $v = $v[0] }
        return ([string]$v).Trim()
    }
    finally {
        $view.Close()
    }
}

$productVersion = Get-MsiPropertyValue -Path $msiPath -Name "ProductVersion"
$productVersion = [string]$productVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) {
    throw "Could not read ProductVersion from MSI."
}

# MSI normalizes three-part to four-part (e.g. 1.0.1 -> 1.0.1.0); compare first three fields only.
$exp = [version]($expectedThree.Trim() + ".0")
$got = [version]$productVersion.Trim()
$sameThree = ($exp.Major -eq $got.Major -and $exp.Minor -eq $got.Minor -and $exp.Build -eq $got.Build)

if (-not $sameThree) {
    throw "MSI ProductVersion mismatch: expected first three fields from WorkAudit.Version.props '$expectedThree' but MSI has ProductVersion '$productVersion'. Bump <Version> in build\WorkAudit.Version.props and rebuild."
}

Write-Host "OK: Audita.msi ProductVersion '$($productVersion.Trim())' matches <Version> '$expectedThree' in WorkAudit.Version.props."
