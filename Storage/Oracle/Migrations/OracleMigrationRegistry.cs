namespace WorkAudit.Storage.Oracle.Migrations;

internal static class OracleMigrationRegistry
{
    public static IReadOnlyList<IOracleMigration> GetOrderedMigrations()
    {
        // Add future migrations here in ascending version order.
        return
        [
            new Migration_050_NormalizeRequiredDocumentText(),
            new Migration_051_NormalizeEventTimeColumns(),
            new Migration_052_OracleBackupAppSettings(),
            new Migration_053_SchedulerLeaderElection(),
            new Migration_054_RemoveObsoleteAiSettings(),
            new Migration_055_CleanupObsoleteAiSettings(),
            new Migration_056_UserAuditorUiPreferences(),
            new Migration_057_JournalAnchorDocument(),
            new Migration_058_ReportHistoryGeneratedAtTimestamp()
        ];
    }
}
