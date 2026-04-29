param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Invoke-Gate {
    param(
        [string]$Name,
        [string]$Command
    )

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host "    $Command" -ForegroundColor DarkGray
    Invoke-Expression $Command
}

Write-Host "Running WorkAudit quality gates ($Configuration)..." -ForegroundColor Green

Invoke-Gate -Name "Build application" -Command "dotnet build WorkAudit.csproj -c $Configuration"
Invoke-Gate -Name "Run test suite" -Command "dotnet test WorkAudit.Tests\WorkAudit.Tests.csproj -c $Configuration --logger `"trx;LogFileName=workaudit-tests.trx`" --results-directory TestResults"
Invoke-Gate -Name "Build installer project" -Command "dotnet build installer\WorkAudit.Installer.wixproj -c $Configuration"

Write-Host ""
Write-Host "All quality gates passed." -ForegroundColor Green
