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
powershell -Command "& {$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%PUBLIC%\Desktop\WorkAudit.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\WorkAudit.exe'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.Description = 'WorkAudit Document Management System'; $Shortcut.Save()}"

REM Create Start Menu shortcut
echo Creating Start Menu shortcut...
if not exist "%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit" (
    mkdir "%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit"
)
powershell -Command "& {$WshShell = New-Object -ComObject WScript.Shell; $Shortcut = $WshShell.CreateShortcut('%ProgramData%\Microsoft\Windows\Start Menu\Programs\WorkAudit\WorkAudit.lnk'); $Shortcut.TargetPath = '%INSTALL_DIR%\WorkAudit.exe'; $Shortcut.WorkingDirectory = '%INSTALL_DIR%'; $Shortcut.Description = 'WorkAudit Document Management System'; $Shortcut.Save()}"

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
