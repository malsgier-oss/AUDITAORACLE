using FluentAssertions;
using Oracle.ManagedDataAccess.Client;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Integration;

public sealed class OracleCompatibilitySmokeTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;

    public OracleCompatibilitySmokeTests(OracleTestFixture fixture) => _fx = fixture;

    [SkippableFact]
    public void Migrate_ShouldApplyEventTimeCompatibilityChanges()
    {
        Skip.IfNot(_fx.IsAvailable);

        var migrationService = new MigrationService(_fx.ConnectionString!);
        migrationService.Migrate();
        migrationService.GetCurrentVersion().Should().BeGreaterThanOrEqualTo(52);

        using var conn = new OracleConnection(_fx.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = """
            SELECT data_type
              FROM user_tab_cols
             WHERE table_name = :tableName
               AND column_name = :columnName
            """;
        cmd.Parameters.Add(new OracleParameter("tableName", "AUDIT_LOG"));
        cmd.Parameters.Add(new OracleParameter("columnName", "EVENT_TIME"));
        var dataType = Convert.ToString(cmd.ExecuteScalar());
        dataType.Should().Be("TIMESTAMP");
    }

    [SkippableFact]
    public void StorageLayer_ShouldRoundTripAuditAndPaginationQueries()
    {
        Skip.IfNot(_fx.IsAvailable);

        var appConfig = new AppConfiguration
        {
            OracleConnectionString = _fx.ConnectionString!
        };
        var auditStore = new AuditLogStore(appConfig);
        var distributionStore = new ReportDistributionStore(appConfig);
        var docStore = _fx.DocumentStore;

        var id = docStore.Insert(new Document
        {
            FilePath = "smoke.pdf",
            Branch = Branches.Default,
            Section = "Smoke"
        });
        id.Should().BeGreaterThan(0);

        var auditEntry = new AuditLogEntry
        {
            Uuid = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow.ToString("O"),
            UserId = "smoke-user",
            Username = "smoke-user",
            UserRole = "Administrator",
            Action = AuditAction.DocumentViewed,
            Category = AuditCategory.Document,
            EntityType = "Document",
            EntityId = id.ToString(),
            Success = true
        };
        var auditId = auditStore.Insert(auditEntry);
        auditId.Should().BeGreaterThan(0);

        var auditPage = auditStore.Query(limit: 5, offset: 0);
        auditPage.Should().NotBeEmpty();
        auditPage.Should().Contain(e => e.Id == auditId);

        var distId = distributionStore.Log(
            reportPath: "report-smoke.pdf",
            reportType: "Executive",
            eventType: "View",
            userId: "smoke-user",
            username: "smoke-user");
        distId.Should().BeGreaterThan(0);

        var distRows = distributionStore.List(limit: 5);
        distRows.Should().Contain(d => d.Id == distId);

        var docPage1 = docStore.ListDocuments(limit: 1, offset: 0);
        var docPage2 = docStore.ListDocuments(limit: 1, offset: 1);
        docPage1.Count.Should().BeLessOrEqualTo(1);
        docPage2.Count.Should().BeLessOrEqualTo(1);
    }
}
