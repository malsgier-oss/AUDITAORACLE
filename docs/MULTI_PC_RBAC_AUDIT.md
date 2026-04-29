# Multi-PC Shared Oracle — RBAC and mutation audit

This document records how authorization applies when several Windows desktops use the **same Oracle schema** (`WORKAUDIT_ORACLE_CONNECTION`).

## Model

- **Oracle** stores users, roles, sessions, documents, assignments, audit log, and `app_settings`.
- **Each PC** runs a separate process with its own DI container (`ServiceContainer`) and in-memory session (`SessionService` / `AppConfiguration.CurrentUser*`).
- **RBAC enforcement** for document and archive actions is implemented in `PermissionService` + `AuthorizationService` (throwing `UnauthorizedAccessException` when used). Views such as `WorkspaceView` and `ArchiveView` call `IPermissionService` before destructive actions (e.g. `CanEditDocument`, `HasPermission(Permissions.ArchiveCreate)`).

## Gaps and responsibilities

| Area | Server-side enforcement | Notes |
|------|-------------------------|--------|
| Document CRUD from UI | Partial | Workspace/Archive paths check permissions; direct use of `IDocumentStore` / `DocumentAssignmentService` from new code must call `IAuthorizationService` or `IPermissionService`. |
| `DocumentAssignmentService.Assign` | No role check | Callers (UI) must ensure only authorized roles invoke assignment APIs. |
| Scheduled backup / report | Leader election + DB | `scheduler_leader_election_enabled` (default on) + `workaudit_scheduler_locks`; scheduled report also uses `scheduled_report_last_run_date` in `app_settings`. |
| File attachments | Path + env | Without `WORKAUDIT_BASE_DIR` pointing to shared storage, `file_path` may exist on one PC only. |

## Recommended hardening (future)

1. Add `IPermissionService` checks to `DocumentAssignmentService` mutating methods.
2. Optionally pass `expectedUpdatedAtUtc` into `DocumentStore.UpdateResult` from the UI after each load to surface concurrency conflicts to users.
3. Centralize “command” APIs that combine permission + storage for automation/testing.

## Related code

- `Core/Security/PermissionService.cs`, `AuthorizationService.cs`
- `Views/WorkspaceView.xaml.cs`, `Views/ArchiveView.xaml.cs`
- `Core/Assignment/DocumentAssignmentService.cs`
- `Storage/DocumentStore.cs` — `UpdateResult(Document, DateTime? expectedUpdatedAtUtc)`
- `Storage/Oracle/SchedulerLockStore.cs`, migrations v53+
