using System.Globalization;
using System.IO;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public interface IWorkflowMonitor
{
    List<WorkflowIssue> DetectIssues(AppConfiguration config);
    SystemStats BuildSystemStats(IDocumentStore docStore, IDocumentAssignmentStore assignmentStore, IConfigStore configStore, IUserStore userStore);
}

public sealed class WorkflowMonitor : IWorkflowMonitor
{
    private readonly IDocumentStore _documentStore;
    private readonly IDocumentAssignmentStore _assignmentStore;
    private readonly IProcessingMergeQueueService _mergeQueue;

    public WorkflowMonitor(
        IDocumentStore documentStore,
        IDocumentAssignmentStore assignmentStore,
        IProcessingMergeQueueService mergeQueue)
    {
        _documentStore = documentStore;
        _assignmentStore = assignmentStore;
        _mergeQueue = mergeQueue;
    }

    public List<WorkflowIssue> DetectIssues(AppConfiguration config)
    {
        var docStore = _documentStore;
        var assignmentStore = _assignmentStore;
        var issues = new List<WorkflowIssue>();
        var now = DateTime.UtcNow;

        // Stuck Draft (>7 days)
        var draftCutoff = now.AddDays(-7);
        var drafts = docStore.ListDocuments(status: Enums.Status.Draft, limit: 400, newestFirst: false);
        foreach (var d in drafts)
        {
            if (!TryParseUtc(d.CaptureTime, out var cap) || cap > draftCutoff) continue;
            issues.Add(new WorkflowIssue
            {
                Type = "StuckDraft",
                Severity = "Warning",
                DocumentId = d.Id,
                DocumentUuid = d.Uuid,
                Description = $"Document in Draft since {cap:yyyy-MM-dd} (capture time).",
                RecommendedAction = "Complete classification or archive/delete.",
                DetectedAtUtc = now
            });
            if (issues.Count(w => w.Type == "StuckDraft") >= 50) break;
        }

        // Draft / Processing flow: classified but no OCR text after 48h (heuristic)
        var ocrCutoff = now.AddHours(-48);
        var candidates = docStore.ListDocuments(status: Enums.Status.Draft, limit: 200, newestFirst: true);
        foreach (var d in candidates)
        {
            if (string.IsNullOrWhiteSpace(d.OcrText) && !DocumentTypeInfo.IsUnclassified(d.DocumentType))
            {
                if (TryParseUtc(d.CaptureTime, out var cap) && cap < ocrCutoff)
                {
                    issues.Add(new WorkflowIssue
                    {
                        Type = "MissingOcrAfterClassification",
                        Severity = "Warning",
                        DocumentId = d.Id,
                        DocumentUuid = d.Uuid,
                        Description = $"Classified document without OCR text (captured {cap:yyyy-MM-dd}).",
                        RecommendedAction = "Retry OCR from Workspace or check logs.",
                        DetectedAtUtc = now
                    });
                }
            }
        }

        // Ready for Audit too long (>30 days)
        var rfaCutoff = now.AddDays(-30);
        var rfa = docStore.ListDocuments(status: Enums.Status.ReadyForAudit, limit: 300, newestFirst: false);
        foreach (var d in rfa)
        {
            if (!TryParseUtc(d.CaptureTime, out var cap) || cap > rfaCutoff) continue;
            issues.Add(new WorkflowIssue
            {
                Type = "ReadyForAuditStale",
                Severity = "Info",
                DocumentId = d.Id,
                DocumentUuid = d.Uuid,
                Description = $"Ready for Audit since {cap:yyyy-MM-dd}.",
                RecommendedAction = "Review status or move to workspace/archive.",
                DetectedAtUtc = now
            });
            if (issues.Count(w => w.Type == "ReadyForAuditStale") >= 40) break;
        }

        // Overdue assignments
        var assignments = assignmentStore.ListAll(null, null, 1500);
        foreach (var a in assignments)
        {
            if (a.Status is AssignmentStatus.Completed or AssignmentStatus.Cancelled)
                continue;
            if (string.IsNullOrEmpty(a.DueDate)) continue;
            if (!DateTime.TryParse(a.DueDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var due))
                continue;
            if (due.ToUniversalTime() >= now) continue;

            issues.Add(new WorkflowIssue
            {
                Type = "OverdueAssignment",
                Severity = "Warning",
                DocumentId = a.DocumentId,
                DocumentUuid = a.DocumentUuid,
                AssignmentId = a.Id,
                Description = $"Assignment #{a.Id} overdue (due {due:yyyy-MM-dd}, status {a.Status}).",
                RecommendedAction = "Complete, reassign, or cancel the assignment.",
                DetectedAtUtc = now
            });
        }

        // InProgress assignment stale (>7 days since StartedAt)
        var assignStale = now.AddDays(-7);
        foreach (var a in assignments.Where(x => x.Status == AssignmentStatus.InProgress))
        {
            if (string.IsNullOrEmpty(a.StartedAt)) continue;
            if (!DateTime.TryParse(a.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
                continue;
            if (started.ToUniversalTime() > assignStale) continue;

            issues.Add(new WorkflowIssue
            {
                Type = "StaleInProgressAssignment",
                Severity = "Info",
                DocumentId = a.DocumentId,
                DocumentUuid = a.DocumentUuid,
                AssignmentId = a.Id,
                Description = $"Assignment #{a.Id} InProgress since {started:yyyy-MM-dd}.",
                RecommendedAction = "Complete or mark cancelled.",
                DetectedAtUtc = now
            });
        }

        // Missing files (sample of recent documents)
        var recent = docStore.ListDocuments(limit: 120, newestFirst: true);
        foreach (var d in recent)
        {
            if (string.IsNullOrWhiteSpace(d.FilePath)) continue;
            var full = PathHelpers.ResolveFullPath(config.BaseDirectory, d.FilePath);
            if (!File.Exists(full))
            {
                issues.Add(new WorkflowIssue
                {
                    Type = "MissingFile",
                    Severity = "Error",
                    DocumentId = d.Id,
                    DocumentUuid = d.Uuid,
                    Description = $"File not found for document {d.Id}: {d.FilePath}",
                    RecommendedAction = "Restore from backup or re-import.",
                    DetectedAtUtc = now
                });
            }
        }

        // Merge queue backlog (pending count + oldest enqueue among unfinished jobs)
        var pendingMerge = _mergeQueue.PendingCount;
        var oldestMergeUtc = _mergeQueue.OldestPendingMergeEnqueueUtc;
        var oldestAge = oldestMergeUtc.HasValue ? now - oldestMergeUtc.Value : TimeSpan.Zero;
        if (pendingMerge > 5 || (pendingMerge > 0 && oldestAge.TotalHours >= 1))
        {
            var severity = pendingMerge > 20 || oldestAge.TotalHours >= 24 ? "Warning" : "Info";
            var ageNote = oldestMergeUtc.HasValue
                ? $" Oldest unfinished job enqueued (UTC): {oldestMergeUtc:O} (~{(int)Math.Max(1, oldestAge.TotalMinutes)} min)."
                : "";
            issues.Add(new WorkflowIssue
            {
                Type = "MergeQueueBacklog",
                Severity = severity,
                Description =
                    $"Processing merge queue: {pendingMerge} unfinished job(s).{ageNote} If this persists, check Processing view and logs.",
                RecommendedAction = "Review merge failures in logs; retry or clear stuck jobs after verifying disk/database health.",
                DetectedAtUtc = now
            });
        }

        // Orphaned files: on-disk files under attachments with no matching documents.file_path (sampled scan)
        TryDetectOrphanedAttachmentFiles(config.BaseDirectory, docStore, issues, now);

        return issues;
    }

    public SystemStats BuildSystemStats(IDocumentStore docStore, IDocumentAssignmentStore assignmentStore, IConfigStore configStore, IUserStore userStore)
    {
        var stats = docStore.GetStats();
        var assignments = assignmentStore.ListAll(null, null, 2000);
        var now = DateTime.UtcNow;
        var overdue = 0L;
        foreach (var a in assignments)
        {
            if (a.Status is AssignmentStatus.Completed or AssignmentStatus.Cancelled) continue;
            if (string.IsNullOrEmpty(a.DueDate)) continue;
            if (DateTime.TryParse(a.DueDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var due)
                && due.ToUniversalTime() < now)
                overdue++;
        }

        var byStatus = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            [Enums.Status.Draft] = stats.DraftCount,
            [Enums.Status.Reviewed] = stats.ReviewedCount,
            [Enums.Status.ReadyForAudit] = stats.ReadyForAuditCount,
            [Enums.Status.Issue] = stats.IssueCount,
            [Enums.Status.Cleared] = stats.ClearedCount,
            [Enums.Status.Archived] = stats.ArchivedCount
        };

        var pendingAssign = assignments.LongCount(a =>
            a.Status is AssignmentStatus.Pending or AssignmentStatus.InProgress);

        var branches = configStore.GetBranches(true).LongCount(b => b.IsActive);
        var docTypes = configStore.GetDocumentTypes(true).LongCount(t => t.IsActive);

        var users = userStore.ListUsers(isActive: true);
        return new SystemStats
        {
            TotalDocuments = stats.TotalDocuments,
            DocumentsByStatus = byStatus,
            PendingAssignments = pendingAssign,
            OverdueAssignments = overdue,
            TotalBranches = branches,
            TotalDocumentTypes = docTypes,
            TotalUsers = users.Count,
            ActiveUsers = users.Count
        };
    }

    private static void TryDetectOrphanedAttachmentFiles(
        string baseDirectory,
        IDocumentStore docStore,
        List<WorkflowIssue> issues,
        DateTime nowUtc)
    {
        const int maxScanFiles = 400;
        const int maxOrphanIssues = 25;
        var attachRoot = Path.Combine(baseDirectory.TrimEnd(Path.DirectorySeparatorChar), "attachments");
        if (!Directory.Exists(attachRoot))
            return;

        var orphanCount = issues.Count(w => w.Type == "OrphanedFile");
        var scanned = 0;
        foreach (var fullPath in Directory.EnumerateFiles(attachRoot, "*", SearchOption.AllDirectories))
        {
            if (scanned++ >= maxScanFiles)
                break;
            if (orphanCount >= maxOrphanIssues)
                break;

            string relative;
            try
            {
                relative = Path.GetRelativePath(baseDirectory, fullPath);
            }
            catch
            {
                continue;
            }

            if (DocumentReferencesPath(docStore, relative))
                continue;

            issues.Add(new WorkflowIssue
            {
                Type = "OrphanedFile",
                Severity = "Info",
                Description = $"File on disk with no matching document row: {relative}",
                RecommendedAction = "If leftover from deleted docs, remove safely; otherwise run integrity/import recovery.",
                DetectedAtUtc = nowUtc,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Path"] = relative }
            });
            orphanCount++;
        }
    }

    private static bool DocumentReferencesPath(IDocumentStore docStore, string relativePath)
    {
        if (docStore.CountDocumentsWithFilePath(relativePath) > 0)
            return true;

        var altSlash = relativePath.Replace('\\', '/');
        if (altSlash != relativePath && docStore.CountDocumentsWithFilePath(altSlash) > 0)
            return true;

        var altWin = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (altWin != relativePath && docStore.CountDocumentsWithFilePath(altWin) > 0)
            return true;

        return false;
    }

    private static bool TryParseUtc(string? iso, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(iso)) return false;
        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            utc = dt.ToUniversalTime();
            return true;
        }

        return false;
    }
}

/// <summary>Resolves stored relative or absolute document paths.</summary>
internal static class PathHelpers
{
    public static string ResolveFullPath(string baseDir, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "";
        if (Path.IsPathRooted(filePath))
            return filePath;
        return Path.GetFullPath(Path.Combine(baseDir.TrimEnd(Path.DirectorySeparatorChar), filePath));
    }
}
