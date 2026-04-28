using System.Data;
using WorkAudit.Core.Common;
using WorkAudit.Domain;

namespace WorkAudit.Storage;

/// <summary>
/// Interface for document storage operations.
/// </summary>
public interface IDocumentStore
{
    long Insert(Document doc);
    /// <summary>Gets a document by ID. Returns Result with error details on failure.</summary>
    Result<Document> GetResult(int id);
    Document? Get(int id);
    List<Document> GetByIds(List<int> ids);
    Document? GetByUuid(string uuid);
    Document? GetByFileHash(string fileHash);
    List<Document> ListDocuments(
        string? branch = null,
        string? section = null,
        string? documentType = null,
        string? status = null,
        string? dateFrom = null,
        string? dateTo = null,
        string? textSearch = null,
        int limit = 500,
        int offset = 0,
        string? archivedAtFrom = null,
        string? archivedAtTo = null,
        bool? legalHoldOnly = null,
        string? retentionExpiryBefore = null,
        string? tagFilter = null,
        int? custodianId = null,
        string? disposalStatus = null,
        string? createdBy = null,
        string? reviewedBy = null,
        string? createdOrReviewedBy = null,
        string? engagement = null,
        string? dateFilterField = "extracted");
    /// <param name="appBaseDirectory">When set, also matches <c>file_path</c> values stored relative to this base (legacy / moved installs).</param>
    List<Document> ListDocumentsByFolder(string folderPath, int limit = 1000, string? appBaseDirectory = null);
    List<string> GetDistinctBranches();
    List<string> GetDistinctSections(string? branch);
    List<string> GetDistinctDocumentTypes(string? branch, string? section);
    List<string> GetDistinctEngagements(string? branch = null);
    List<string> GetDistinctTags(string? status = null);
    /// <param name="branch">When set, restricts hits to this branch (exact match on <c>documents.branch</c>).</param>
    List<Document> FullTextSearch(string query, int limit = 100, string? branch = null);
    bool Update(Document doc);
    /// <summary>Inserts a document. Returns Result with ID or error.</summary>
    Result<long> InsertResult(Document doc);
    /// <summary>Updates a document. Returns Result with error details on failure.</summary>
    Result UpdateResult(Document doc);
    /// <summary>Deletes a document by ID. Returns Result with error details on failure.</summary>
    Result DeleteResult(int id);
    bool UpdateStatus(int id, string status);
    bool UpdateNotes(int id, string notes);
    /// <summary>Updates OCR-related columns only (does not touch classification/path/status fields).</summary>
    bool UpdateOcrFields(Document doc);
    bool UpdateDocumentType(int id, string documentType);
    bool UpdateTags(int id, string tags);
    bool UpdateCustodian(int id, int? custodianId);
    bool UpdateDisposalStatus(int id, string disposalStatus, int? requestedBy = null, int? approvedBy = null, int? rejectedBy = null, string? rejectionReason = null);
    bool UpdateDocumentSection(int id, string section);
    /// <summary>Updates only <c>file_path</c> and <c>updated_at</c> (avoids clobbering other columns with a partial in-memory document).</summary>
    bool UpdateDocumentFilePath(int id, string filePath);
    /// <summary>Returns documents whose stored <c>file_path</c> exactly matches <paramref name="filePath"/>.</summary>
    List<Document> GetDocumentsByFilePath(string filePath);
    /// <summary>Creates a new database connection for transaction management.</summary>
    IDbConnection CreateConnection();
    /// <summary>Updates document type within an existing transaction.</summary>
    bool UpdateDocumentType(int id, string documentType, IDbTransaction transaction);
    /// <summary>Updates document section within an existing transaction.</summary>
    bool UpdateDocumentSection(int id, string section, IDbTransaction transaction);
    /// <summary>Updates document file path within an existing transaction.</summary>
    bool UpdateDocumentFilePath(int id, string filePath, IDbTransaction transaction);
    /// <summary>Number of documents whose <c>file_path</c> equals <paramref name="filePath"/> (exact match as stored).</summary>
    int CountDocumentsWithFilePath(string filePath);
    bool Delete(int id);
    int Count(string? section = null, string? status = null, string? branch = null);
    /// <summary>Returns total document count (all time) without loading documents.</summary>
    int GetTotalDocumentCount();
    DocumentStats GetStats(string? branch = null);
}

public class DocumentStats
{
    public int TotalDocuments { get; set; }
    public int DraftCount { get; set; }
    public int ReviewedCount { get; set; }
    public int ReadyForAuditCount { get; set; }
    public int IssueCount { get; set; }
    public int ClearedCount { get; set; }
    public int IndividualsCount { get; set; }
    public int CompaniesCount { get; set; }
    public int ClearingCount { get; set; }
    public int TodayCount { get; set; }
    public int ThisWeekCount { get; set; }
    public int ThisMonthCount { get; set; }
    public Dictionary<string, int> ByDocumentType { get; set; } = new();
    /// <summary>Total archived documents.</summary>
    public int ArchivedCount { get; set; }
    /// <summary>Archived documents under legal hold.</summary>
    public int ArchivedLegalHoldCount { get; set; }
    /// <summary>Archived documents expiring within 90 days.</summary>
    public int ArchivedExpiringWithin90DaysCount { get; set; }
    /// <summary>Documents pending disposal approval.</summary>
    public int DisposalPendingCount { get; set; }
}
