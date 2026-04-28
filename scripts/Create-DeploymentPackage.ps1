# WorkAudit Installer Build Guide

<#
## Prerequisites

1. **WiX Toolset v4.x** - Download from https://wixtoolset.org/
2. **.NET 8.0 SDK** - Download from https://dot.net/
3. **Visual Studio 2022** (optional, for WiX project support)

## Building the MSI Installer

### Option 1: Using WiX Command Line (Recommended for CI/CD)

```powershell
# Build the application in Release mode
dotnet publish WorkAudit.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false

# Compile WiX installer (from installer directory)
cd installer
wix build -arch x64 -o ..\bin\Release\WorkAudit-Setup.msi WorkAudit.wxs
```

### Option 2: Using Visual Studio

1. Open `WorkAudit.sln`
2. Right-click on the installer project (once created)
3. Select "Build"
4. MSI will be generated in `bin\Release\`

## Manual Deployment Package (Current Method)

Until the WiX installer is created, use the following PowerShell script to create a deployment package:

### Create-DeploymentPackage.ps1

```powershell
#>
# WorkAudit Deployment Package Builder
# Creates a distributable ZIP with the application, dependencies, and installation scripts

param(
    [string]$OutputDir = ".\deploy",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "Building WorkAudit deployment package v$Version..." -ForegroundColor Cyan

# Clean and create output directory
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# Build the application
Write-Host "Building application (Release mode)..." -ForegroundColor Yellow
dotnet publish WorkAudit.csproj -c Release -r win-x64 --self-contained false -o "$OutputDir\app"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Copy installation scripts
Write-Host "Copying installation scripts..." -ForegroundColor Yellow
@"
@echo off
REM WorkAudit Installation Script
REM Run as Administrator

echo ========================================
echo   WorkAudit Installation
echo ========================================
echo.

REM Check for admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator
    echo Right-click and select "Run as Administrator"
    pause
    exit /b 1
)

REM Set installation directory
set INSTALL_DIR=%ProgramFiles%\WorkAudit
echo Installing to: %INSTALL_DIR%
echo.

REM Create directory
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
)

REM Copy files
echo Copying application files...
xcopy /E /I /Y "app\*" "%INSTALL_DIR%"

REM Create desktop shortcut
echo Creating desktop shortcut...
powershell -Command "& {`$WshShell = New-Object -ComObject WScript.Shell; `$Shortcut = `$WshShell.CreateShortcut('%PUBLIC%\Desktop\WorkAudit.lnk'); `$Shortcut.TargetPath = '%INSTALL_DIR%\WorkAudit.exe'; `$Shortcut.WorkingDirectory = '%INSTALL_DIR%'; `$Shortcut.Description = 'WorkAudit Document Management System'; `$Shortcut.Save()}"

REM Create Start Menu shortcut
echo Creating Start Menu shortcut...
if not exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit" (
    mkdir "%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit"
)
powershell -Command "& {`$WshShell = New-Object -ComObject WScript.Shell; `$Shortcut = `$WshShell.CreateShortcut('%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit\WorkAudit.lnk'); `$Shortcut.TargetPath = '%INSTALL_DIR%\WorkAudit.exe'; `$Shortcut.WorkingDirectory = '%INSTALL_DIR%'; `$Shortcut.Description = 'WorkAudit Document Management System'; `$Shortcut.Save()}"

echo.
echo ========================================
echo   Installation Complete!
echo ========================================
echo.
echo WorkAudit has been installed to:
echo   %INSTALL_DIR%
echo.
echo Shortcuts created on:
echo   - Desktop
echo   - Start Menu
echo.
echo Press any key to exit...
pause >nul
"@ | Out-File -FilePath "$OutputDir\install.bat" -Encoding ASCII

# Create uninstall script
@"
@echo off
REM WorkAudit Uninstallation Script
REM Run as Administrator

echo ========================================
echo   WorkAudit Uninstallation
echo ========================================
echo.

REM Check for admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator
    pause
    exit /b 1
)

set INSTALL_DIR=%ProgramFiles%\WorkAudit

echo WARNING: This will remove WorkAudit from your system.
echo User data in AppData\WORKAUDIT will NOT be deleted.
echo.
set /p CONFIRM="Continue with uninstallation? (Y/N): "
if /i not "%CONFIRM%"=="Y" (
    echo Uninstallation cancelled.
    pause
    exit /b 0
)

echo.
echo Removing application files...
if exist "%INSTALL_DIR%" (
    rmdir /S /Q "%INSTALL_DIR%"
)

echo Removing desktop shortcut...
del "%PUBLIC%\Desktop\WorkAudit.lnk" 2>nul

echo Removing Start Menu shortcut...
rmdir /S /Q "%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit" 2>nul

echo.
echo ========================================
echo   Uninstallation Complete
echo ========================================
echo.
echo WorkAudit has been removed.
echo User data remains in: %APPDATA%\WORKAUDIT
echo.
pause
"@ | Out-File -FilePath "$OutputDir\uninstall.bat" -Encoding ASCII

# Create README
@"
# WorkAudit Deployment Package v$Version

## Installation Instructions

### Standard Installation (Recommended)

1. Extract this ZIP file to a temporary location
2. Right-click `install.bat` and select "Run as Administrator"
3. Follow the on-screen prompts
4. Launch WorkAudit from Desktop or Start Menu

### Manual Installation

1. Copy the `app` folder contents to your desired location (e.g., C:\Program Files\WorkAudit)
2. Run `WorkAudit.exe` from the installation directory

### First Launch

- Default admin credentials will be displayed on first launch
- **CRITICAL**: Save the temporary password and change it immediately
- Configure base directory and Oracle connection in Settings

### System Requirements

- Windows 10 version 1809 or later (Windows 11 supported)
- .NET 8.0 Runtime (Desktop) - Download from https://dotnet.microsoft.com/download/dotnet/8.0
- 4 GB RAM minimum (8 GB recommended)
- 10 GB free disk space minimum (50 GB recommended for production)
- Display: 1920x1080 or higher

### Configuration via Environment Variables

Set these environment variables BEFORE first launch to customize defaults:

- `WORKAUDIT_BASE_DIR` - Base directory for data storage
- `WORKAUDIT_ORACLE_CONNECTION` - Oracle connection string
- `WORKAUDIT_ADMIN_USERNAME` - Default admin username (default: admin)
- `WORKAUDIT_ADMIN_EMAIL` - Default admin email
- `WORKAUDIT_ADMIN_BRANCH` - Default admin branch

### Uninstallation

1. Right-click `uninstall.bat` and select "Run as Administrator"
2. Follow the prompts
3. User data in `%APPDATA%\WORKAUDIT` will be preserved

### Support

For technical support or issues, check logs at:
`%APPDATA%\WORKAUDIT\Logs\workaudit-YYYYMMDD.log`

---

**Build Date**: $(Get-Date -Format "yyyy-MM-dd HH:mm")
**Version**: $Version
"@ | Out-File -FilePath "$OutputDir\README.txt" -Encoding UTF8

# Create deployment ZIP
Write-Host "Creating deployment ZIP..." -ForegroundColor Yellow
$zipPath = ".\WorkAudit-v$Version-Deployment.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "`nDeployment package created successfully!" -ForegroundColor Green
Write-Host "  Package: $zipPath" -ForegroundColor Cyan
Write-Host "  Size: $([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB" -ForegroundColor Cyan
Write-Host "`nTo deploy:" -ForegroundColor Yellow
Write-Host "  1. Copy ZIP to target machine"
Write-Host "  2. Extract and run install.bat as Administrator"
Write-Host "  3. Configure on first launch"
