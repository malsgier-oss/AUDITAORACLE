# WorkAudit Deployment Package v1.0.0

## Installation Instructions

### Standard Installation (Recommended)

1. Extract this ZIP file to a temporary location
2. Right-click install.bat and select "Run as Administrator"
3. Follow the on-screen prompts
4. Launch WorkAudit from Desktop or Start Menu

### Manual Installation

1. Copy the pp folder contents to your desired location (e.g., C:\Program Files\WorkAudit)
2. Run WorkAudit.exe from the installation directory

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

- WORKAUDIT_BASE_DIR - Base directory for data storage
- WORKAUDIT_ORACLE_CONNECTION - Oracle connection string
- WORKAUDIT_ADMIN_USERNAME - Default admin username (default: admin)
- WORKAUDIT_ADMIN_EMAIL - Default admin email
- WORKAUDIT_ADMIN_BRANCH - Default admin branch

### Uninstallation

1. Right-click uninstall.bat and select "Run as Administrator"
2. Follow the prompts
3. User data in %APPDATA%\WORKAUDIT will be preserved

### Support

For technical support or issues, check logs at:
%APPDATA%\WORKAUDIT\Logs\workaudit-YYYYMMDD.log

---

**Build Date**: 2026-02-25 00:17
**Version**: 1.0.0
