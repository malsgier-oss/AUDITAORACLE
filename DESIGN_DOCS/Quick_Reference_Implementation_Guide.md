# Quick Reference Implementation Guide

## 🎯 For Developers: Start Here

This is your quick reference for implementing the Audit Manager's Intelligence Dashboard. For full details, see the comprehensive strategy documents.

---

## 📂 Files to Create

### Domain Models (Domain/)
- `AuditNote.cs` - Core note entity
- `NoteAttachment.cs` - File attachments for notes
- `AuditReportModel.cs` - Complete report structure
- `ReportExportConfig.cs` - Export configuration

### Services (Core/Reports/)
- `AuditNoteStore.cs` + `IAuditNoteStore.cs` - Note persistence
- `NoteAggregationService.cs` - Efficient note querying
- `AuditReportService.cs` - Report orchestration
- `BilingualLayoutService.cs` - RTL/LTR layout management
- `PdfReportGenerator.cs` - PDF export with QuestPDF

### UI (Views/)
- Update `ReportsView.xaml` - Dashboard layout
- Update `ReportsView.xaml.cs` - Code-behind
- `AddNoteDialog.xaml` + code-behind - Note creation dialog

### Resources
- `BilingualStyles.xaml` - RTL/LTR resource dictionaries

---

## 🗄️ Database Changes

### Oracle Migration Script

```sql
-- Run this migration before implementing code

-- Create AuditNotes table
CREATE TABLE IF NOT EXISTS AuditNotes (
    Id TEXT PRIMARY KEY,
    FileId TEXT,
    Content TEXT NOT NULL,
    Type TEXT NOT NULL CHECK(Type IN ('Issue', 'Observation', 'Evidence', 'Recommendation', 'General')),
    Severity TEXT NOT NULL CHECK(Severity IN ('Critical', 'High', 'Medium', 'Low', 'Info')),
    Category TEXT,
    CreatedAt TEXT NOT NULL,
    CreatedBy TEXT NOT NULL,
    CreatedByRole TEXT,
    LastModifiedAt TEXT,
    LastModifiedBy TEXT,
    Tags TEXT,
    IsFlagged INTEGER DEFAULT 0,
    Status TEXT DEFAULT 'Open' CHECK(Status IN ('Open', 'InProgress', 'Resolved', 'Deferred', 'NotApplicable')),
    ResolvedAt TEXT,
    ResolvedBy TEXT,
    ResolutionComment TEXT,
    FOREIGN KEY (FileId) REFERENCES Documents(Uuid) ON DELETE CASCADE
);

-- Create indexes for performance
CREATE INDEX IF NOT EXISTS idx_auditnotes_fileid ON AuditNotes(FileId);
CREATE INDEX IF NOT EXISTS idx_auditnotes_createdat ON AuditNotes(CreatedAt);
CREATE INDEX IF NOT EXISTS idx_auditnotes_type ON AuditNotes(Type);
CREATE INDEX IF NOT EXISTS idx_auditnotes_severity ON AuditNotes(Severity);

-- Create NoteAttachments table
CREATE TABLE IF NOT EXISTS NoteAttachments (
    Id TEXT PRIMARY KEY,
    NoteId TEXT NOT NULL,
    FileName TEXT NOT NULL,
    FilePath TEXT NOT NULL,
    FileSize INTEGER,
    ContentType TEXT,
    UploadedAt TEXT NOT NULL,
    FOREIGN KEY (NoteId) REFERENCES AuditNotes(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_noteattachments_noteid ON NoteAttachments(NoteId);
```

**Where to run:**
- Add to `Storage/DatabaseInitializer.cs` in the initialization method
- Or create a migration script and run manually

---

## 📦 NuGet Packages to Install

```bash
# PDF generation with RTL support
Install-Package QuestPDF

# Already in project (verify versions)
# Install-Package Oracle.ManagedDataAccess.Core
# Install-Package Newtonsoft.Json
# Install-Package OxyPlot.Wpf
# Install-Package Serilog
```

**QuestPDF License:** Free for open-source and educational use. Requires license for commercial use. See https://www.questpdf.com/license/

---

## 🔧 Key Code Snippets

### 1. AuditNote Domain Model (Simplified)

```csharp
// Domain/AuditNote.cs
namespace WorkAudit.Domain;

public class AuditNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? FileId { get; set; }
    public string Content { get; set; } = "";
    public NoteType Type { get; set; }
    public NoteSeverity Severity { get; set; }
    public string Category { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
    public List<NoteAttachment> Attachments { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public NoteStatus Status { get; set; } = NoteStatus.Open;

    // Display helpers
    public string TypeIcon => Type switch
    {
        NoteType.Issue => "🔴",
        NoteType.Observation => "📋",
        NoteType.Evidence => "✅",
        NoteType.Recommendation => "💡",
        _ => "📝"
    };
}

public enum NoteType { Issue, Observation, Evidence, Recommendation, General }
public enum NoteSeverity { Critical, High, Medium, Low, Info }
public enum NoteStatus { Open, InProgress, Resolved, Deferred, NotApplicable }
```

### 2. AuditNoteStore (Interface)

```csharp
// Storage/IAuditNoteStore.cs
namespace WorkAudit.Storage;

public interface IAuditNoteStore
{
    AuditNote Insert(AuditNote note);
    AuditNote Update(AuditNote note);
    void Delete(string noteId);
    AuditNote? Get(string noteId);
    List<AuditNote> GetNotesForFile(string fileId);
    Dictionary<string, List<AuditNote>> GetNotesGroupedByFile(DateTime from, DateTime to, string? branch = null, string? section = null);
    int GetTotalNoteCount(DateTime from, DateTime to);
}
```

### 3. Critical Note Aggregation Query

```csharp
// Storage/AuditNoteStore.cs
public Dictionary<string, List<AuditNote>> GetNotesGroupedByFile(
    DateTime from, DateTime to, string? branch = null, string? section = null)
{
    var sql = @"
        SELECT n.*,
               GROUP_CONCAT(a.Id || '|' || a.FileName || '|' || a.FilePath, ';;') as AttachmentsData
        FROM AuditNotes n
        LEFT JOIN NoteAttachments a ON n.Id = a.NoteId
        WHERE n.CreatedAt BETWEEN @from AND @to
          AND (@branch IS NULL OR n.FileId IN (
              SELECT Uuid FROM Documents WHERE Branch = @branch
          ))
          AND (@section IS NULL OR n.FileId IN (
              SELECT Uuid FROM Documents WHERE Section = @section
          ))
        GROUP BY n.Id
        ORDER BY n.CreatedAt DESC";

    var notes = new List<AuditNote>();

    using (var cmd = _connection.CreateCommand())
    {
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@from", from.ToString("o"));
        cmd.Parameters.AddWithValue("@to", to.ToString("o"));
        cmd.Parameters.AddWithValue("@branch", branch ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@section", section ?? (object)DBNull.Value);

        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var note = MapNoteFromReader(reader);
                notes.Add(note);
            }
        }
    }

    // Group by FileId
    return notes
        .Where(n => !string.IsNullOrEmpty(n.FileId))
        .GroupBy(n => n.FileId!)
        .ToDictionary(g => g.Key, g => g.ToList());
}
```

### 4. FlowDirection Binding (XAML)

```xaml
<!-- Views/ReportsView.xaml -->
<UserControl x:Class="WorkAudit.Views.ReportsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Root Grid with FlowDirection binding -->
    <Grid FlowDirection="{Binding CurrentFlowDirection}">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Header -->
            <RowDefinition Height="Auto"/>  <!-- KPI Cards -->
            <RowDefinition Height="*"/>     <!-- Findings Table -->
        </Grid.RowDefinitions>

        <!-- Header with language switcher -->
        <StackPanel Grid.Row="0" Orientation="Horizontal">
            <TextBlock Text="{Binding DashboardTitle}" FontSize="20"/>
            <ComboBox x:Name="LanguageCombo"
                      SelectedValue="{Binding CurrentLanguage}"
                      SelectionChanged="Language_Changed">
                <ComboBoxItem Content="English" Tag="en"/>
                <ComboBoxItem Content="العربية" Tag="ar"/>
            </ComboBox>
        </StackPanel>

        <!-- KPI Cards -->
        <WrapPanel x:Name="KpiCardsPanel" Grid.Row="1"/>

        <!-- Findings DataGrid -->
        <DataGrid x:Name="FindingsDataGrid" Grid.Row="2"
                  ItemsSource="{Binding Findings}"
                  AutoGenerateColumns="False">
            <!-- Columns set in code-behind based on language -->
        </DataGrid>
    </Grid>
</UserControl>
```

### 5. ViewModel with Language Support

```csharp
// ViewModels/ReportDashboardViewModel.cs (create if not exists)
public class ReportDashboardViewModel : INotifyPropertyChanged
{
    private readonly IConfigStore _configStore;
    private string _currentLanguage = "en";

    public FlowDirection CurrentFlowDirection =>
        _currentLanguage == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                _configStore.SetSettingValue("report_language", value);
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged(nameof(CurrentFlowDirection));
                // Trigger UI refresh
                RefreshDashboard();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

### 6. PDF Export with QuestPDF

```csharp
// Core/Reports/PdfReportGenerator.cs
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class PdfReportGenerator
{
    public string GeneratePdf(AuditReportModel report, ReportExportConfig config)
    {
        var isRtl = report.LanguageSettings.IsRightToLeft;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);

                // CRITICAL: Set RTL for Arabic
                if (isRtl)
                    page.ContentDirection(ContentDirection.RightToLeft);

                page.Header().Element(c => ComposeHeader(c, report));
                page.Content().Element(c => ComposeContent(c, report, config));
                page.Footer().Element(c => ComposeFooter(c, report));
            });
        });

        var outputPath = Path.Combine(Path.GetTempPath(),
            $"AuditReport_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        document.GeneratePdf(outputPath);
        return outputPath;
    }

    private void ComposeContent(IContainer container, AuditReportModel report,
        ReportExportConfig config)
    {
        container.Column(column =>
        {
            // Findings table with notes
            var findingsSection = report.Sections
                .FirstOrDefault(s => s.Type == SectionType.FindingsTable);

            if (findingsSection != null && config.IncludedSections.Contains(SectionType.FindingsTable))
            {
                column.Item().Element(c => ComposeFindingsTable(c, findingsSection.Findings, config.NoteDetailLevel));
            }
        });
    }

    private void ComposeFindingsTable(IContainer container, List<AuditFinding> findings,
        NoteDetailLevel noteDetailLevel)
    {
        container.Table(table =>
        {
            // Define columns
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1);  // Severity
                columns.RelativeColumn(3);  // File
                columns.RelativeColumn(2);  // Type
                columns.RelativeColumn(1);  // Notes
            });

            // Header
            table.Header(header =>
            {
                header.Cell().Text("Severity").Bold();
                header.Cell().Text("File Name").Bold();
                header.Cell().Text("Type").Bold();
                header.Cell().Text("Notes").Bold();
            });

            // Rows
            foreach (var finding in findings)
            {
                table.Cell().Text(finding.Severity.ToString());
                table.Cell().Text(finding.FileName);
                table.Cell().Text(finding.DocumentType);
                table.Cell().Text(finding.Notes.Count.ToString());

                // Note details (if Full detail level)
                if (noteDetailLevel == NoteDetailLevel.Full && finding.Notes.Any())
                {
                    table.Cell().ColumnSpan(4).Element(c => ComposeNotes(c, finding.Notes));
                }
            }
        });
    }

    private void ComposeNotes(IContainer container, List<AuditNote> notes)
    {
        container.Column(column =>
        {
            foreach (var note in notes)
            {
                column.Item().BorderLeft(3).BorderColor(note.SeverityColor)
                    .PaddingLeft(8).Column(noteCol =>
                    {
                        noteCol.Item().Text($"{note.TypeIcon} {note.Type}").Bold();
                        noteCol.Item().Text(note.Content);
                    });
            }
        });
    }
}
```

---

## ⚡ Performance Tips

### Database Queries
```csharp
// ✅ GOOD: Single query with JOIN
var notes = GetNotesGroupedByFile(from, to);  // One query, indexed

// ❌ BAD: N+1 queries
foreach (var doc in documents)
{
    var notes = GetNotesForFile(doc.Uuid);  // N queries!
}
```

### UI Virtualization
```xaml
<!-- ✅ GOOD: Virtualization enabled (default for DataGrid) -->
<DataGrid VirtualizingPanel.IsVirtualizing="True"
          VirtualizingPanel.VirtualizationMode="Recycling"/>

<!-- ❌ BAD: ItemsControl without virtualization for large lists -->
<ItemsControl ItemsSource="{Binding Findings}"/>  <!-- Loads ALL items! -->
```

### Caching
```csharp
// ✅ GOOD: Cache frequently-accessed data
private Dictionary<string, List<AuditNote>>? _cachedNotes;
private DateTime _cacheExpiry;

public Dictionary<string, List<AuditNote>> GetNotesGroupedByFile(DateTime from, DateTime to)
{
    if (_cachedNotes != null && DateTime.UtcNow < _cacheExpiry)
        return _cachedNotes;

    _cachedNotes = LoadNotesFromDatabase(from, to);
    _cacheExpiry = DateTime.UtcNow.AddMinutes(5);
    return _cachedNotes;
}
```

---

## 🧪 Testing Checklist

### Unit Tests
- [ ] AuditNoteStore.Insert/Update/Delete
- [ ] NoteAggregationService.GetNotesGroupedByFile
- [ ] AuditReportService.BuildReport (verify note count)
- [ ] BilingualLayoutService.GetFontFamily (en/ar)
- [ ] Executive summary generation (bilingual)

### Integration Tests
- [ ] Generate report with 1000 files + 5000 notes → verify all notes included
- [ ] Export PDF (English) → open and manually verify
- [ ] Export PDF (Arabic) → verify RTL layout, Arabic fonts
- [ ] Switch language 10 times → no crashes, no data loss

### Performance Tests
- [ ] Load 10,000 notes in dashboard < 2 seconds
- [ ] Generate 50-page PDF < 5 seconds
- [ ] Filter 10,000 findings < 200ms

### Accessibility Tests
- [ ] Keyboard-only navigation (Tab, Enter, Arrow keys)
- [ ] Screen reader (NVDA) - test with both English and Arabic
- [ ] High contrast mode (Windows)
- [ ] 200% text zoom

---

## 🐛 Common Pitfalls to Avoid

### 1. Lost Notes in Reports
**Problem:** Notes not showing in PDF
**Solution:**
- Always use `NoteDetailLevel.Full` as default
- Validate note counts at each stage (see validation checkpoints)
- Log note counts during report generation

### 2. RTL Layout Issues
**Problem:** Arabic text appears left-aligned
**Solution:**
- Set `FlowDirection` on root container
- Use `ContentDirection.RightToLeft` in QuestPDF
- Reverse column order for tables in RTL mode

### 3. Font Issues in PDF
**Problem:** Arabic characters show as boxes (□)
**Solution:**
- Embed Arabic fonts in PDF (QuestPDF does this automatically)
- Use system fonts like "Arabic Typesetting" or "Tahoma"
- Test on machine without Arabic fonts installed

### 4. Performance Issues
**Problem:** Dashboard slow with many notes
**Solution:**
- Add database indexes (see migration script)
- Use single query with JOIN (not N+1)
- Enable DataGrid virtualization
- Cache aggregated notes for 5 minutes

### 5. Language Switching Bugs
**Problem:** UI doesn't fully update on language change
**Solution:**
- Use data binding with `INotifyPropertyChanged`
- Call `OnPropertyChanged` for all language-dependent properties
- Refresh DataGrid columns on language change

---

## 📞 Need Help?

1. **Review full documentation:**
   - `Report_Dashboard_Implementation_Strategy.md` - Complete 10-week plan
   - `Report_Dashboard_Data_Models.cs` - All data structures
   - `Notes_Integration_Flow_Diagram.md` - Visual flow diagrams

2. **Check examples:**
   - Existing `ReportService.cs` for report generation patterns
   - Existing `ExecutiveDashboardView.xaml` for dashboard layout patterns
   - `ReportLocalizationService.cs` for bilingual text

3. **Common issues:**
   - Database not initializing? Check `DatabaseInitializer.cs`
   - Language not switching? Check `IConfigStore` implementation
   - PDF not generating? Verify QuestPDF NuGet installed

---

## ✅ Quick Start (First Day)

```bash
# 1. Create feature branch
git checkout -b feature/report-dashboard

# 2. Install NuGet packages
Install-Package QuestPDF

# 3. Create AuditNote.cs (copy from Report_Dashboard_Data_Models.cs)
# Location: Domain/AuditNote.cs

# 4. Run database migration
# Location: Storage/DatabaseInitializer.cs
# Add migration from this guide

# 5. Create IAuditNoteStore interface
# Location: Storage/IAuditNoteStore.cs

# 6. Implement AuditNoteStore
# Location: Storage/AuditNoteStore.cs
# Use query example from this guide

# 7. Register in ServiceContainer
# Location: Core/Services/ServiceContainer.cs
services.AddSingleton<IAuditNoteStore>(new AuditNoteStore(dbConnection));

# 8. Test basic CRUD
# Create unit test, insert/read a note

# 9. Commit progress
git add .
git commit -m "feat: Add AuditNote domain model and storage layer"
```

**Day 1 Goal:** Have AuditNote domain model and basic storage working with unit tests passing.

---

**Good luck with implementation! 🚀**
