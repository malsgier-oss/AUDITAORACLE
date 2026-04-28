using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>Forward-only Oracle schema/data migration.</summary>
public interface IOracleMigration
{
    int Version { get; }
    string Name { get; }
    void Apply(OracleConnection connection, ILogger log);
}
