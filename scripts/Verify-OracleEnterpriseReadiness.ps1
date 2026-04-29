param(
    [switch]$RequireManagedMode
)

$ErrorActionPreference = "Stop"

function Write-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    $color = if ($Passed) { "Green" } else { "Red" }
    $state = if ($Passed) { "PASS" } else { "FAIL" }
    Write-Host "[$state] $Name - $Detail" -ForegroundColor $color
}

function Get-EnvValue {
    param([string]$Name)
    return [Environment]::GetEnvironmentVariable($Name, "Machine")
}

function Parse-ConnectionString {
    param([string]$ConnectionString)
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder.ConnectionString = $ConnectionString
    return $builder
}

function Test-OracleEndpointReachability {
    param([string]$DataSource)

    $normalized = $DataSource.Trim()
    $host = $null
    $port = $null

    if ($normalized -match "^//(?<host>[^:/\)]+):(?<port>\d+)/") {
        $host = $Matches["host"]
        $port = [int]$Matches["port"]
    } elseif ($normalized -match "^\(DESCRIPTION=.*HOST\s*=\s*(?<host>[^)\s]+).*(PORT\s*=\s*(?<port>\d+))") {
        $host = $Matches["host"]
        $port = [int]$Matches["port"]
    }

    if (-not $host -or -not $port) {
        return @{
            Passed = $true
            Detail = "Data Source appears to be a TNS alias or custom descriptor; host/port probe skipped."
        }
    }

    try {
        if (Get-Command Test-NetConnection -ErrorAction SilentlyContinue) {
            $ok = Test-NetConnection -ComputerName $host -Port $port -InformationLevel Quiet
            return @{
                Passed = [bool]$ok
                Detail = "Host $host port $port reachable: $ok"
            }
        }

        $ok = Test-Connection -ComputerName $host -Count 1 -Quiet
        return @{
            Passed = [bool]$ok
            Detail = "Host $host ping reachable: $ok (port test unavailable)"
        }
    } catch {
        return @{
            Passed = $false
            Detail = "Network probe failed: $($_.Exception.Message)"
        }
    }
}

Write-Host "Verifying Oracle enterprise readiness..." -ForegroundColor Cyan

$oracle = Get-EnvValue -Name "WORKAUDIT_ORACLE_CONNECTION"
$managed = Get-EnvValue -Name "WORKAUDIT_REQUIRE_ORACLE_ENV"

$hasOracle = -not [string]::IsNullOrWhiteSpace($oracle)
Write-Check -Name "WORKAUDIT_ORACLE_CONNECTION set" -Passed $hasOracle -Detail "Machine scope"
if (-not $hasOracle) {
    throw "WORKAUDIT_ORACLE_CONNECTION is missing at machine scope."
}

$managedEnabled = ($managed -match "^(1|true|yes)$")
if ($RequireManagedMode -or $managedEnabled) {
    Write-Check -Name "Managed mode flag" -Passed $managedEnabled -Detail "WORKAUDIT_REQUIRE_ORACLE_ENV=$managed"
    if (-not $managedEnabled) {
        throw "Managed mode is required but WORKAUDIT_REQUIRE_ORACLE_ENV is not enabled."
    }
}

if ($oracle -match "change-me|yourpassword|password=\*\*\*") {
    Write-Check -Name "Placeholder credential check" -Passed $false -Detail "Connection string still contains placeholder values."
    throw "Replace placeholder credentials in WORKAUDIT_ORACLE_CONNECTION."
}
Write-Check -Name "Placeholder credential check" -Passed $true -Detail "No placeholder markers detected."

try {
    $parsed = Parse-ConnectionString -ConnectionString $oracle
    $hasUserId = $parsed.ContainsKey("User Id") -or $parsed.ContainsKey("UserID")
    $hasDataSource = $parsed.ContainsKey("Data Source")
    Write-Check -Name "Connection string shape" -Passed ($hasUserId -and $hasDataSource) -Detail "User Id/Data Source keys"
    if (-not ($hasUserId -and $hasDataSource)) {
        throw "Oracle connection string must include User Id and Data Source."
    }

    $dataSource = [string]$parsed["Data Source"]
    $reachability = Test-OracleEndpointReachability -DataSource $dataSource
    Write-Check -Name "Oracle endpoint network reachability" -Passed $reachability.Passed -Detail $reachability.Detail
    if (-not $reachability.Passed) {
        throw "Oracle host/port is unreachable."
    }
} catch {
    Write-Check -Name "Connection string parse" -Passed $false -Detail $_.Exception.Message
    throw
}

$logDir = Join-Path $env:APPDATA "WORKAUDIT\Logs"
$latestLog = Get-ChildItem -Path $logDir -Filter "workaudit-*.log" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($latestLog) {
    $tail = Get-Content -Path $latestLog.FullName -Tail 200 -ErrorAction SilentlyContinue
    $hasOracleBootError = $tail -match "BOOT_ORACLE_(MISSING|MALFORMED|UNREACHABLE|ENV_REQUIRED)"
    $hasMigrationVersion = $tail -match "Database migrations applied, version"
    Write-Check -Name "Startup Oracle error codes absent (latest log tail)" -Passed (-not $hasOracleBootError) -Detail $latestLog.Name
    Write-Check -Name "Migration version log present" -Passed $hasMigrationVersion -Detail $latestLog.Name
} else {
    Write-Check -Name "Startup log discovery" -Passed $false -Detail "No workaudit-*.log found under $logDir"
}

Write-Host "Oracle enterprise readiness verification completed." -ForegroundColor Green
