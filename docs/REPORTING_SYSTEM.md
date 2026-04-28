# WorkAudit Reporting System Documentation

## Overview

The WorkAudit Reporting System is a comprehensive, enterprise-grade reporting framework designed for bank-level audit and document management. This system provides organized report generation, validation, progress tracking, draft editing, and advanced search capabilities.

## Architecture

### Core Components

1. **Report Generation** (`IReportService`, `ReportService`)
   - Generates reports in multiple formats (PDF, Excel, CSV)
   - Async generation with progress reporting and cancellation
   - Supports 11+ report types

2. **Report Organization** (`IReportFileOrganizer`)
   - Creates structured output folders (Year/Month or Year/Quarter)
   - Generates standardized filenames with timestamps
   - Handles per-branch/section exports with ZIP support

3. **Validation** (`IReportValidationService`)
   - Pre-generation configuration validation
   - Date range validation
   - Format compatibility checking
   - Document count preview

4. **Draft System** (`IReportDraftService`, `IReportDraftStore`)
   - Create editable HTML drafts before final export
   - WebView2-based WYSIWYG editor
   - Draft metadata: title, tags, notes, finalization status
   - Export drafts to final formats

5. **History & Search** (`IReportHistoryStore`, `IReportHistoryFilterService`)
   - Comprehensive report history with enhanced metadata
   - Advanced filtering: tags, date range, report type, user, purpose
   - Full-text search across metadata fields
   - Version tracking with parent-child relationships

6. **Comparison** (`IReportComparisonService`)
   - Compare report configurations across versions
   - Track metadata changes
   - Get all versions of a report

7. **Bulk Operations** (`IReportBulkExportService`)
   - Export multiple reports to ZIP archive
   - Copy reports to directory with metadata CSV
   - Batch operations on filtered report sets

## Data Models

### ReportConfig
Primary configuration for report generation:
- `ReportType`: Type of report (Daily, Branch, Executive, etc.)
- `DateFrom`, `DateTo`: Date range
- `Format`: Output format (PDF, Excel, CSV)
- `Branch`, `Section`, `Status`, `DocumentType`: Filters
- `IncludeCharts`, `ExportPerBranch`, `ZipPerBranch`: Options
- `Watermark`, `ReportTemplate`: Presentation settings

### ReportHistory
Enhanced history tracking:
- Basic: `Id`, `Uuid`, `UserId`, `Username`, `ReportType`, `FilePath`, `GeneratedAt`
- Enhanced: `Tags`, `Purpose`, `Description`, `Version`, `ParentReportId`, `AppVersion`
- `ConfigJson`: Full serialized configuration for reproducibility

### ReportDraft
Editable report drafts:
- `Title`, `Notes`, `Tags`: User metadata
- `DraftFilePath`: Path to HTML draft file
- `IsFinalized`: Ready for export flag
- `ExportedReportHistoryId`: Link to final report

### ReportProgress
Real-time progress information:
- `PercentComplete`: 0-100 progress indicator
- `Stage`: Current generation stage
- `ItemsProcessed`, `TotalItems`: Item counts
- `Elapsed`: Time elapsed since start

## Features

### Phase 1: Foundation (✅ Complete)
- **Organized folder structure** with year/month or year/quarter organization
- **Standardized filenames** with type, date range, and timestamps
- **Pre-generation validation** with errors and warnings
- **Enhanced history metadata**: tags, purpose, description, version tracking
- **Full config reproducibility** with JSON serialization

### Phase 2: Rich Feedback & Editor (✅ Complete)
- **Progress reporting** with IProgress<ReportProgress>
- **Cancellation support** with CancellationToken
- **Report drafts** for editing before final export
- **WebView2 editor** for WYSIWYG HTML editing
- **Draft workflow**: create → edit → finalize → export

### Phase 3: Advanced Search & Filtering (✅ Complete)
- **Multi-criteria filtering**: tags, dates, types, users, purpose
- **Full-text search** across all metadata fields
- **Report comparison** between versions
- **Bulk export** to ZIP with metadata CSV
- **Version tracking** with parent-child relationships

### Phase 4: Performance & Scale (Planned)
- Report generation caching
- Incremental report updates
- Large dataset optimization
- Parallel report generation

### Phase 5: Polish & Documentation (Planned)
- User guide and tutorials
- Admin configuration guide
- API documentation
- Performance tuning guide

## Usage Examples

### Basic Report Generation

```csharp
var reportService = ServiceContainer.GetService<IReportService>();

var config = new ReportConfig
{
    ReportType = ReportType.DailySummary,
    DateFrom = DateTime.Today.AddDays(-7),
    DateTo = DateTime.Today,
    Format = ReportFormat.Pdf,
    IncludeCharts = true
};

// Synchronous
var path = reportService.Generate(config);

// Asynchronous with progress
var progress = new Progress<ReportProgress>(p => 
{
    Console.WriteLine($"{p.PercentComplete}%: {p.Stage}");
});

var cts = new CancellationTokenSource();
var path = await reportService.GenerateAsync(config, progress, cts.Token);
```

### Creating and Editing Drafts

```csharp
var draftService = ServiceContainer.GetService<IReportDraftService>();

// Create draft
var draft = draftService.CreateDraft(config, userId, username);

// Update metadata
draft.Title = "Monthly Audit Report - January 2026";
draft.Tags = "audit, monthly, compliance";
draft.Purpose = "Regulatory submission";
draftService.UpdateDraft(draft);

// Update content
var htmlContent = "<html>...</html>";
draftService.UpdateDraftContent(draft.Id, htmlContent);

// Export to final format
var finalPath = draftService.ExportDraft(draft.Id, ReportFormat.Pdf);
```

### Advanced Filtering and Search

```csharp
var filterService = ServiceContainer.GetService<IReportHistoryFilterService>();

var filter = new ReportHistoryFilter
{
    FromDate = DateTime.Today.AddMonths(-3),
    ToDate = DateTime.Today,
    Tags = new[] { "compliance", "audit" },
    ReportTypes = new[] { "DailySummary", "ExecutiveSummary" },
    SearchText = "January",
    SortBy = "date_desc",
    Limit = 50
};

var results = filterService.ApplyFilters(filter);
```

### Bulk Export

```csharp
var bulkService = ServiceContainer.GetService<IReportBulkExportService>();
var filterService = ServiceContainer.GetService<IReportHistoryFilterService>();

// Get filtered reports
var reports = filterService.ApplyFilters(filter);

// Export to ZIP
var zipPath = bulkService.ExportToZip(reports, "compliance_reports_2026.zip");

// Or copy to directory
var copied = bulkService.CopyReportsToDirectory(reports, @"C:\Exports\Reports");
```

### Report Comparison

```csharp
var comparisonService = ServiceContainer.GetService<IReportComparisonService>();

// Get all versions
var versions = comparisonService.GetReportVersions(parentReportId);

// Compare configurations
var comparison = comparisonService.CompareConfigs(versions[0], versions[1]);
foreach (var diff in comparison.Differences)
{
    Console.WriteLine(diff);
}

// Get metadata differences
var metadataDiffs = comparisonService.GetMetadataDifferences(versions[0], versions[1]);
```

## Database Schema

### report_history Table
```sql
CREATE TABLE report_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    uuid TEXT NOT NULL UNIQUE,
    user_id TEXT NOT NULL,
    username TEXT NOT NULL,
    report_type TEXT NOT NULL,
    file_path TEXT NOT NULL,
    generated_at TEXT NOT NULL,
    config_json TEXT,
    tags TEXT,
    purpose TEXT,
    description TEXT,
    version INTEGER,
    parent_report_id TEXT,
    app_version TEXT
);

CREATE INDEX idx_report_history_tags ON report_history(tags);
```

### report_drafts Table
```sql
CREATE TABLE report_drafts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    uuid TEXT NOT NULL UNIQUE,
    user_id TEXT NOT NULL,
    username TEXT NOT NULL,
    report_type TEXT NOT NULL,
    created_at TEXT NOT NULL,
    last_modified_at TEXT,
    config_json TEXT NOT NULL,
    draft_file_path TEXT NOT NULL,
    title TEXT,
    notes TEXT,
    tags TEXT,
    is_finalized INTEGER NOT NULL DEFAULT 0,
    exported_report_history_id TEXT
);

CREATE INDEX idx_report_drafts_user_id ON report_drafts(user_id);
CREATE INDEX idx_report_drafts_created_at ON report_drafts(created_at);
CREATE INDEX idx_report_drafts_finalized ON report_drafts(is_finalized);
```

## Service Registration

All services are registered in `ServiceContainer.cs`:

```csharp
services.AddSingleton<IReportFileOrganizer, ReportFileOrganizer>();
services.AddSingleton<IReportValidationService, ReportValidationService>();
services.AddSingleton<IReportHistoryFilterService, ReportHistoryFilterService>();
services.AddSingleton<IReportBulkExportService, ReportBulkExportService>();
services.AddSingleton<IReportComparisonService, ReportComparisonService>();
services.AddSingleton<IReportDraftStore, ReportDraftStore>();
services.AddSingleton<IReportDraftService, ReportDraftService>();
services.AddSingleton<IReportService, ReportService>();
```

## File Structure

```
Core/
  Reports/
    IReportService.cs                    - Main report generation interface
    ReportService.cs                     - Report generation implementation
    ReportFileOrganizer.cs               - File organization service
    ReportValidationService.cs           - Pre-generation validation
    ReportDraftService.cs                - Draft management
    ReportHistoryFilterService.cs        - Advanced filtering
    ReportBulkExportService.cs           - Bulk operations
    ReportComparisonService.cs           - Version comparison

Domain/
    ReportConfig.cs                      - Report configuration model
    ReportHistory.cs                     - History record model
    ReportDraft.cs                       - Draft model
    ReportProgress.cs                    - Progress reporting model

Storage/
    ReportHistoryStore.cs                - History persistence
    ReportDraftStore.cs                  - Draft persistence
    MigrationService.cs                  - Database migrations
      - Migration_036_EnhancedReportHistory
      - Migration_037_ReportDrafts

Views/
    ReportsView.xaml(.cs)                - Main reports UI
    ReportEditorView.xaml(.cs)           - Draft editor UI
```

## Configuration

### Report Output Paths
Default base path: `%USERPROFILE%\Documents\WorkAudit\Reports\`

Organization modes:
- `YearMonth`: `2026\01\DailySummary_20260101_20260131_153045.pdf`
- `YearQuarter`: `2026\Q1\DailySummary_20260101_20260331_153045.pdf`

### Draft Storage
Default path: `%USERPROFILE%\Documents\WorkAudit\Reports\Drafts\`

### Bulk Exports
Default path: `%USERPROFILE%\Documents\WorkAudit\Reports\BulkExports\`

## Best Practices

1. **Always validate** configurations before generation
2. **Use async methods** for UI responsiveness
3. **Implement cancellation** for long-running operations
4. **Tag reports** consistently for better organization
5. **Set purpose and description** for important reports
6. **Use drafts** for reports requiring review or editing
7. **Version reports** when regenerating with modifications
8. **Bulk export** for compliance or archival needs

## Troubleshooting

### Common Issues

**Report generation fails:**
- Check validation errors first
- Verify date range is valid
- Ensure format compatibility with report type
- Check document count preview

**WebView2 editor not loading:**
- Verify WebView2 runtime is installed
- Check file permissions for temp directory
- Review application logs for errors

**Draft export fails:**
- Ensure draft is saved before export
- Verify original configuration is valid
- Check target format is supported

## Future Enhancements

- PDF annotation and markup tools
- Report templates with custom branding
- Scheduled report generation with email distribution
- Real-time collaborative editing
- Cloud storage integration
- Advanced analytics and insights
- Custom report builder with drag-and-drop

## Support

For issues or feature requests, refer to the main application documentation or contact the development team.

---

*Last Updated: February 2026*
*Version: 1.0*
