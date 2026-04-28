# WorkAudit Quick Reference Guide

## 🚀 For Deployment Team - Fast Access

---

## Critical Pre-Deployment Actions

### 1️⃣ Security Audit (CRITICAL - DO NOT SKIP)
```
⚠️ MUST complete external security audit before production
   Cost: $5,000-10,000
   Duration: 2-3 weeks
   Contact: External security firm
```

### 2️⃣ Code Signing Certificate (REQUIRED)
```
✅ Obtain EV Code Signing Certificate
   Provider: DigiCert, Sectigo, or GlobalSign
   Cost: $400-600/year
   Required for: Windows SmartScreen approval

   Command to sign MSI:
   signtool sign /f certificate.pfx /p password /t http://timestamp.digicert.com WorkAudit-Setup.msi
```

### 3️⃣ Test Installation (REQUIRED)
```
✅ Test on clean machines:
   - Windows 10 (version 1809+)
   - Windows 11
   - As standard user (should prompt for admin)
   - Verify .NET 8.0 runtime check
```

---

## Installation Commands (Per Branch)

### Environment Setup (Optional - Before First Launch)
```powershell
# Customize for each branch
$branch = "Branch01"
$baseDir = "D:\WorkAuditData"

[System.Environment]::SetEnvironmentVariable("WORKAUDIT_BASE_DIR", $baseDir, "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_USERNAME", "${branch}_admin", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_EMAIL", "${branch}_admin@bank.com", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_BRANCH", $branch, "Machine")
```

### Install via MSI
```powershell
# Silent installation
msiexec /i WorkAudit-Setup.msi /quiet /norestart /l*v install.log

# Interactive installation
# Right-click WorkAudit-Setup.msi → Run as Administrator
```

### Install via ZIP
```powershell
# Extract deployment package
Expand-Archive WorkAudit-v1.0.0-Deployment.zip -DestinationPath C:\Temp\WorkAudit

# Run installer
cd C:\Temp\WorkAudit
.\install.bat
# (Right-click → Run as Administrator)
```

---

## Post-Installation Checklist (Per Branch)

```
✅ Launch WorkAudit
✅ Save temporary admin password (shown on first launch)
✅ Login and change admin password immediately
✅ Configure Settings → Control Panel:
   ✅ Base Directory: D:\WorkAuditData (or drive with space)
   ✅ Branch name
   ✅ Document types (add relevant types)
✅ Create standard users (Manager, Auditor, Viewer roles)
✅ Configure scheduled backups:
   ✅ Location: \\fileserver\backups\workaudit\branch01\
   ✅ Time: 02:00 (2 AM)
   ✅ Encryption: Enabled
   ✅ Password: [Store in password manager]
✅ Test workflow: Import → Process → Metadata → Assign → Approve
✅ Test backup: Create manual backup, verify file created
✅ Document admin credentials (password manager)
```

---

## Troubleshooting (Quick Fixes)

### Application Won't Start
```powershell
# 1. Verify .NET Runtime
dotnet --list-runtimes | Select-String "WindowsDesktop.App 8.0"

# 2. Check Event Viewer
eventvwr.msc
# Windows Logs → Application → Filter "WorkAudit"

# 3. Check logs
notepad "$env:APPDATA\WORKAUDIT\Logs\workaudit-$(Get-Date -Format yyyyMMdd).log"

# 4. Run as admin
Start-Process "C:\Program Files\WorkAudit\WorkAudit.exe" -Verb RunAs
```

### Database Locked Error
```powershell
# Kill all instances
Get-Process WorkAudit -ErrorAction SilentlyContinue | Stop-Process -Force

# Remove lock files
Remove-Item "$env:APPDATA\WORKAUDIT\*.db-shm" -Force -ErrorAction SilentlyContinue
Remove-Item "$env:APPDATA\WORKAUDIT\*.db-wal" -Force -ErrorAction SilentlyContinue
```

### Out of Disk Space
```powershell
# Check space
Get-PSDrive C | Select-Object Used,Free

# Clean temp files
Remove-Item "$env:TEMP\WorkAudit_*" -Recurse -Force -ErrorAction SilentlyContinue

# Archive old documents (via UI)
# Settings → Data Management → Archive documents >90 days old
```

### Backup Failed
```powershell
# Test network share
Test-Path \\fileserver\backups\workaudit\branch01\

# Test write permission
New-Item \\fileserver\backups\workaudit\branch01\test.txt -ItemType File -Force

# Check backup logs
notepad "$env:APPDATA\WORKAUDIT\Logs\workaudit-$(Get-Date -Format yyyyMMdd).log"
# Search for: "Backup failed"
```

---

## Emergency Contacts

### Deployment Issues
- **Central IT**: it-support@yourbank.com | +XXX-XXX-XXXX (24/7)

### Security Incidents
- **Security Team**: security@yourbank.com | +XXX-XXX-XXXX (24/7)

### Critical Bugs
- **Development Team**: dev-team@yourbank.com (business hours)

---

## Key File Locations (Quick Reference)

| Item | Path |
|------|------|
| **Application** | `C:\Program Files\WorkAudit\` |
| **Database** | Oracle 19c via `WORKAUDIT_ORACLE_CONNECTION` |
| **Logs** | `%APPDATA%\WORKAUDIT\Logs\` |
| **Attachments** | `%APPDATA%\WORKAUDIT\attachments\` |
| **Backups** | `[Network share or local path]` |
| **Encryption Keys** | `%APPDATA%\WORKAUDIT\.machinekey` |

---

## Rollback Procedure (If Deployment Fails)

```powershell
# Uninstall
msiexec /x {ProductCode} /quiet

# OR use uninstall.bat
cd C:\Temp\WorkAudit
.\uninstall.bat

# Restore previous system (if upgrading)
# User data in %APPDATA%\WORKAUDIT is preserved
```

---

## Performance Benchmarks (Expected)

| Operation | Expected Time | Alert If Exceeds |
|-----------|---------------|------------------|
| Document import | 2-5 seconds | 10 seconds |
| Bulk import (100 docs) | 5-10 minutes | 20 minutes |
| Database query | <1 second | 5 seconds |
| PDF generation | 15-30 seconds | 60 seconds |
| Backup (10 GB) | 3-5 minutes | 15 minutes |

---

## Deployment Timeline (Template)

### Week -2: Preparation
- [ ] Security audit completed
- [ ] All testing completed
- [ ] MSI signed with certificate
- [ ] Deployment package prepared

### Week -1: Pilot
- [ ] Deploy to 1 test branch
- [ ] Train 3-5 users
- [ ] Monitor for issues

### Week 0: Deployment Begins
- [ ] **Monday**: Branch 1-2
- [ ] **Wednesday**: Branch 3-4
- [ ] **Friday**: Branch 5-6

### Week 1+: Remaining Branches
- [ ] Deploy 1-2 branches per day
- [ ] Monitor health daily
- [ ] Collect feedback

---

## Success Indicators

✅ **Deployment Successful** if:
- Zero data loss
- <5 critical errors per branch (first week)
- All backups successful
- Users can complete full workflow
- Average document processing <5 seconds

⚠️ **Investigate** if:
- >5 critical errors in first 24 hours
- Backup failures
- Performance >2x slower than expected
- User complaints about usability

🛑 **STOP and Rollback** if:
- Data loss or corruption
- Security breach
- Application unusable (constant crashes)
- Regulatory compliance violated

---

## Build Commands (For Developers)

### Standard Build
```powershell
dotnet build WorkAudit.csproj -c Release
```

### With Tests
```powershell
dotnet build && dotnet test WorkAudit.Tests\WorkAudit.Tests.csproj
```

### Create Deployment Package
```powershell
.\scripts\Create-DeploymentPackage.ps1 -Version "1.0.0"
```

### Build MSI Installer
```powershell
cd installer
wix build WorkAudit.wxs -arch x64 -o ..\bin\Release\WorkAudit-Setup.msi
```

---

## Configuration Quick Reference

### SMTP Settings (for Email Reports)
```
Host: smtp.yourbank.com
Port: 587 (TLS) or 465 (SSL)
User: workaudit@yourbank.com
Password: [Will be encrypted automatically]
```

### Backup Settings
```
Enabled: Yes
Schedule: Daily at 02:00
Location: \\fileserver\backups\workaudit\[branch]\
Encryption: Yes
Password: [Store in password manager]
Retention: 30 days
```

### Update Settings (Future)
```
Update Server: https://updates.yourbank.com/workaudit
Check Interval: Daily
Auto-Update: Disabled (manual approval required)
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-02-22 | Initial production release |

---

**Last Updated**: 2026-02-22  
**For**: Deployment Team, IT Staff  
**Keep This Handy During Deployment**
