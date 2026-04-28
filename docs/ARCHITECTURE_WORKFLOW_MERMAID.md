# WorkAudit Architecture and Workflow (Implementation-Based)

Implementation-based repo map completed from the runtime code paths in the desktop app, storage layer, and OCR pipeline.

## Component Summary

- **Entry/Bootstrap**
  - `App.xaml` + `App.xaml.cs` (`App_Startup`) initializes logging/theme, resolves base/data paths, calls `ServiceContainer.Initialize`, runs `IMigrationService.Migrate`, ensures admin user, then loops through `ShowLoginAndContinue`.
  - `Core/Services/ServiceContainer.cs` wires DI for UI-facing services, OCR, storage, compliance, reports, security, and scheduled jobs.

- **Authentication + Session**
  - `Dialogs/LoginDialog.xaml.cs` calls `ISessionService.LoginAsync`.
  - `Core/Security/SessionService.cs` handles lockout rules, emergency admin codes, session token creation, inactivity timeout, session invalidation.
  - `MainWindow.xaml.cs` inactivity timer calls `CheckInactivityTimeoutAsync`; logout triggers close/re-login loop in `App.xaml.cs`.

- **UI Layer (WPF)**
  - Shell: `MainWindow.xaml.cs` (`SwitchToView`) hosts role-based pages.
  - Input pipeline: `Views/InputView.xaml.cs` + `Views/ImportView.xaml.cs`.
  - Processing pipeline: `Views/ProcessingView.xaml.cs`.
  - Workspace operations: `Views/WorkspaceView.xaml.cs`.
  - Archive/search: `Views/ArchiveView.xaml.cs`.
  - Reporting: `Views/ReportsView.xaml.cs` + `Core/Reports/ReportService.cs`.
  - Admin windows under `Views/Admin/*`.
  - `Views/SearchView.xaml.cs` exists but has no runtime instantiation found (`new SearchView(...)` not found).

- **Import + Processing Services**
  - `Core/Import/ImportService.cs` imports image/PDF docs, inserts into `DocumentStore`, enqueues OCR as needed.
  - `Core/Services/ProcessingMergeQueueService.cs` serializes merge jobs, exports merged PDF, re-imports via `ImportSinglePdfDocumentAsync`, removes source docs/files.

- **OCR + Text Extraction**
  - `Core/TextExtraction/SelectingOcrService.cs` delegates to `TesseractOcrService`.
  - `Core/TextExtraction/TesseractOcrService.cs` does background queue OCR, multipass preprocessing, layout extraction, fallback retry, structured field extraction, then `IDocumentStore.Update`.
  - `Core/Helpers/DocumentWorkspaceOcr.cs` decides when to enqueue OCR from workspace/import status transitions.
  - Preview OCR overlay uses `IWindowsPreviewOcrLayout` bound to `TesseractPreviewOcrLayoutService`.

- **Data Layer (Oracle + file system)**
  - `Storage/DocumentStore.cs` is core query/update path (`ListDocuments`, `FullTextSearch`, `UpdateStatus`, `Update`, `Delete`, `GetStats`).
  - `Storage/MigrationService.cs` runs schema migrations (`Migration_001` ... `Migration_049`), includes FTS migrations.
  - Other stores: `UserStore`, `AuditLogStore`, `NotesStore`, `ConfigStore`, assignment/report stores, etc.
  - Files are stored in branch/section/type folders under configured base directory.

- **Compliance/Archive**
  - `Core/Compliance/ArchiveService.cs` sets archived status, retention expiry, immutability hash, and audit trail.
  - `ArchiveView` drives legal hold, custodians, disposal workflow, export, chain-of-custody.

- **External/Library Dependencies (runtime)**
  - OCR: `Tesseract`, `OpenCvSharp4`.
  - PDF/text/image: `PdfPig`, `PDFtoImage`, `PdfiumViewer.Net.WPF`, `PDFsharp`, `QuestPDF`.
  - Storage: `Oracle.ManagedDataAccess.Core` (Oracle provider).
  - UI/infra: `WebView2`, `OxyPlot`, `Serilog`, DI libs.
  - Update endpoint client: `AutoUpdateService` uses `HttpClient` to fetch `version.json` and zip packages.

## MAIN Architecture Diagram

```mermaid
flowchart TD
  A[App Startup]
  B[Main Window]
  C[Input View]
  D[Processing View]
  E[Workspace View]
  F[Archive View]
  G[Reports View]

  H[Service Container]
  I[Session Service]
  J[Import Service]
  K[OCR Service]
  L[Archive Service]
  M[Report Service]
  N[Merge Queue Service]

  O[Document Store Oracle]
  P[User Store]
  Q[Other Stores]
  R[Migration Service]
  S[File System]

  T[Tesseract]
  U[OpenCV]
  V[PDF Libraries]
  W[Update Server]

  A --> H
  A --> I
  A --> B

  B --> C
  B --> D
  B --> E
  B --> F
  B --> G

  C --> J
  D --> N
  D --> O
  E --> K
  F --> L
  G --> M

  J --> O
  J --> S
  I --> P
  M --> O
  M --> Q
  L --> O
  O --> R
  O --> S

  K --> T
  K --> U
  K --> O
  J --> V
  N --> V
  M --> V

  A --> W
```

## WORKFLOW Diagram (Detailed Runtime Path)

```mermaid
flowchart TD
  A[App Startup]
  B[Init Services and Migrate]
  C[Login Dialog]
  D[Session Service Login]
  E[Main Window]

  F[Import Action]
  G[Import Service]
  H[Document Store Insert]
  I[OCR Enqueue]

  J[Processing View]
  K[Merge Queue]
  L[Merge Export and Reimport]
  M[Set ReadyForAudit]

  N[Workspace View]
  O[Workspace Search]
  P[Archive Action]
  Q[Archive Service]

  R[Archive View Search]
  S[Archive Export Hold Chain]

  T[Reports View Generate]
  U[Validate Config]
  V[Report Service]
  W[Save History and Attestation]

  X[Error and Fallback Paths]

  A --> B --> C --> D --> E
  E --> F --> G --> H --> I
  E --> J --> K --> L
  J --> M
  E --> N --> O
  N --> P --> Q
  E --> R --> S
  E --> T --> U --> V --> W
  G --> X
  N --> X
  R --> X
  T --> X
```

## OCR Pipeline Diagram (Focused)

```mermaid
flowchart TD
  A[OCR Trigger]
  B[Enqueue OCR Task]
  C[Tesseract Queue Worker]
  D{File Type}
  E[Render PDF Page to PNG]
  F[Preprocess Variants]
  G[Run Tesseract Passes]
  H[Merge and Normalize Text]
  I{Fallback Retry Needed}
  J[Fallback Variants and Retry]
  K[Arabic Normalize]
  L[Bilingual Organize Optional]
  M[Structured Data Extract]
  N[Apply Structured Fields]
  O[Document Store Update]
  P[OCR Completed Event]
  X[Error Fallback Paths]

  A --> B --> C --> D
  D -->|PDF| E --> F
  D -->|Image| F
  F --> G --> H --> I
  I -->|Yes| J --> K
  I -->|No| K
  K --> L --> M --> N --> O --> P
  C --> X
  F --> X
  G --> X
  E --> X
```

## Document Lifecycle State Diagram

```mermaid
stateDiagram-v2
  [*] --> Imported: ImportService import actions
  Imported --> Draft: DocumentStore InsertResult

  Draft --> Processing: ProcessingView queue view
  Processing --> Draft: Set type and section only

  Processing --> ReadyForAudit: ProcessingView FinishBtn and UpdateStatus
  ReadyForAudit --> Reviewed: Workspace set status Reviewed
  ReadyForAudit --> Issue: Workspace set status Issue
  ReadyForAudit --> Cleared: Workspace set status Cleared
  Reviewed --> ReadyForAudit: Workspace set status ReadyForAudit
  Issue --> ReadyForAudit: Workspace set status ReadyForAudit
  Cleared --> ReadyForAudit: Workspace set status ReadyForAudit

  Draft --> Archived: Workspace MoveToArchive and ArchiveService
  Reviewed --> Archived: Workspace MoveToArchive and ArchiveService
  Issue --> Archived: Workspace MoveToArchive and ArchiveService
  Cleared --> Archived: Workspace MoveToArchive and ArchiveService
  ReadyForAudit --> Archived: Workspace MoveToArchive and ArchiveService

  Draft --> Deleted: Processing delete action
  Processing --> Deleted: Processing delete action
  Reviewed --> Deleted: Processing delete action
  Issue --> Deleted: Processing delete action
  Cleared --> Deleted: Processing delete action
  ReadyForAudit --> Deleted: Processing delete action
  Archived --> Deleted: Archive delete action

  Processing --> Imported: Merge queue reimport merged PDF
  Processing --> Deleted: Merge queue deletes source docs
```

## Notes on unclear/unknown points

- `SearchView` has archive/search logic (including FTS path), but no direct instantiation/navigation path was found, so runtime usage is **unknown**.
- OCR engine selection wrapper exists (`SelectingOcrService`), but runtime wiring currently points to Tesseract path; alternative engine runtime switching is **unknown/not active** from current DI wiring.

## Recent Fixes (2026-04-21)

### Document Classification Workflow Hardening

Critical fixes were implemented to prevent document classification failures:

1. **Transaction Support** - Added transaction-aware methods to `DocumentStore` for atomic operations
2. **File Rollback Capability** - `FileRenameService` can now roll back file moves when database operations fail
3. **Fail-Fast Logic** - Processing stops immediately if either type or section update fails (prevents partial updates)
4. **Exponential Backoff Retry** - Improved from 4×75ms to 6 retries with backoff (50ms→1600ms) for Oracle lock/busy conditions
5. **Pre-flight Validation** - Checks file locks, disk space, and permissions before attempting operations
6. **Automatic Rollback** - Files are automatically moved back if database updates fail after file move
7. **Better Error Reporting** - Users see detailed failure reasons instead of silent failures

**Result:** Classification operations are now robust, consistent, and transparent. File system and database remain synchronized even during failures.

**Documentation:** See `docs/CLASSIFICATION_WORKFLOW_FIX.md` and `docs/CLASSIFICATION_FIX_SUMMARY.md` for complete details.
