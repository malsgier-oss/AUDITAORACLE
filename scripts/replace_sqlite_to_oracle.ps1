$files = @(
  "g:/WorkAudit.CSharpOracle/Storage/AuditLogStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ChangeHistoryService.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ConfigStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/DocumentAssignmentStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/DocumentStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/IntegrityService.cs",
  "g:/WorkAudit.CSharpOracle/Storage/MarkupStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/NotesStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/NotificationStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ReportAttestationStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ReportDistributionStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ReportDraftStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ReportHistoryStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/ReportTemplateStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/TeamTaskStore.cs",
  "g:/WorkAudit.CSharpOracle/Storage/UserStore.cs",
  "g:/WorkAudit.CSharpOracle/App.xaml.cs",
  "g:/WorkAudit.CSharpOracle/Core/Import/ClassificationPathHelper.cs"
)
foreach ($p in $files) {
  if (-not (Test-Path $p)) { Write-Host "skip $p"; continue }
  $c = [IO.File]::ReadAllText($p)
  $c = $c.Replace('using Microsoft.Data.Sqlite;', 'using Oracle.ManagedDataAccess.Client;')
  $c = $c.Replace('SqliteConnection', 'OracleConnection')
  $c = $c.Replace('SqliteCommand', 'OracleCommand')
  $c = $c.Replace('SqliteParameter', 'OracleParameter')
  $c = $c.Replace('SqliteDataReader', 'OracleDataReader')
  $c = $c.Replace('SqliteException', 'OracleException')
  $c = $c.Replace('SqliteTransaction', 'OracleTransaction')
  [IO.File]::WriteAllText($p, $c)
  Write-Host "ok $p"
}
