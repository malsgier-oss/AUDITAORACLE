# Expands Microsoft's WebView2 Fixed Version runtime (x64) next to your published app so
# end users do not need the Evergreen WebView2 Runtime installer.
#
# 1) Download "Fixed Version" x64 from https://developer.microsoft.com/microsoft-edge/webview2/
#    (Get the .cab for the runtime, not the installer-only stub.)
# 2) Run:
#    .\scripts\Expand-WebView2FixedRuntime.ps1 -CabPath "C:\path\Microsoft.WebView2.FixedVersionRuntime.x64.cab" -Destination "C:\path\publish\WebView2Runtime"
#
param(
    [Parameter(Mandatory = $true)]
    [string] $CabPath,
    [Parameter(Mandatory = $true)]
    [string] $Destination
)

if (-not (Test-Path -LiteralPath $CabPath)) {
    throw "Cab not found: $CabPath"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$expand = Join-Path $env:SystemRoot "System32\expand.exe"
if (-not (Test-Path -LiteralPath $expand)) {
    throw "expand.exe not found at $expand"
}

& $expand $CabPath -F:* $Destination
if ($LASTEXITCODE -ne 0) {
    throw "expand.exe failed with exit code $LASTEXITCODE"
}

$exe = Get-ChildItem -Path $Destination -Filter "msedgewebview2.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) {
    throw "msedgewebview2.exe not found under $Destination — check CAB contents and WebView2 download page."
}

Write-Host "OK: fixed runtime extracted. msedgewebview2.exe at $($exe.DirectoryName)"
