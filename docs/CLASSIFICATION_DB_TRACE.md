# Classification vs database trace

Use this when a file on disk sits under the classified folder tree (for example `MoneyGram\2026-04-20`) but the app still shows **Unclassified** (or inconsistent) metadata: compare SQLite `documents` rows to the real path.

## Where the SQLite database file is

Resolution order at startup is implemented in [`App.xaml.cs`](../App.xaml.cs) (database path block):

1. **`WORKAUDIT_DATABASE_PATH`** environment variable (if set, wins over user setting).
2. **User override** `database_path` from Cursor/user settings storage (see Control Panel “Database path”; persisted for the app).
3. **Default:** `{baseDirectory}\workaudit.db`

`baseDirectory` comes from configured **base directory** (e.g. `C:\ProgramData\Audita` on many installs), with fallback to [`Defaults.GetDefaultBaseDir()`](../Config/Defaults.cs) (`Documents\WORKAUDIT_Docs`) when needed.

**Quick ways to see the active path**

- **Control Panel** in the app: database path is shown (see [`ControlPanelWindow.xaml.cs`](../Views/Admin/ControlPanelWindow.xaml.cs)).
- **Default file** when no overrides: `{your base directory}\workaudit.db` next to (or under) the same tree as [`AppConfiguration.BaseDirectory`](../Core/Services/ServiceContainer.cs) / stored `base_directory`.

Open the file with **DB Browser for SQLite**, **sqlite3** CLI, or any SQLite client using:

`Data Source=<full path to workaudit.db>`

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

- If **`file_path`** includes the MoneyGram (or other type) folder but **`document_type`** is still null, empty, legacy **Other**, or **Unclassified**, the row may be out of sync with the on-disk layout. Unclassified documents normally use **NULL/empty** `document_type` in SQLite; the UI shows the localized “Unclassified” label. After app fixes, [`ClassificationPathHelper`](../Core/Import/ClassificationPathHelper.cs) re-persists **`document_type`** for every document id in the same resolved-file group when a rename succeeds.
- If two **`id`** values once shared the same logical file, compare **`file_path`** and **`document_type`** for both.

**Filters:** Processing and Workspace type filters include **`Unclassified`** for unclassified-only rows; SQL also treats legacy **`Other`** as unclassified for older databases.

## Related code

- Rename / folder layout: [`FileRenameService`](../Core/Import/FileRenameService.cs)
- DB updates for type vs path: [`DocumentStore`](../Storage/DocumentStore.cs) (`UpdateDocumentType`, `UpdateDocumentFilePath`)
