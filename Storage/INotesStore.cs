using WorkAudit.Domain;

namespace WorkAudit.Storage;

/// <summary>
/// Storage interface for managing enhanced notes with categorization and metadata.
/// </summary>
public interface INotesStore
{
    /// <summary>
    /// Adds a new note to the database.
    /// </summary>
    Note Add(Note note);

    /// <summary>
    /// Retrieves a note by its numeric ID.
    /// </summary>
    Note? Get(int id);

    /// <summary>
    /// Retrieves a note by its UUID.
    /// </summary>
    Note? GetByUuid(string uuid);

    /// <summary>
    /// Retrieves all notes associated with a specific document (by document ID).
    /// </summary>
    List<Note> GetByDocumentId(int documentId);

    /// <summary>
    /// Retrieves all notes associated with a specific document (by document UUID).
    /// </summary>
    List<Note> GetByDocumentUuid(string documentUuid);

    /// <summary>
    /// Lists notes with pagination support.
    /// </summary>
    List<Note> List(int limit = 1000, int offset = 0);

    /// <summary>
    /// Searches for notes with optional filters.
    /// </summary>
    List<Note> Search(
        string? type = null,
        string? severity = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? createdBy = null,
        int limit = 1000);

    /// <summary>
    /// Updates an existing note.
    /// </summary>
    bool Update(Note note);

    /// <summary>
    /// Deletes a note by ID.
    /// </summary>
    bool Delete(int id);

    /// <summary>
    /// Gets the count of notes for a single document.
    /// </summary>
    int GetCountByDocument(int documentId);

    /// <summary>
    /// Gets note counts for multiple documents in a single query (performance critical for dashboard).
    /// Returns dictionary mapping DocumentId -> Note Count.
    /// </summary>
    Dictionary<int, int> GetCountsByDocuments(List<int> documentIds);

    /// <summary>
    /// Loads all Issue-type notes for the given document IDs (batched SQL). Unlike <see cref="Search"/> with a global limit,
    /// every scoped document's issue notes are included.
    /// </summary>
    Dictionary<int, List<Note>> GetIssueNotesByDocumentIds(IReadOnlyList<int> documentIds);
}
