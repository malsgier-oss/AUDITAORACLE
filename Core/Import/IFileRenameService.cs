using WorkAudit.Domain;

namespace WorkAudit.Core.Import;

/// <summary>
/// Service for renaming and moving document files when classification changes.
/// </summary>
public interface IFileRenameService
{
    /// <summary>
    /// Renames and moves the document file to Branch/Section/DocType/ with name {DocType}_{yyyyMMdd}_{n}.ext (n = next sequence in folder).
    /// Only runs if rename_file_on_classify is true and file is under baseDir.
    /// </summary>
    /// <param name="doc">Document with current FilePath and classification (Branch, Section).</param>
    /// <param name="newType">New document type (e.g. Cash Deposit Slip).</param>
    /// <param name="baseDir">Base directory for documents (e.g. WORKAUDIT_Docs).</param>
    /// <returns>True if file was moved and doc.FilePath was updated; false otherwise.</returns>
    bool TryRenameAndMoveForClassification(Document doc, string newType, string baseDir);

    /// <summary>
    /// Same as <see cref="TryRenameAndMoveForClassification(Document,string,string)"/>, but returns a user-friendly failure reason.
    /// </summary>
    bool TryRenameAndMoveForClassification(Document doc, string newType, string baseDir, out string? failureReason);
    
    /// <summary>
    /// Attempts to rename and move a document file for classification, tracking the original path for potential rollback.
    /// </summary>
    /// <param name="doc">Document with current FilePath and classification.</param>
    /// <param name="newType">New document type.</param>
    /// <param name="baseDir">Base directory for documents.</param>
    /// <param name="originalPath">Returns the original file path before the move (for rollback purposes).</param>
    /// <param name="failureReason">Returns the failure reason if unsuccessful.</param>
    /// <returns>True if file was moved successfully; false otherwise.</returns>
    bool TryRenameAndMoveForClassification(Document doc, string newType, string baseDir, out string? originalPath, out string? failureReason);
    
    /// <summary>
    /// Rolls back a file move by moving it from the current path back to the original path.
    /// Used when a classification operation needs to be rolled back after a partial failure.
    /// </summary>
    /// <param name="currentPath">Current location of the file.</param>
    /// <param name="originalPath">Original location to restore the file to.</param>
    /// <returns>True if rollback was successful; false otherwise.</returns>
    bool RollbackRename(string currentPath, string originalPath);
}
