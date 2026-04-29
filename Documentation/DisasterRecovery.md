# WorkAudit Disaster Recovery Guide

This guide explains how to recover WorkAudit data after a failure (disk loss, corruption, or accidental deletion).

---

## 1. Where Your Data Lives

| Data | Location |
|------|----------|
| **Database** | Oracle schema referenced by `WORKAUDIT_ORACLE_CONNECTION` (or saved `oracle_connection_string`). |
| **Documents** | Base directory (e.g. `Documents\WORKAUDIT_Docs` or your configured path). |
| **Automatic backups** | `%APPDATA%\WORKAUDIT\Backups\` — ZIP files named `WorkAudit_Backup_yyyyMMdd_HHmmss.zip`. |
| **User settings** | `%APPDATA%\WORKAUDIT\user_settings.json`. |

---

## 2. Backup Contents

Each backup ZIP contains:

- **manifest.json** — Version, creation time, machine name, whether documents are included, and (if enabled) Oracle export metadata (`IncludesOracleSchema`, dump/log file names, DIRECTORY name).
- **Documents/** — Copy of the document base directory (if “Include documents” was selected).
- **Oracle/** (optional) — Copies of Data Pump `.dmp` / `.log` files produced when **Include Oracle schema** is enabled. This requires Oracle client `expdp`/`impdp` on the workstation, an Oracle `DIRECTORY` object, and app setting **oracle_datapump_local_folder** pointing to the same physical path the database uses for that DIRECTORY (often a UNC share or mapped drive).

When Oracle is **not** included in the ZIP, treat RMAN / DBA `expdp` on the server as the authoritative database backup; the app ZIP still protects document files and configuration workflow.

---

## 3. Restoring from a Backup

### Option A: Restore a specific backup file

1. Close WorkAudit.
2. Locate the backup ZIP (e.g. in `%APPDATA%\WORKAUDIT\Backups` or on USB/network).
3. Use **Tools → Backup** → **Restore from backup file…** (or **Control Panel** → **Backup** → **Restore from Backup…**).
4. Select the `.zip` file. The app will:
   - Optionally create a safety backup of the current state (documents and, if configured, Oracle).
   - If the backup contains **Oracle/** and you confirm schema restore, run **impdp** into the current schema, then replace document files from **Documents/**.
5. Restart WorkAudit and verify data.

### Option B: Point-in-time recovery

If the app supports **point-in-time recovery**:

1. Choose a **target date** (e.g. last known good day).
2. The system selects the **most recent backup** on or before that date.
3. Restore runs as above using that backup.

### Option C: Manual restore (no app)

1. Close WorkAudit.
2. Copy your current database and base directory elsewhere (safety copy).
3. Extract the backup ZIP to a temporary folder.
4. Restore Oracle data from your Oracle backup/export procedure for the target schema.
5. If the backup includes a `Documents` folder, copy its contents over your document base directory.
6. Start WorkAudit and verify.

---

## 4. Verifying Backups

Before relying on a backup:

- Use **Tools → Backup → Verify recent backups** (or verify via API) to check manifest and, for Oracle-enabled backups, that the `Oracle/*.dmp` entry exists in the ZIP. Encrypted backups must be decrypted before ZIP verification.
- Periodically **restore to a test location** and open WorkAudit against it to confirm the backup is usable.

---

## 5. Backup to External Drive (USB / Network)

To reduce risk of losing backups with the machine:

1. Open **Tools → Backup**.
2. Click **Backup to external drive...**.
3. Choose a folder on a **USB drive** or **network share**.
4. Ensure **Include documents** is checked, then **Create Backup**.
5. Keep at least one recent backup off the main PC.

---

## 6. Quick Recovery Checklist

- [ ] WorkAudit is closed.
- [ ] You have identified the correct backup ZIP (by date/time or location).
- [ ] You have a safety copy of the current Oracle schema and documents (if they still exist).
- [ ] You restored Oracle data and documents from backup.
- [ ] You restarted WorkAudit and confirmed login and data.
- [ ] You ran verification on the backup (if the feature is available).

---

## 7. Getting Help

- **Logs**: Check application logs (e.g. under `%APPDATA%\WORKAUDIT` or the path configured for Serilog) for restore or backup errors.
- **Oracle connection**: Ensure startup configuration points to the intended Oracle schema/service.
- **Permissions**: Ensure the app has read/write access to the document base directory and Oracle account has required DDL/DML permissions.
