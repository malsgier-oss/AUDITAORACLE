using Oracle.ManagedDataAccess.Client;
using WorkAudit.Core.Reports;
using WorkAudit.Storage;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Tests.Fixtures;

/// <summary>Real Oracle (set <see cref="OracleTestConfig.EnvKey"/>). Skippable if unset.</summary>
public sealed class OracleTestFixture : IDisposable
{
    public string? ConnectionString { get; }
    public bool IsAvailable => !string.IsNullOrEmpty(ConnectionString);
    public int User1Id { get; }
    public int User2Id { get; }
    public DocumentStore DocumentStore { get; }
    public ReportTemplateStore ReportTemplateStore { get; }
    public IReportBuilderService ReportBuilder { get; }
    public OracleConnection? Connection { get; }

    public OracleTestFixture()
    {
        ConnectionString = OracleTestConfig.GetConnectionString();
        if (string.IsNullOrEmpty(ConnectionString))
        {
            User1Id = User2Id = 0;
            DocumentStore = null!;
            ReportTemplateStore = null!;
            ReportBuilder = null!;
            return;
        }

        new MigrationService(ConnectionString).Migrate();

        DocumentStore = new DocumentStore(ConnectionString, "test");
        ReportTemplateStore = new ReportTemplateStore(ConnectionString);
        ReportBuilder = new ReportBuilderService(ReportTemplateStore, DocumentStore);

        var conn = new OracleConnection(ConnectionString);
        conn.Open();
        Connection = conn;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var u1 = "t1_" + suffix;
        var u2 = "t2_" + suffix;

        InsertUser(conn, u1, "admin");
        InsertUser(conn, u2, "user");

        User1Id = GetUserId(conn, u1);
        User2Id = GetUserId(conn, u2);
    }

    private static void InsertUser(OracleConnection conn, string username, string role)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (uuid, username, display_name, password_hash, email, role, created_at)
            VALUES (@uuid, @un, 'Test User', 'hash', 't@test.com', @role, '2024-01-01T00:00:00Z')
            """;
        cmd.BindByName = true;
        cmd.Parameters.AddWithValue("uuid", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("un", username);
        cmd.Parameters.AddWithValue("role", role);
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
        cmd.ExecuteNonQuery();
    }

    private static int GetUserId(OracleConnection conn, string username)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM users WHERE username = @un";
        cmd.BindByName = true;
        cmd.Parameters.AddWithValue("un", username);
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public void Dispose() => Connection?.Dispose();
}
