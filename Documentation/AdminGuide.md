# WorkAudit Administrator Guide

## Overview

This guide covers administrative tasks: configuration, user management, KPI targets, scheduled reports, and assignment management.

---

## Control Panel

Access via **Admin → Control Panel** (Manager+ role required).

### General Tab

- **Database path:** Location of the SQLite database.
- **Log level:** Debug, Information, Warning, Error.
- **Confidence threshold:** Minimum OCR confidence for auto-classification.

### Workflow Tab

#### Report Labels

- **Report Language:** English or Arabic for report section headers.

#### Scheduled Reports

- **Enable scheduled reports:** When checked, app generates reports at the configured time when running.
- **Report type:** Performance, Executive Summary, Branch Summary, or Issues & Focus.
- **Time:** HH:mm (e.g., 08:00).
- **Save to:** Folder path for generated PDFs (blank = temp folder).
- **Email to:** Comma-separated email addresses for scheduled report delivery.
- **SMTP host:** Hostname (e.g., smtp.gmail.com).
- **SMTP port:** 587 (TLS) or 465 (SSL).
- **SMTP user / password:** Credentials for authenticated SMTP.

**Note:** Email is sent only if both "Email to" and "SMTP host" are configured.

#### Webcam

- **Default to Auto mode:** Open webcam with Auto capture selected.

### KPI Targets Tab

Configure targets for Performance reports:

- **Clearing Rate:** Target % (e.g., 80%).
- **Throughput:** Documents per day (e.g., 50).
- **Issue Rate:** Target % (e.g., 5%).

Set per branch/section or bank-wide. Use **Reset** to restore defaults.

### Backup Tab

- **Enable automatic backups:** Enable scheduled backups.
- **Backup interval (hours):** How often to run.
- **Backups to retain:** Number of old backups to keep.
- **Include document files:** Include document files in backup.

---

## User Management

Access via **Admin → User Management**.

1. **Add user:** Click Add, enter username, display name, role, password.
2. **Edit user:** Select user, click Edit.
3. **Reset password:** Use Password Reset.
4. **Roles:** Auditor (view), Manager (assign, approve, configure), Admin (full access).

---

## Assignment Management

Access via **Admin → Assignment Management**.

### List Tab

- View all assignments across users.
- Filter by **Assigned to** and **Status**.
- **Reassign:** Select one assignment, click Reassign, choose new user.
- **Drag-and-drop reassign:** Drag an assignment row and drop on a user in the right panel.
- **Cancel:** Cancel Pending or In Progress assignments.
- **Bulk Assign:** Select multiple, cancel current assignments, then assign to a new user.

### Calendar Tab

- View assignments by due date.
- Navigate months with Prev/Next.
- Click **Today** to jump to current month.

### Analytics Tab

- Workload distribution chart by user.
- Filter by **Active** (Pending + In Progress) or **All** (including Completed).
- Use to balance workload across team members.

---

## Report Attestation

For PDF reports:

1. **Generate** report → Status: Generated.
2. **Review** (Manager+): Click Review in attestation panel.
3. **Approve** (Manager+): Click Approve.

Sign-offs are recorded with user and timestamp.

---

## Branches & Document Types

- **Admin → Branches:** Add/edit branches.
- **Admin → Document Types:** Configure document type classifications.

---

## Audit Log

- **Admin → Audit Log:** View all system actions (login, report generation, assignments, etc.).
- Filter by date, action, category, user.

---

## Security Notes

- SMTP passwords are stored in app settings. Restrict Control Panel access.
- Use strong passwords for database and admin accounts.
- Regular backups are recommended.

---

*Last updated: 2026-02-06*
