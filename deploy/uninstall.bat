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
