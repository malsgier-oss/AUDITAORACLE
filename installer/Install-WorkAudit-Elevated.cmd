@echo off
REM Windows 11 often hides "Run as administrator" on .msi until you use "Show more options".
REM Double-click THIS file instead — it will show the UAC prompt and start the installer.

setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-WorkAudit-Elevated.ps1"
if errorlevel 1 pause
