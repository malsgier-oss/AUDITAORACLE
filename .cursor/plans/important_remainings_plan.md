---
name: Important Remainings Plan
overview: "Complete the three important remaining items from the audit implementation: pilot Result pattern at key call sites, add MVVM XAML bindings in DashboardView, and wire WorkspaceView to use its ViewModel for document loading."
todos:
  - id: result_pilot_call_sites
    content: Update 3-5 critical call sites to use GetResult/InsertResult and show result.Error in UI
    status: completed
  - id: dashboard_xaml_bindings
    content: Bind DashboardView Refresh button and TimeRange combo to ViewModel (Command + SelectedTimeRange)
    status: completed
  - id: workspace_viewmodel_loading
    content: Use WorkspaceViewModel.LoadDocuments/SetDocuments and bind DocumentList to CurrentDocuments
    status: completed
isProject: false
---

# Plan: Important Remainings

**Status:** All three items completed (see **Completion summary** at the bottom).

This plan covered three high-value items from the audit implementation: error visibility (Result), MVVM on the dashboard (bindings + reload), and consistency (WorkspaceView using its ViewModel).

---

## 1. Result Pattern Pilot at Call Sites

**Goal:** Have 3–5 critical paths use `GetResult` / `InsertResult` (and optionally `UpdateResult` / `DeleteResult`) and surface `result.Error` to the user instead of silent null/false.

**Why it matters:** Callers today use `Get()` and only see “document not found” or no feedback. Using Result lets the UI show a clear message (e.g. “Database error: …”) and ensures failures are logged.

**Targets (pick 3–5):**

| Location | Current | Change to |
|----------|--------|-----------|
| [Views/DashboardView.xaml.cs](Views/DashboardView.xaml.cs) | `_store.Get(note.DocumentId)` (lines 332, 360, 733) | `_store.GetResult(id)`; if `!result.IsSuccess` show MessageBox + log, else `doc = result.Value` |
| [Core/Import/ImportService.cs](Core/Import/ImportService.cs) | `_documentStore.Get(documentId)` (line 172) | Use `GetResult`; on failure return/throw or propagate error to caller |
| [Views/AuditorDashboardView.xaml.cs](Views/AuditorDashboardView.xaml.cs) | Multiple `_store.Get(...)` (e.g. 380, 597, 1006) | Use `GetResult` in 1–2 key flows (e.g. open document by ID), show `result.Error` in UI |
| [Views/Admin/AssignmentManagementView.xaml.cs](Views/Admin/AssignmentManagementView.xaml.cs) | `_documentStore.Get(...)` (95, 137, 286) | Use `GetResult` in the path that opens a document; show error if failure |
| [Core/Import/ImportService.cs](Core/Import/ImportService.cs) | `_documentStore.Insert(doc)` (353, 419) | Use `InsertResult`; on failure log and return/surface error to caller |

**Implementation pattern (everywhere):**

```csharp
var result = _store.GetResult(id);
if (!result.IsSuccess)
{
    _log.Warning("Could not load document {Id}: {Error}", id, result.Error);
    MessageBox.Show($"Failed to load document: {result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    return;
}
var doc = result.Value!;
```

**Out of scope for this plan:** Converting every `Get`/`Insert` caller in the solution; only the chosen pilot sites.

---

## 2. DashboardView XAML Bindings (MVVM)

**Goal:** Drive Refresh and time range from the ViewModel via bindings so the View does not depend on code-behind for those actions.

**Current state:** [Views/DashboardView.xaml](Views/DashboardView.xaml) uses `Click="RefreshBtn_Click"` and `SelectionChanged="TimeRangeCombo_Changed"`. The ViewModel already has `RefreshCommand` and could expose `SelectedTimeRange` (string).

**Tasks:**

1. **DashboardViewModel**
   - Add a writable property: `string SelectedTimeRange` (e.g. default `"This Month"`) with `SetProperty`.
   - When `SelectedTimeRange` is set, optionally call `LoadDataAsync(SelectedTimeRange)` so changing the combo refreshes data (or keep refresh explicit and only sync combo → ViewModel).
   - Ensure `RefreshCommand` still calls `LoadDataAsync(SelectedTimeRange)` so Refresh uses the current selection.

2. **DashboardView.xaml**
   - **Refresh button:** Replace `Click="RefreshBtn_Click"` with `Command="{Binding RefreshCommand}"`. Optionally bind `IsEnabled` to `IsLoading` via converter (e.g. `{Binding IsLoading, Converter={StaticResource InvertBool}}`) so the button is disabled while loading.
   - **Time range combo:** Bind `SelectedItem` to ViewModel:
     - Use `SelectedItem="{Binding SelectedTimeRange, Mode=TwoWay}"` if the combo’s items are strings and the ViewModel holds the same string.
     - If the combo is filled in code-behind with items, keep filling in code-behind but in `TimeRangeCombo_Changed` set `_viewModel.SelectedTimeRange = TimeRangeCombo.SelectedItem?.ToString()` and in constructor/loaded set `TimeRangeCombo.SelectedItem` from `_viewModel.SelectedTimeRange` so they stay in sync; or switch to a binding that works with the current Items source (e.g. bind `SelectedIndex` to a ViewModel index and map index ↔ string in ViewModel).

3. **DashboardView.xaml.cs**
   - Remove or simplify `RefreshBtn_Click` (no-op if command is bound).
   - Keep `TimeRangeCombo_Changed` only if you need to trigger a full reload when the range changes (e.g. call `LoadDashboardData()` or `_viewModel.LoadDataAsync(_viewModel.SelectedTimeRange)`); otherwise rely on ViewModel + binding.

**Acceptable scope:** Refresh via Command binding is required; time range can be “binding + one-way sync from ViewModel” or “combo change still triggers load in code-behind but ViewModel.SelectedTimeRange is updated” so that the ViewModel always reflects the selected range.

---

## 3. WorkspaceView Using ViewModel for Document Loading

**Goal:** Have WorkspaceView use `WorkspaceViewModel.LoadDocuments` and `SetDocuments` for all document list updates, and bind `DocumentList.ItemsSource` to `ViewModel.CurrentDocuments`, so the list is owned by the ViewModel.

**Current state:** [Views/WorkspaceView.xaml.cs](Views/WorkspaceView.xaml.cs) uses `_currentDocuments` and sets `DocumentList.ItemsSource = _currentDocuments` in several places (e.g. RunFilteredSearch, LoadMyAssignments, folder selection). The ViewModel is set as DataContext but not used for loading.

**Tasks:**

1. **Obtain ViewModel reference**
   - In constructor, store the resolved ViewModel: `_workspaceViewModel = (WorkspaceViewModel)DataContext;` (or a dedicated field set from `ServiceContainer.GetService<WorkspaceViewModel>()` before setting DataContext).

2. **RunFilteredSearch (and ApplyFiltersToDocuments path)**
   - Replace:
     - `_currentDocuments = _store.ListDocuments(branch: ..., section: ..., ...);`
     - `DocumentList.ItemsSource = _currentDocuments;`
   - With:
     - `_workspaceViewModel.LoadDocuments(branch, section, docType, dateFrom, dateTo);`
     - `DocumentList.ItemsSource = _workspaceViewModel.CurrentDocuments;` (can be set once in Loaded if ItemsSource is not cleared elsewhere, or keep setting each time for clarity).

3. **LoadMyAssignments**
   - Replace:
     - `_currentDocuments = _store.GetByIds(docIds);`
     - `DocumentList.ItemsSource = _currentDocuments;`
   - With:
     - `_workspaceViewModel.SetDocuments(_store.GetByIds(docIds));`
     - `DocumentList.ItemsSource = _workspaceViewModel.CurrentDocuments;`

4. **Other assignments to _currentDocuments**
   - Folder / tree selection (lines ~271–272, ~457–458): load list then `_workspaceViewModel.SetDocuments(list)` and set `DocumentList.ItemsSource = _workspaceViewModel.CurrentDocuments`.
   - Empty list (lines ~496–498): `_workspaceViewModel.SetDocuments(new List<Document>());` and set ItemsSource to ViewModel’s collection.

5. **Replace _currentDocuments reads**
   - Any code that uses `_currentDocuments` (e.g. `_currentDocuments.FindIndex(d => d.Id == doc.Id)` around line 926) should use `_workspaceViewModel.CurrentDocuments` (or a local list built from it) so the source of truth is the ViewModel. Since `CurrentDocuments` is `ObservableCollection<Document>`, use a linear search or add a helper that gets index by id from the collection.

6. **Single place for ItemsSource (optional)**
   - In `OnLoaded` or constructor, set `DocumentList.ItemsSource = _workspaceViewModel.CurrentDocuments` once and never reassign; all load paths only call `LoadDocuments` or `SetDocuments` so the same collection is updated. Then you don’t need to set ItemsSource in every method.

**Edge cases:**
   - Remove or repurpose `_docToAssignment` if it’s still unused (plan noted it’s dead code).
   - Ensure selection and “current document” logic still work when the list is `ViewModel.CurrentDocuments` (e.g. selection changed handlers that use `DocumentList.SelectedItem` are unchanged).

---

## Order and Effort

| Order | Item | Effort |
|-------|------|--------|
| 1 | Result pilot (3–5 call sites) | ~30–45 min |
| 2 | Dashboard XAML bindings (RefreshCommand + SelectedTimeRange) | ~30 min |
| 3 | WorkspaceView ViewModel loading | ~45–60 min |

**Total:** about 2–2.5 hours.

Doing **1** first improves error handling quickly. **2** and **3** can be done in either order; **2** is smaller and makes the dashboard clearly MVVM-driven for refresh and time range.

---

## Definition of Done

- **Result pilot:** At least 3 call sites (e.g. DashboardView document open, ImportService Get/Insert, one of AuditorDashboardView/AssignmentManagementView) use `GetResult`/`InsertResult` and show `result.Error` in UI and log.
- **Dashboard bindings:** Refresh is triggered by `Command="{Binding RefreshCommand}"`; time range selection is reflected in ViewModel (and triggers reload if desired).
- **WorkspaceView ViewModel:** All document list updates go through `WorkspaceViewModel.LoadDocuments` or `SetDocuments`; `DocumentList.ItemsSource` is bound to `CurrentDocuments`; no remaining direct assignment to `_currentDocuments` for the main list (read usages can stay as `_workspaceViewModel.CurrentDocuments` or a local copy where index is needed).

---

## Completion summary

1. **Result pilot** — `GetResult` / `InsertResult` used on critical paths including `AuditorDashboardView` (assignments grid, branch filter, activity feed, mark complete, add note), `WorkspaceView.AuditorMarkup` (save / leave prompts / audit log), `AssignmentCalendarView`, plus existing coverage in `DashboardView`, `ImportService`, `AssignmentManagementView`, and `NavigateToDocument`.
2. **Dashboard MVVM** — `RefreshBtn` uses `RefreshCommand`; `TimeRangeCombo` uses `SelectedItem` TwoWay to `SelectedTimeRange`; reload on range change via `PropertyChanged` (no `SelectionChanged` handler); single UI refresh path through `DataLoadCompleted` → `ApplyDashboardDataToUi`; `_timeRangeReloadEnabled` avoids spurious reload before first load completes.
3. **Workspace** — `WorkspaceView` uses `WorkspaceViewModel.LoadDocuments` / `SetDocuments`; `DocumentList` uses `CollectionViewSource` over `CurrentDocuments` in `OnLoaded` (filter preserved). `_docToAssignment` remains in use for My Assignments flows.
