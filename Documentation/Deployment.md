# WorkAudit Deployment Guide

## Overview

This guide covers deploying WorkAudit for production use.

---

## Prerequisites

- **Windows 10/11** (64-bit)
- **.NET 8 Runtime** (Desktop)
- **Oracle Database 19c+** reachable by ODP.NET connection string
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

- **Connection source:** `WORKAUDIT_ORACLE_CONNECTION` (or `WORKAUDIT_ORACLE_CONN` / `ORACLE_CONNECTION_STRING`)
- **User setting fallback:** `oracle_connection_string` in `%APPDATA%\WORKAUDIT\user_settings.json`
- **Base directory:** still controls document files, not the Oracle database location
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

- `WORKAUDIT_ORACLE_CONNECTION`: Override Oracle connection string.
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

1. Backup Oracle schema (DBA export) and `%APPDATA%\WORKAUDIT\` document/settings files.
2. Stop the app.
3. Replace app files.
4. Start the app (migrations run automatically).
5. Verify in Control Panel → System.

## Oracle 19c upgrade and rollback

### Upgrade steps

1. Stop all running instances of WorkAudit.
2. Take an Oracle export of the WorkAudit schema (`expdp`) and copy `%APPDATA%\WORKAUDIT\` documents/settings.
3. Install the new binaries and relaunch the app.
4. Confirm startup completes without `Migration` errors in `%APPDATA%\WORKAUDIT\Logs\`.
5. Run a baseline validation by checking the application `System` view:
   - Migration version should be at or above the expected target.
   - `Database path` should show `Oracle` connection details in logs and UI.
6. Confirm report and assignment workflows execute once before returning to normal service.

### Rollback guidance

1. Keep the previous version binaries available.
2. Stop the app.
3. Restore the previous binaries and `WORKAUDIT_ORACLE_CONNECTION` settings if needed.
4. If the schema migration failed partway, restore from the DB export taken in step 2 and restart.
5. If application startup still fails, preserve logs and contact support with:
   - `%APPDATA%\WORKAUDIT\Logs\workaudit-*.log`
   - Oracle migration table state (`WORKAUDIT_MIGRATIONS` table content)

---

## Troubleshooting

| Issue | Action |
|-------|--------|
| App won't start | Check .NET 8 Runtime installed; review logs for errors. |
| Oracle connection fails | Validate `WORKAUDIT_ORACLE_CONNECTION` and Oracle listener/service reachability. |
| Scheduled report not sent | Verify SMTP settings; check "Email to" and "SMTP host" are set. |
| Reports fail | Check disk space; verify document store has data. |

---

*Last updated: 2026-02-06*
