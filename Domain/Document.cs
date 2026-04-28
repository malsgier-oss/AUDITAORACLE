using System.Globalization;
using Newtonsoft.Json.Linq;

namespace WorkAudit.Domain;

/// <summary>
/// Document metadata as persisted by the Oracle-backed storage layer.
/// Enhanced with additional fields for professional audit workflow.
/// </summary>
public class Document
{
    // Core identifiers
    public int Id { get; set; }
    public string Uuid { get; set; } = "";

    // File information
    public string FilePath { get; set; } = "";
    public string? FileHash { get; set; }
    public long? FileSize { get; set; }
    public int? PageCount { get; set; } = 1;

    // Document classification
    public string? DocumentType { get; set; }
    public string? Category { get; set; }
    public float? ClassificationConfidence { get; set; }
    public string? Explanation { get; set; }

    // OCR data
    public string? OcrText { get; set; }
    public string? Snippet { get; set; }
    public string? OcrLanguage { get; set; }
    public int? OcrDurationMs { get; set; }

    // Extracted data
    public string? ExtractedDate { get; set; }
    public string? Amounts { get; set; }
    /// <summary>Account holder, customer, or counterparty name for banking/audit context.</summary>
    public string? AccountName { get; set; }
    /// <summary>Bank or internal account identifier (stored as text for leading zeros and masks).</summary>
    public string? AccountNumber { get; set; }
    /// <summary>Wire reference, cheque number, authorization id, or similar.</summary>
    public string? TransactionReference { get; set; }

    // Organization
    public string Engagement { get; set; } = "";
    public string Section { get; set; } = "";
    public string? Branch { get; set; }

    // Clearing-specific fields
    public string? ClearingDirection { get; set; }
    public string? ClearingStatus { get; set; }

    // Workflow status
    public string Status { get; set; } = Enums.Status.Draft;
    public float? Confidence { get; set; }

    // DEPRECATED: Use INotesStore for structured notes. This field is preserved for export compatibility.
    [Obsolete("Use INotesStore.GetByDocumentId() instead for structured notes")]
    public string? Notes { get; set; }

    // Timestamps
    public string CaptureTime { get; set; } = "";
    public string Source { get; set; } = "";
    public string? ReviewedAt { get; set; }
    public string? UpdatedAt { get; set; }

    // Audit trail
    public string? CreatedBy { get; set; }
    public string? ReviewedBy { get; set; }

    // Duplicate detection
    public bool IsDuplicate { get; set; }
    public string? DuplicateOf { get; set; }

    // Custom metadata
    public string? Tags { get; set; }
    public string? CustomFields { get; set; }

    // P0 Archive fields (application-level immutability - NOT hardware-certified WORM)
    public string? ArchivedAt { get; set; }
    public int? ArchivedBy { get; set; }
    public bool LegalHold { get; set; }
    public string? LegalHoldReason { get; set; }
    public string? LegalHoldCaseNumber { get; set; }
    public string? LegalHoldAppliedAt { get; set; }
    public int? LegalHoldAppliedBy { get; set; }
    public string? RetentionExpiryDate { get; set; }
    public bool IsImmutable { get; set; }
    public string? ImmutableHash { get; set; }
    public string? ImmutableSince { get; set; }
    public int HashVerificationCount { get; set; }
    public string? LastHashVerification { get; set; }

    // Custodian and disposal
    public int? CustodianId { get; set; }
    public string? DisposalStatus { get; set; } // Pending, Approved, Rejected
    public string? DisposalRequestedAt { get; set; }
    public int? DisposalRequestedBy { get; set; }
    public string? DisposalApprovedAt { get; set; }
    public int? DisposalApprovedBy { get; set; }
    public string? DisposalRejectedAt { get; set; }
    public int? DisposalRejectedBy { get; set; }
    public string? DisposalRejectionReason { get; set; }

    // Computed properties
    public bool IsClearing => Section == Enums.Section.Clearing;
    public bool HasOcrText => !string.IsNullOrEmpty(OcrText);
    public bool IsClassified => !DocumentTypeInfo.IsUnclassified(DocumentType);

    public string DisplayName => DocumentType ?? "Unknown Document";

    /// <summary>Formatted document date for display (user/import <see cref="ExtractedDate"/> only; not capture time).</summary>
    public string DateDisplay
    {
        get
        {
            var raw = ExtractedDate;
            if (string.IsNullOrWhiteSpace(raw)) return "—";
            var t = raw.Trim();
            // Prefer calendar date prefix so values like yyyy-MM-dd are not shifted by local time zone parsing.
            if (t.Length >= 10 && t[4] == '-' && t[7] == '-')
            {
                var prefix = t[..10];
                if (DateTime.TryParseExact(prefix, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    return prefix;
            }

            if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return t.Length > 10 ? t[..10] : t;
        }
    }

    /// <summary>Display string for document list (date + filename + type).</summary>
    public string DocumentListDisplay
    {
        get
        {
            var fileName = string.IsNullOrEmpty(FilePath) ? "Unknown" : System.IO.Path.GetFileName(FilePath);
            var type = string.IsNullOrWhiteSpace(DocumentType) ? "—" : DocumentType.Trim();
            return $"{DateDisplay}  {fileName} ({type})";
        }
    }

    public string StatusDisplay => Status switch
    {
        Enums.Status.Draft => "Draft",
        Enums.Status.Reviewed => "Reviewed",
        Enums.Status.ReadyForAudit => "Ready for Audit",
        Enums.Status.Issue => "Issue Found",
        Enums.Status.Cleared => "Cleared",
        _ => Status
    };

    public string FileSizeDisplay
    {
        get
        {
            if (!FileSize.HasValue) return "Unknown";
            var size = FileSize.Value;
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            return $"{size / (1024.0 * 1024):F1} MB";
        }
    }

    public DateTime? CaptureDateTime
    {
        get
        {
            if (DateTime.TryParse(CaptureTime, out var dt))
                return dt;
            return null;
        }
    }

    public string[] GetTags()
    {
        if (string.IsNullOrEmpty(Tags)) return Array.Empty<string>();
        return Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void SetTags(IEnumerable<string> tags)
    {
        Tags = string.Join(",", tags);
    }

    public void AddTag(string tag)
    {
        var currentTags = GetTags().ToList();
        if (!currentTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            currentTags.Add(tag);
            SetTags(currentTags);
        }
    }

    public void RemoveTag(string tag)
    {
        var currentTags = GetTags().Where(t => !t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        SetTags(currentTags);
    }

    /// <summary>Gets the follow-up due date from CustomFields, if set.</summary>
    public DateTime? GetFollowUpDue()
    {
        if (string.IsNullOrEmpty(CustomFields)) return null;
        try
        {
            var obj = JObject.Parse(CustomFields);
            var val = obj["follow_up_due"]?.ToString();
            if (string.IsNullOrEmpty(val)) return null;
            return DateTime.TryParse(val, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
        }
        catch { return null; }
    }

    /// <summary>Sets or clears the follow-up due date in CustomFields. Merges with existing JSON.</summary>
    public void SetFollowUpDue(DateTime? due)
    {
        var obj = string.IsNullOrEmpty(CustomFields) ? new JObject() : JObject.Parse(CustomFields);
        if (due.HasValue)
            obj["follow_up_due"] = due.Value.ToUniversalTime().ToString("O");
        else
            obj.Remove("follow_up_due");
        CustomFields = obj.ToString();
    }

    /// <summary>True if document has follow-up tag and due date is in the past or today.</summary>
    public bool IsFollowUpDue => GetTags().Contains("follow-up", StringComparer.OrdinalIgnoreCase) &&
        GetFollowUpDue() is { } d && d.Date <= DateTime.UtcNow.Date;
}
