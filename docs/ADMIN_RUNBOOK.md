# WorkAudit System Administrator Runbook

## Version: 1.0.2
## Last Updated: 2026-05-04
## Audience: Bank IT Staff, System Administrators

---

## Table of Contents

1. [System Overview](#system-overview)
2. [Installation](#installation)
3. [Initial Configuration](#initial-configuration)
4. [Daily Operations](#daily-operations)
5. [Monitoring](#monitoring)
6. [Backup and Recovery](#backup-and-recovery)
7. [Security Management](#security-management)
8. [Troubleshooting](#troubleshooting)
9. [Maintenance](#maintenance)
10. [Emergency Procedures](#emergency-procedures)
11. [Multi-PC shared Oracle](#multi-pc-shared-oracle)

---

## 1. System Overview

### Purpose
WorkAudit is a document management system designed for banking compliance, audit, and workflow management. It provides document scanning, processing, metadata management, approval workflows, and comprehensive reporting.

### Architecture
- **Type**: Desktop application (WPF, .NET 8.0)
- **Database**: Oracle 19c (`WORKAUDIT_ORACLE_CONNECTION`), with `TNS_ADMIN`/Wallet support per deployment.
- **Storage**: File system (attachments, images, PDFs)
- **Deployment**: Per-branch installation with centralized reporting

### System Requirements
- Windows 10 version 1809 or later (Windows 11 supported)
- .NET 8.0 Runtime (Desktop)
- 4 GB RAM minimum (8 GB recommended)
- 10 GB free disk space minimum (50 GB+ for production)
- Network connectivity for central sync (optional)

---

## 2. Installation

### Pre-Installation Checklist

- [ ] Verify Windows version (Win+R → winver)
- [ ] Install .NET 8.0 Runtime from https://dotnet.microsoft.com/download/dotnet/8.0
- [ ] Verify disk space (at least 50 GB free)
- [ ] Ensure user has Administrator rights for installation
- [ ] Disable antivirus temporarily if installer is blocked
- [ ] Configure firewall rules if central sync is needed

### Installation Methods

#### Method A: MSI Installer (Recommended)

1. Copy `WorkAudit-Setup.msi` to the target machine
2. Right-click MSI → "Run as Administrator"
3. Follow installation wizard
4. Accept default installation path or customize
5. Click "Install"
6. Wait for completion (~2-5 minutes)

#### Method B: ZIP Deployment Package

1. Extract `WorkAudit-v1.0.2-Deployment.zip`
2. Right-click `install.bat` → "Run as Administrator"
3. Follow on-screen prompts
4. Verify shortcuts created on Desktop and Start Menu

### Post-Installation Verification

```powershell
# Verify installation
Test-Path "C:\Program Files\WorkAudit\WorkAudit.exe"

# Verify .NET Runtime
dotnet --list-runtimes | Select-String "Microsoft.WindowsDesktop.App 8.0"

# Check permissions
icacls "C:\Program Files\WorkAudit"
```

---

## 3. Initial Configuration

### First Launch

1. Launch WorkAudit from Desktop shortcut
2. **CRITICAL**: Save the temporary admin password shown in the popup
3. Login with username: `admin` and the temporary password
4. You will be forced to change the password immediately

### Environment Variables (Set BEFORE First Launch)

For customized deployment, set these system environment variables:

```powershell
# Set via PowerShell (requires admin)
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_BASE_DIR", "D:\WorkAuditData", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION", "Data Source=//db-host:1521/WORKAUDIT;User Id=WORKAUDIT_APP;Password=<secure-secret>;Persist Security Info=True;", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_REQUIRE_ORACLE_ENV", "true", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_USERNAME", "sysadmin", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_EMAIL", "admin@yourbank.com", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_BRANCH", "Main Branch", "Machine")
```

Use deployment secret tooling to inject `WORKAUDIT_ORACLE_CONNECTION`; do not store real credentials in source-controlled scripts.

After setting environment variables, run:
```powershell
.\scripts\Verify-OracleEnterpriseReadiness.ps1 -RequireManagedMode
```

### Essential Settings Configuration

Navigate to **Settings** → **Control Panel**:

1. **Base Directory**: Set to a location with sufficient disk space (e.g., `D:\WorkAuditData`)
2. **Branch Configuration**: Add all bank branches
3. **Document Types**: Configure document types per branch
4. **User Roles**: Create users with appropriate roles (Admin, Manager, Auditor, Viewer)
5. **SMTP Email** (if using scheduled reports):
   - SMTP Host: `smtp.yourbank.com`
   - SMTP Port: `587` (TLS) or `465` (SSL)
   - SMTP User: `workaudit@yourbank.com`
   - SMTP Password: **(will be encrypted automatically)**

---

## 4. Daily Operations

### Starting the Application

- Double-click Desktop shortcut OR
- Start Menu → WorkAudit

### User Login

- Users must authenticate with username/password
- Sessions expire after 8 hours of inactivity
- Failed login attempts are logged to audit trail

### Document Processing Workflow

1. **Import** → Scan/Upload documents
2. **Processing** → Apply image enhancements (crop, deskew, etc.)
3. **Metadata Entry** → Manually enter document metadata (dates, amounts, accounts)
4. **Assignment** → Assign documents for review
5. **Review & Approval** → Manager/auditor reviews and approves
6. **Archive** → Long-term retention with compliance controls

### Monitoring Daily Activity

Check the **Dashboard** view for:
- Active documents count
- Pending assignments
- Overdue items
- Recent activity
- System alerts

---

## 5. Monitoring

### Health Checks

The application includes automatic health monitoring:

- **Database**: Query performance, connection health
- **Storage**: Disk space, file count
- **Memory**: Process memory usage (<2 GB normal)
- **Performance**: Operation duration metrics

### Log Files Location

```
%APPDATA%\WORKAUDIT\Logs\
  - workaudit-YYYYMMDD.log   (main application log)
  - performance-YYYYMMDD.log  (performance metrics)
```

### Key Log Patterns to Monitor

```
HEALTH: System is unhealthy       → Investigate immediately
PERF: * completed in *ms          → Track slow operations (>5000ms)
error CS*                         → Application errors
Authentication failed             → Security concern
Database locked                   → Concurrent access issue
OutOfMemoryException              → Memory leak or large file processing
```

### Performance Metrics to Track

- Document import time (target: <5 seconds per document)
- Database query time (target: <1 second)
- Memory usage (target: <1 GB)
- Daily document throughput (target: 500+ documents/day)

### Recommended Monitoring Schedule

- **Hourly**: Check for application crashes (Event Viewer)
- **Daily**: Review logs for errors, check disk space
- **Weekly**: Review performance metrics, clear temp files
- **Monthly**: Backup verification, security review

---

## 6. Backup and Recovery

### Automatic Backups

Configure scheduled backups in **Control Panel** → **Backup & Recovery**:

1. Enable scheduled backups
2. Set backup time (e.g., 02:00 daily)
3. Set backup location (network share recommended):
   ```
   \\fileserver\backups\workaudit\branch01\
   ```
4. Enable encryption (strongly recommended)
5. Set encryption password (store securely in password manager)

### Manual Backup

From **Control Panel** → **Backup & Recovery**:

1. Click "Create Backup Now"
2. Choose encryption password
3. Select backup location
4. Wait for completion
5. Verify backup file created (`.zip` under `%APPDATA%\WORKAUDIT\Backups` or your chosen folder). Optional Oracle schema adds `Oracle\*.dmp` inside the ZIP when Data Pump is configured.

### Backup Best Practices

- **Frequency**: Daily automatic backups
- **Retention**: Keep last 30 days
- **Location**: Network share (NOT local disk)
- **Encryption**: Always encrypt backups
- **Verification**: Weekly restore test on non-production machine
- **Offsite**: Weekly copy to offsite/cloud storage

### Disaster Recovery Procedure

#### Scenario 1: Application Corruption (Application Files Damaged)

1. Uninstall WorkAudit via Control Panel
2. Delete `C:\Program Files\WorkAudit` directory
3. Reinstall from MSI or deployment package
4. Launch application (will use existing database)
5. Verify data intact

#### Scenario 2: Database Corruption

1. Stop WorkAudit application
2. Navigate to `%APPDATA%\WORKAUDIT\Logs\`
3. Check logs for error details
4. Restore from latest backup:
   - Launch WorkAudit
   - Login as admin
   - Navigate to **Control Panel** → **Backup & Recovery**
   - Click "Restore from Backup"
   - Select backup file
   - Enter decryption password
   - Confirm restoration
5. Restart application

#### Scenario 3: Complete System Failure

1. Install WorkAudit on new machine
2. On first launch, EXIT immediately (don't create admin user)
3. Stop application
4. Restore database file from backup:
   ```powershell
   # If backup is encrypted, use the application to decrypt first
   # Restore database data using the application's backup/restore workflow
   ```
5. Launch application
6. Login with existing admin credentials

### Recovery Time Objectives (RTO)

- **Application reinstall**: 15-30 minutes
- **Database restore**: 5-10 minutes (depends on database size)
- **Full disaster recovery**: 1-2 hours

---

## 7. Security Management

### User Management

#### Creating Users

1. Login as Administrator
2. Navigate to **Settings** → **User Management**
3. Click "Add User"
4. Fill in details:
   - Username (unique, no spaces)
   - Display Name
   - Email
   - Role (Administrator, Manager, Auditor, Viewer)
   - Branch
5. Click "Save"
6. Communicate temporary password to user securely
7. User MUST change password on first login

#### Roles and Permissions

| Role | Permissions |
|------|-------------|
| **Administrator** | Full system access, user management, configuration, backups |
| **Manager** | Document approval, assignment management, reporting |
| **Auditor** | Document review, read-only access to audit trail, reporting |
| **Viewer** | Read-only access to documents and reports |

#### Password Policy

- Minimum 8 characters
- Must include: uppercase, lowercase, digit, special character
- Passwords hashed with BCrypt (cost factor: 12)
- Session tokens expire after 8 hours
- Failed login attempts logged to audit trail

#### Forcing Password Reset

1. Navigate to **Settings** → **User Management**
2. Select user
3. Click "Require Password Change"
4. User will be forced to reset on next login

### Encryption

#### SMTP Password Encryption

SMTP passwords are automatically encrypted using AES-256 with machine-specific keys:
- Format: `enc:v1:<base64-encrypted-data>`
- Stored in `app_settings` table
- Keys stored in `%APPDATA%\WORKAUDIT\.machinekey` (protected by Windows DPAPI)

#### Database File Encryption

Optional file-level encryption for offline database protection:

```powershell
# Not implemented in UI - use IDatabaseEncryptionService API if needed
# For production: Enable Windows BitLocker for full disk encryption
```

#### Backup Encryption

Always encrypt backups:
- Uses AES-256 encryption
- PBKDF2 key derivation (100,000 iterations)
- Unique salt and IV per backup
- Store encryption password in secure password manager

### Audit Trail

All critical actions are logged to the audit trail:
- User login/logout
- Document creation/modification/deletion
- Configuration changes
- User management actions
- Backup/restore operations

View audit trail: **Reports** → **Audit Trail Report**

---

## 8. Troubleshooting

### Common Issues and Resolutions

#### Issue 1: Application Won't Start

**Symptoms**: Double-click does nothing, or immediate crash

**Diagnosis**:
```powershell
# Check Event Viewer
eventvwr.msc
# Navigate to: Windows Logs → Application
# Look for "WorkAudit" or ".NET Runtime" errors
```

**Solutions**:
1. Verify .NET 8.0 installed: `dotnet --list-runtimes`
2. Check log files in `%APPDATA%\WORKAUDIT\Logs\`
3. Run as Administrator (right-click → Run as Administrator)
4. Reinstall application
5. Check antivirus quarantine

#### Issue 2: Database Connectivity/Locking Errors

**Symptoms**: `ORA-*` database connectivity errors, slow queries, or intermittent hangs.

**Diagnosis**:
- Check active session health in Oracle views and application logs.
- Confirm network/TNS stability and endpoint reachability.
- Identify long-running report/maintenance SQL at the same time.

**Solutions**:
```powershell
# Check recent connectivity and database errors
Select-String "$env:APPDATA\WORKAUDIT\Logs\workaudit-$(Get-Date -Format yyyyMMdd).log" -Pattern "ORA-"

# Confirm Oracle session is reachable
[System.Environment]::GetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION", "Machine")
```

#### Issue 3: Out of Disk Space

**Symptoms**: Import fails, application crashes, "disk full" errors

**Solutions**:
1. Check disk space: `Get-PSDrive C | Select-Object Used,Free`
2. Clean up old attachments (archive or delete)
3. Run disk cleanup: `cleanmgr`
4. Move data directory to larger drive
5. Implement retention policies

#### Issue 4: Slow Performance

**Symptoms**: Slow searches, import delays, UI freezing

**Diagnosis**:
1. Check performance logs: `%APPDATA%\WORKAUDIT\Logs\performance-*.log`
2. Look for operations >5000ms
3. Check memory usage in Task Manager

**Solutions**:
1. Run database integrity check (Control Panel → Database Maintenance)
2. Increase pagination limits if searching large datasets
3. Archive old documents (move to Archive)
4. Check for runaway processes (Task Manager)
5. Restart application daily if memory leak suspected

#### Issue 5: Network Share Access Issues (Backups)

**Symptoms**: Scheduled backups fail, cannot save to network

**Solutions**:
1. Verify network connectivity: `Test-Connection \\fileserver`
2. Check share permissions: User must have Read/Write access
3. Test manual file creation:
   ```powershell
   New-Item \\fileserver\backups\test.txt -ItemType File
   ```
4. Use UNC paths (NOT mapped drives): `\\server\share\path`
5. Verify credentials if share requires authentication

---

## 9. Maintenance

### Daily Maintenance Tasks

- [ ] Review application logs for errors
- [ ] Verify scheduled backups completed successfully
- [ ] Check disk space on data drive
- [ ] Monitor active user sessions
- [ ] Review pending assignments (overdue items)

### Weekly Maintenance Tasks

- [ ] Test backup restoration (on non-production machine)
- [ ] Review audit trail for suspicious activity
- [ ] Check performance metrics (slow queries, memory usage)
- [ ] Clear temporary files in `%TEMP%\WorkAudit_*`
- [ ] Review user access (disable inactive users)
- [ ] Run database integrity check

### Monthly Maintenance Tasks

- [ ] Archive old documents (>90 days, completed workflow)
- [ ] Review and purge audit logs (>12 months)
- [ ] Update document type configurations
- [ ] Review and update retention policies
- [ ] Conduct disaster recovery drill
- [ ] Check for application updates
- [ ] Security review (failed logins, permission changes)

### Database Maintenance

#### Run Integrity Check

1. Login as Administrator
2. Navigate to **Control Panel** → **Database Maintenance**
3. Click "Check Database Integrity"
4. Review results
5. If issues found, restore from backup

#### Optimize Database

```powershell
# Oracle maintenance (index/statistics + tablespace tuning) is handled by DBA procedures.
# Run through Control Panel → Database Maintenance → "Optimize Database" as guided.
```

---

## 10. Emergency Procedures

### Procedure A: Data Loss Event

**Trigger**: Accidental deletion, corruption, ransomware

**Steps**:
1. **STOP ALL ACTIVITY** - Disconnect network, close application
2. Document what happened (timestamp, scope, user)
3. Do NOT make further changes
4. Identify last good backup (check backup logs)
5. Notify management and security team
6. Restore from backup on isolated machine
7. Verify restored data before going live
8. Investigate root cause
9. Update procedures to prevent recurrence

### Procedure B: Security Breach

**Trigger**: Unauthorized access, suspicious activity, malware

**Steps**:
1. **IMMEDIATELY** change all user passwords (start with admin)
2. Review audit trail for unauthorized actions
3. Check for unexpected data exports
4. Scan system with antivirus
5. Review user permissions (disable compromised accounts)
6. Change SMTP password
7. Notify security team and management
8. Preserve logs for forensic analysis
9. Conduct full security audit

### Procedure C: Application Crash Loop

**Trigger**: Application crashes immediately on launch

**Steps**:
1. Check Event Viewer for error details
2. Review latest log file: `%APPDATA%\WORKAUDIT\Logs\workaudit-*.log`
3. Attempt safe mode launch:
   ```powershell
   cd "C:\Program Files\WorkAudit"
   .\WorkAudit.exe --safe-mode  # (if implemented)
   ```
4. Restore database from backup
5. Reinstall application
6. Contact vendor support with logs

### Procedure D: Central Sync Failure

**Trigger**: Hybrid sync between branch and central database fails

**Steps**:
1. Verify network connectivity to central server
2. Check sync service status
3. Review sync logs
4. Test central database connection
5. Resolve conflicts manually if needed
6. Retry sync operation
7. If persistent, switch to manual reporting until resolved

---

## Support Contacts

### Internal IT Support
- **Email**: it-support@yourbank.com
- **Phone**: +XXX-XXX-XXXX
- **Hours**: 24/7

### Vendor Support (WorkAudit)
- **Email**: support@workaudit.com (if applicable)
- **Documentation**: See `docs/` directory in installation

---

## Appendix A: File Locations

| Item | Location |
|------|----------|
| Application | `C:\Program Files\WorkAudit\` |
| Database | Oracle 19c (configured via `WORKAUDIT_ORACLE_CONNECTION`) |
| Attachments | `%APPDATA%\WORKAUDIT\attachments\` (or custom base dir) |
| Logs | `%APPDATA%\WORKAUDIT\Logs\` |
| Configuration | `%APPDATA%\WORKAUDIT\user_settings.json` |
| Encryption Keys | `%APPDATA%\WORKAUDIT\.machinekey`, `.dbkey` |

---

## Appendix B: Database Schema

The Oracle database contains these primary tables:

- `users` - User accounts and credentials
- `sessions` - Active user sessions
- `documents` - Document metadata
- `audit_log` - Complete audit trail
- `assignments` - Workflow assignments
- `notes` - Document annotations
- `config_*` - System configuration
- `app_settings` - Application settings

**NEVER** manually edit the database unless directed by vendor support.

---

## Appendix D: Multi-PC shared Oracle

Use this when **more than one workstation** runs WorkAudit against the **same Oracle user/schema**.

### Required

1. **Same app version** on every PC (avoid migration drift).
2. **`WORKAUDIT_ORACLE_CONNECTION`** (machine or user env) identical on all clients, or equivalent connection to the same schema.
3. **`WORKAUDIT_BASE_DIR`** set to a **UNC path or shared drive** so `documents.file_path` resolves on every machine. If unset, each PC defaults to its own Documents folder and peers will see **missing file** errors for attachments.

### Scheduled jobs (backup / email report)

- **`scheduler_leader_election_enabled`** (default `true` in DB after migration 53): only one instance holds the lease for `scheduled_backup` and `scheduled_report` at a time.
- **`scheduled_report_last_run_date`**: `yyyy-MM-dd` of the last successful scheduled report (prevents duplicate daily PDF/email across PCs).
- To run timers on **every** client without coordination (not recommended), set `scheduler_leader_election_enabled` to `false` in Control Panel / `app_settings`.

### RBAC

- Roles and sessions live in Oracle; each login creates a new session row.
- See [`MULTI_PC_RBAC_AUDIT.md`](MULTI_PC_RBAC_AUDIT.md) for permission boundaries and service-layer gaps.

### Incident: duplicate backups or “skipped” jobs

1. Check `%APPDATA%\WORKAUDIT\Logs\` for `Scheduled backup skipped: another instance holds the scheduler lock`.
2. Verify migration **53+** applied: `SELECT MAX(version) FROM workaudit_migrations;`
3. Inspect `SELECT * FROM workaudit_scheduler_locks;` — stale `lease_until` should expire within `scheduler_lock_lease_minutes` unless a PC crashed mid-backup (wait for lease expiry or clear row in maintenance window only).

---

## Appendix C: Firewall Rules (if Central Sync Enabled)

```powershell
# Allow outbound HTTPS to central server
New-NetFirewallRule -DisplayName "WorkAudit Central Sync" `
  -Direction Outbound `
  -Protocol TCP `
  -LocalPort Any `
  -RemotePort 443 `
  -RemoteAddress "10.x.x.x" `
  -Action Allow

# Allow outbound SMTP (if email reporting enabled)
New-NetFirewallRule -DisplayName "WorkAudit SMTP" `
  -Direction Outbound `
  -Protocol TCP `
  -RemotePort 587,465 `
  -Action Allow
```

---

**Document Version**: 1.0.2  
**Effective Date**: 2026-05-04  
**Review Date**: 2026-11-04 (6 months)
