namespace WorkAudit.Storage.Oracle.Migrations;

internal static class OracleMigrationRegistry
{
    public static IReadOnlyList<IOracleMigration> GetOrderedMigrations()
    {
        // Add future migrations here in ascending version order.
        return
        [
            new Migration_050_NormalizeRequiredDocumentText()
        ];
    }
}
