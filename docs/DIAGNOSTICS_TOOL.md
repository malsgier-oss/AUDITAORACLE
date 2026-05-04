# Diagnostics & Health Monitoring (Control Panel)

## Overview

The **Diagnostics** tab in the Control Panel (Manager+ / full control panel) provides a consolidated view of application health, log-derived errors, workflow anomalies, background services, database metrics, configuration validation, recent activity, and performance timings.

## Access

- Open **Control Panel** from the main menu.
- Users with **Manager** role or higher see the **Diagnostics** tab.
- Users with preferences-only access do **not** see this tab.

## What is monitored

| Area | Description |
|------|-------------|
| **Overview** | High-level stats, **24h error/warning trend** (UTC hours), recent activity (last import, OCR, backup, report, logins), plus **missing/orphan file counts**, **OCR error count (7d logs)**, and **import-related error count (24h, heuristic)**. |
| **Health Checks** | Results from the built-in health check (database, storage, memory, disk). |
| **Error Log** | Parsed lines from `workaudit-*.log` with level, period, **component** (from last snapshot’s error sources), message search, and **Copy details**. |
| **Workflow Issues** | Heuristics: stale Drafts, missing OCR on classified documents, stale “Ready for Audit”, overdue assignments, **merge queue backlog** (depth + **oldest unfinished enqueue UTC**), **orphaned attachment files** (on-disk sample vs `documents.file_path`), missing files (sampled). |
| **Services** | Folder watch, scheduled backup, scheduled reports, **merge queue** (pending count, oldest enqueue age in details). |
| **Database** | Connectivity probe, schema version, row counts for key tables (when available), **DB-related error lines in logs (24h)**, and optional **`v$session`** row counts when the Oracle user may select those views. |
| **Configuration** | Validation of base path, Oracle connection string, optional OCR/SMTP/backup paths, active branches and document types. |
| **Performance** | Lines from `performance-*.log` (PERF markers) with a minimum duration filter. |

## Log file locations

Logs are created by Serilog (see `LoggingService`). By default:

- **Directory:** `%AppData%\WORKAUDIT\Logs\`
- **Main log:** `workaudit-YYYYMMDD.log`
- **Performance log:** `performance-YYYYMMDD.log`

## Export

- **Export Report** saves a snapshot as **JSON** (structured) or **text** (readable).
- Use **Refresh Now** before export if you need the latest data.
- JSON may include paths and system metadata; review before sharing externally.

## Caching

Diagnostic snapshots are cached for about **60 seconds** to reduce load. Use **Refresh Now** or enable **Auto-refresh (30s)** to update more frequently.

## Troubleshooting

- **Empty error log:** Confirm log files exist under the log directory and the selected time range.
- **Database metrics incomplete:** Extended queries require database permissions; warnings appear if a query fails.
- **Workflow “missing file”:** Paths are resolved relative to the configured base directory; verify storage and moved installs.

## Related code

- Services: `Core/Services/DiagnosticsService.cs`, `ErrorLogAnalyzer.cs`, `WorkflowMonitor.cs`, `DatabaseMonitor.cs`, etc.
- Models: `Domain/Diagnostics.cs`
- UI: `Views/Admin/ControlPanelWindow.xaml` (Diagnostics tab)
