# Legacy: Database classification mismatch debugging

This document captures a legacy troubleshooting workflow for historical per-installation database builds.
Current production builds are Oracle 19c and are configured via `WORKAUDIT_ORACLE_CONNECTION`.

For Oracle instances, use SQL Developer, SQL*Plus, or your preferred Oracle client to query the `documents` table and compare the results with on-disk paths.

`app_settings` / service diagnostics in [`App.xaml.cs`](../App.xaml.cs) resolve the configured Oracle endpoint directly at startup.

## Useful queries

Replace placeholders (`?`, folder segments, filename stem) for your case.

### One document by id

```sql
SELECT id, uuid, document_type, section, file_path, extracted_date, updated_at
FROM documents
WHERE id = ?;
```

### Rows whose stored path mentions a classified folder segment

Adjust `MoneyGram` / date folder as needed:

```sql
SELECT id, document_type, section, file_path, extracted_date
FROM documents
WHERE file_path LIKE '%MoneyGram%2026-04-20%'
ORDER BY id;
```

### Rows sharing a filename stem (duplicate or collision checks)

```sql
SELECT id, document_type, file_path
FROM documents
WHERE file_path LIKE '%YourFileStem%'
ORDER BY id;
```

## How to read results

- If **`file_path`** includes the MoneyGram (or other type) folder but **`document_type`** is still null, empty, legacy **Other**, or **Unclassified**, the row may be out of sync with the on-disk layout. Historically this often occurred in older schemas; the UI shows the localized “Unclassified” label. After app fixes, [`ClassificationPathHelper`](../Core/Import/ClassificationPathHelper.cs) re-persists **`document_type`** for every document id in the same resolved-file group when a rename succeeds.
- If two **`id`** values once shared the same logical file, compare **`file_path`** and **`document_type`** for both.

**Filters:** Processing and Workspace type filters include **`Unclassified`** for unclassified-only rows; SQL also treats legacy **`Other`** as unclassified for older databases.

## Related code

- Rename / folder layout: [`FileRenameService`](../Core/Import/FileRenameService.cs)
- DB updates for type vs path: [`DocumentStore`](../Storage/DocumentStore.cs) (`UpdateDocumentType`, `UpdateDocumentFilePath`)
