namespace WorkAudit.Domain;

/// <summary>
/// Stable identities for notes that are not tied to a real audit document.
/// Oracle treats empty VARCHAR2 binds as NULL, so journal rows must use a non-empty UUID
/// and a matching synthetic <c>documents</c> row (see migration 057).
/// </summary>
public static class NoteAnchors
{
    /// <summary>UUID of the synthetic document row used as the parent for daily journal notes.</summary>
    public const string JournalDocumentUuid = "00000000-0000-0000-0000-00000000DA11";

    /// <summary>True when <paramref name="uuid"/> is the journal anchor document (not a user-facing audit document).</summary>
    public static bool IsJournalAnchorDocument(string? uuid) =>
        !string.IsNullOrEmpty(uuid) &&
        string.Equals(uuid.Trim(), JournalDocumentUuid, StringComparison.OrdinalIgnoreCase);
}
