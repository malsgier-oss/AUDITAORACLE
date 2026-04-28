namespace WorkAudit.Domain;

/// <summary>
/// Application-wide constants for classification and processing.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Confidence thresholds for document classification.
    /// </summary>
    public static class Confidence
    {
        /// <summary>Low confidence threshold (50%) - documents below this need review.</summary>
        public const float Low = 0.5f;

        /// <summary>High confidence threshold (80%) - documents above this are considered reliable.</summary>
        public const float High = 0.8f;
    }

    /// <summary>
    /// Filter persistence key prefixes.
    /// </summary>
    public static class FilterPrefix
    {
        public const string Search = "search_filter_";
        public const string Workspace = "workspace_filter_";
        public const string Processing = "processing_filter_";
        public const string Archive = "archive_filter_";
    }

    /// <summary>
    /// Common filter field keys.
    /// </summary>
    public static class FilterKey
    {
        public const string Branch = "branch";
        public const string Section = "section";
        public const string DocumentType = "documentType";
        public const string Status = "status";
        public const string DateFrom = "dateFrom";
        public const string DateTo = "dateTo";
        public const string TextQuery = "textQuery";
        public const string UseFts = "useFts";
        public const string NaturalLanguage = "naturalLanguage";
        public const string LegalHold = "legalHold";
        public const string ArchivedDateFrom = "archivedDateFrom";
        public const string ArchivedDateTo = "archivedDateTo";
        public const string ExpiringWithinDays = "expiringWithinDays";
        public const string Tag = "tag";
    }

    /// <summary>
    /// User settings key for workspace browser quick links (JSON array of QuickLink).
    /// </summary>
    public const string WorkspaceBrowserQuickLinksKey = "workspace_browser_quicklinks";
}
