# Document Path Synchronization Fix

## Problem Summary

When setting document types in the Processing tab, files were successfully moved and renamed on disk, but the database records were not always updated with the new file paths. This caused documents to appear as "Unknown" in the workspace with no content.

## Root Cause

The `ClassificationPathHelper.ApplyMovesForDocumentsSharingFiles` method was calling `store.UpdateDocumentFilePath()` but not checking the return value. If the database update failed, the file would be in the new location but the database would still have the old/empty path.

## Fixes Implemented

### 1. Database Update Verification (ClassificationPathHelper.cs)

**Location**: `Core\Import\ClassificationPathHelper.cs` (lines 72-95)

**Change**: Now checks the return value of `UpdateDocumentFilePath()` and adds documents to the failure list if the database update fails.

**Impact**: Ensures that file moves are only considered successful if BOTH the file move AND database update succeed.

### 2. Improved Error Reporting (ProcessingView.xaml.cs)

**Location**: `Views\ProcessingView.xaml.cs` (lines 1195-1211)

**Change**: 
- Now shows a **Warning** icon (⚠️) instead of Information when any file rename/move failures occur
- The detailed failure message is always displayed when there are issues
- Makes it immediately clear to the user that some documents may have problems

**Impact**: Users will be alerted when database synchronization fails, preventing silent data corruption.

### 3. Pre-Validation Before Moving to Workspace (ProcessingView.xaml.cs)

**Location**: `Views\ProcessingView.xaml.cs` (lines 455-530)

**Change**: 
- Validates that all documents have valid file paths before moving to workspace
- Checks that files actually exist on disk
- If invalid paths are found, user is prompted to:
  - Skip those documents and move the others
  - Or cancel the operation
- Prevents documents with missing files from being moved to workspace

**Impact**: Broken documents are caught before they reach the workspace, preventing user confusion.

### 4. Document Path Repair Service (NEW)

**Location**: `Core\Helpers\DocumentPathRepairService.cs`

**New utility class** that provides:

#### Methods:
- `FindDocumentsWithInvalidPaths()` - Scans database for documents with missing/invalid paths
- `TryReconstructFilePath(doc, baseDir)` - Reconstructs expected file path from document metadata
- `RepairDocument(doc, correctPath)` - Updates database with correct file path
- `RepairAllInvalidPaths(baseDir)` - Automated repair of all broken documents

#### How it works:
1. Finds documents where `file_path` is empty or points to non-existent file
2. Reconstructs expected path using document metadata (type, section, branch, date)
3. Searches target directory for matching files
4. Verifies match using file hash if available
5. Updates database with correct path

### 5. UI Button for Manual Repair (ProcessingView)

**Location**: Processing tab sidebar, Actions section

**New button**: "Repair broken links"

**What it does**:
1. Shows confirmation dialog explaining the repair process
2. Runs `DocumentPathRepairService.RepairAllInvalidPaths()`
3. Displays detailed results:
   - Total documents with invalid paths
   - Successfully repaired count
   - Documents where files could not be located
   - Documents that failed to update
4. Refreshes the view if any repairs were made

## How to Use

### For End Users

#### If you encounter a document with missing content:

1. **Option A - Use the Repair Button**:
   - Go to the **Processing** tab
   - Click **"Repair broken links"** in the sidebar (Actions section)
   - Confirm the repair operation
   - Review the results
   - The document should now appear correctly in Workspace

2. **Option B - Manual Fix**:
   - Note the document ID from the workspace
   - Use File Explorer to locate the actual file (it should be in the correct folder based on type/section/date)
   - The repair service will automatically match it

#### Prevention:

Going forward, the system will:
- **Alert you immediately** if any file renames fail during type classification
- **Prevent** documents with invalid paths from being moved to workspace
- **Verify** database updates after every file move operation

### For Developers

#### To run repair programmatically:

```csharp
var store = ServiceContainer.GetService<IDocumentStore>();
var repairService = new DocumentPathRepairService(store);

// Find all broken documents
var brokenDocs = repairService.FindDocumentsWithInvalidPaths();

// Repair all
var result = repairService.RepairAllInvalidPaths(baseDirectory);

Console.WriteLine($"Repaired: {result.Repaired}/{result.TotalInvalid}");
```

#### To repair a specific document:

```csharp
var doc = store.GetResult(documentId).Value;
var correctPath = repairService.TryReconstructFilePath(doc, baseDirectory);
if (correctPath != null)
{
    repairService.RepairDocument(doc, correctPath);
}
```

## Testing

### Verify the Fix Works:

1. **Test validation**: Try to move a document with invalid path to workspace
   - Expected: Warning message appears, option to skip
   
2. **Test error reporting**: Set type for a document where file move fails
   - Expected: Warning icon (⚠️) with detailed error message
   
3. **Test repair**: 
   - Create a document with invalid path in database
   - Run "Repair broken links"
   - Expected: Document path is corrected

### Check for Regressions:

1. Normal workflow should still work:
   - Import documents → Set type → Move to workspace
   - All should work without warnings if files are valid

## Files Modified

1. `Core\Import\ClassificationPathHelper.cs` - Added database update verification
2. `Views\ProcessingView.xaml.cs` - Added validation and improved error reporting
3. `Views\ProcessingView.xaml` - Added repair button to UI
4. `Core\Helpers\DocumentPathRepairService.cs` - NEW repair utility class

## Migration Notes

- **No database schema changes** required
- **No breaking changes** to existing APIs
- **Backward compatible** with existing documents
- The repair service can fix documents corrupted by previous versions

## Future Enhancements (Optional)

1. **Automatic repair on startup**: Run repair service during application startup
2. **Background monitoring**: Periodically check for broken documents
3. **Transaction support**: Wrap file move + DB update in a transaction
4. **Workspace repair button**: Add repair capability directly in workspace view
5. **Detailed logging**: Enhanced logging for troubleshooting file operations

---

## Quick Reference

**Problem**: Document shows as "Unknown" with no content in workspace  
**Cause**: Database not updated after file was moved  
**Fix**: Click "Repair broken links" in Processing tab  
**Prevention**: System now validates and alerts before this happens
