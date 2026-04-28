# WorkAudit Deployment Guide

## Overview

This guide covers deploying WorkAudit for production use.

---

## Prerequisites

- **Windows 10/11** (64-bit)
- **.NET 8 Runtime** (Desktop)
- **SQLite** (included via Microsoft.Data.Sqlite)
- **Disk space:** ~500 MB for app + data + logs

---

## Installation

### Option A: Build from Source

1. Clone or extract the repository.
2. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Run:
   ```powershell
   cd WorkAudit.CSharp
   dotnet publish -c Release -r win-x64 --self-contained false
   ```
4. Output: `bin\Release\net8.0-windows\win-x64\publish\`

### Option B: Portable

1. Copy the publish folder to a network share or local drive.
2. Ensure users have read/execute access.
3. Create a shortcut to `WorkAudit.exe`.

---

## Database

- **Default location:** `%USERPROFILE%\Documents\WORKAUDIT_Docs\workaudit.db`
- **Note:** The database is stored in the base directory configured during first-run setup. The default base directory is `Documents\WORKAUDIT_Docs`.
- **Change location:** Run the Setup Wizard again or manually edit `%APPDATA%\WORKAUDIT\user_settings.json` (requires app restart).
- **First run:** Database and migrations run automatically.

### Migrations

- Migrations run on startup.
- If migrations fail, check logs in `%APPDATA%\WORKAUDIT\Logs\`.

---

## Configuration

### App Settings

- Stored in `%APPDATA%\WORKAUDIT\` (config, app_settings).
- Backup this folder before major upgrades.

### Environment Variables (Optional)

- `WORKAUDIT_DB_PATH:** Override database path.
- `WORKAUDIT_LOG_LEVEL:** Debug, Information, Warning, Error.

---

## Scheduled Reports

- Requires the app to be **running** at the scheduled time.
- Options:
  1. **Run as user:** User keeps app open (e.g., on a dedicated workstation).
  2. **Run as service:** Use a Windows Service wrapper or Task Scheduler to launch the app at startup and keep it running.

### Task Scheduler (Example)

1. Create a task that runs at logon.
2. Action: Start `WorkAudit.exe` with working directory set to the app folder.
3. Ensure "Run whether user is logged on or not" is configured if running unattended.

---

## Backup

- **Automatic:** Enable in Control Panel → Backup.
- **Manual:** Use Backup → Create Backup Now.
- **Restore:** Backup → Restore from Backup.

---

## Updates

1. Backup database and `%APPDATA%\WORKAUDIT\`.
2. Stop the app.
3. Replace app files.
4. Start the app (migrations run automatically).
5. Verify in Control Panel → System.

---

## Troubleshooting

| Issue | Action |
|-------|--------|
| App won't start | Check .NET 8 Runtime installed; review logs for errors. |
| Database locked | Ensure only one instance; check for other processes. |
| Scheduled report not sent | Verify SMTP settings; check "Email to" and "SMTP host" are set. |
| Reports fail | Check disk space; verify document store has data. |

---

*Last updated: 2026-02-06*
