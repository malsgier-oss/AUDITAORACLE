namespace WorkAudit.Domain;

/// <summary>
/// Domain enums and allowed values. Single source of truth for status lifecycle,
/// section types, and clearing workflow states.
/// </summary>
public static class Enums
{
    /// <summary>Section values: Individuals, Companies, Clearing</summary>
    public static readonly string[] SectionValues = { "Individuals", "Companies", "Clearing" };

    /// <summary>Required when section is Clearing</summary>
    public static readonly string[] ClearingDirectionValues = { "Outward", "Inward" };

    /// <summary>Required when section is Clearing</summary>
    public static readonly string[] ClearingStatusValues = { "Clearing", "Rejected" };

    /// <summary>Document status lifecycle: Draft -> Reviewed -> Ready for Audit -> Issue | Cleared | Archived</summary>
    public static readonly string[] StatusValues = { "Draft", "Reviewed", "Ready for Audit", "Issue", "Cleared", "Archived" };

    public static class Section
    {
        public const string Individuals = "Individuals";
        public const string Companies = "Companies";
        public const string Clearing = "Clearing";
    }

    public static class Status
    {
        public const string Draft = "Draft";
        public const string Reviewed = "Reviewed";
        public const string ReadyForAudit = "Ready for Audit";
        public const string Issue = "Issue";
        public const string Cleared = "Cleared";
        public const string Archived = "Archived";
    }

    public static class ClearingDirection
    {
        public const string Outward = "Outward";
        public const string Inward = "Inward";
    }

    public static class ClearingStatus
    {
        public const string Clearing = "Clearing";
        public const string Rejected = "Rejected";
    }
}
