using System.Collections.Generic;

namespace WorkAudit.Core.Import;

/// <summary>Outcome of <see cref="ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles"/>.</summary>
public sealed class ClassificationMovesResult
{
    /// <summary>Number of distinct physical files successfully moved/renamed.</summary>
    public int DistinctFilesMoved { get; init; }

    /// <summary>Document IDs where the file path could not be resolved to an existing file (rename not attempted).</summary>
    public IReadOnlyList<int> UnresolvedPathDocumentIds { get; init; } = System.Array.Empty<int>();

    /// <summary>Optional per-document details for unresolved path cases.</summary>
    public IReadOnlyDictionary<int, string> UnresolvedPathReasons { get; init; } = new Dictionary<int, string>();

    /// <summary>Document IDs where rename was attempted but <see cref="IFileRenameService.TryRenameAndMoveForClassification"/> returned false.</summary>
    public IReadOnlyList<int> RenameFailedDocumentIds { get; init; } = System.Array.Empty<int>();

    /// <summary>Optional per-document details for rename/move failures.</summary>
    public IReadOnlyDictionary<int, string> RenameFailedReasons { get; init; } = new Dictionary<int, string>();
}
