namespace WorkAudit.Core.Services;

/// <summary>Stable IDs for user-configurable shortcuts (stored in user_settings.json).</summary>
public static class KeyboardShortcutIds
{
    public const string ProcessingMerge = "Processing.Merge";
    public const string ProcessingMergeAlternate = "Processing.MergeAlternate";
    public const string ProcessingRefresh = "Processing.Refresh";
    public const string ProcessingSelectAllChecks = "Processing.SelectAllChecks";
    public const string ProcessingClearChecks = "Processing.ClearChecks";
    public const string ProcessingUncheckRow = "Processing.UncheckRow";
    public const string ProcessingGridSelectAll = "Processing.GridSelectAll";
    public const string ProcessingSetTypeSection = "Processing.SetTypeSection";
    public const string ProcessingDeleteSelected = "Processing.DeleteSelected";

    public static IReadOnlyList<string> All { get; } =
    [
        ProcessingMerge,
        ProcessingMergeAlternate,
        ProcessingRefresh,
        ProcessingSelectAllChecks,
        ProcessingClearChecks,
        ProcessingUncheckRow,
        ProcessingGridSelectAll,
        ProcessingSetTypeSection,
        ProcessingDeleteSelected
    ];
}
