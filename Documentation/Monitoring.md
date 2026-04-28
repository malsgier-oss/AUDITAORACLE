# WorkAudit Monitoring Guide

## Overview

Guidance for monitoring application health, performance, and usage.

---

## Logs

### Location

- **Default:** `%APPDATA%\WORKAUDIT\Logs\`
- **Format:** `workaudit-YYYYMMDD.log` (rolling daily)

### Log Levels

- **Debug:** Detailed troubleshooting.
- **Information:** Normal operations.
- **Warning:** Recoverable issues.
- **Error:** Failures requiring attention.

### What to Monitor

| Log Message | Action |
|-------------|--------|
| `Failed to generate report` | Check disk space, permissions; verify data. |
| `Scheduled report email failed` | Verify SMTP settings; test SMTP connectivity. |
| `Database migration failed` | Backup database; check schema; contact support. |
| `Document assignment failed` | Check user exists; verify document store. |

---

## Performance

### Targets

- **Report generation:** < 5 seconds for 10K documents.
- **Executive Dashboard:** < 3 seconds load.
- **Search:** < 2 seconds for typical queries.

### Indicators

- Slow report generation → Check database size, indexes.
- High memory usage → Check for large document sets; consider archiving.
- UI freezing → Check for long-running operations (reports, imports).

---

## Database

### Size

- Monitor `workaudit.db` size.
- Archive old documents to control growth.
- Use retention settings in Control Panel.

### Integrity

- **Control Panel → System:** Shows database path and migration status.
- Run integrity checks periodically (SQLite: `PRAGMA integrity_check`).

---

## Scheduled Reports

### Verification

- Check logs for `Scheduled report completed` at expected time.
- If email enabled: verify `Report emailed successfully`.
- Check output folder for generated PDFs.

### Common Issues

- **Skipped:** App not running at scheduled time.
- **Email failed:** SMTP host/port/credentials incorrect.
- **No output:** Check "Save to" path exists and is writable.

---

## Usage Metrics (Manual)

- **Audit Log:** Query by action type (ReportGenerated, DocumentAssigned, etc.).
- **Report History:** Count of reports generated per user/period.
- **Assignments:** Count by status (Pending, In Progress, Completed).

---

## Alerts (Recommendations)

1. **Disk space:** Monitor `%APPDATA%\WORKAUDIT\` and database folder.
2. **Log errors:** Set up log aggregation or alert on `ERROR` or `WARNING` lines.
3. **Backup:** Verify backup jobs complete; check backup folder size.
4. **Scheduled reports:** Alert if no report generated on expected day.

---

## Health Check Checklist

- [ ] Logs contain no errors in last 24 hours.
- [ ] Reports generate successfully.
- [ ] Scheduled reports run (if enabled).
- [ ] Backup completed (if enabled).
- [ ] Database size within expected range.
- [ ] Users can log in and assign documents.

---

*Last updated: 2026-02-06*
