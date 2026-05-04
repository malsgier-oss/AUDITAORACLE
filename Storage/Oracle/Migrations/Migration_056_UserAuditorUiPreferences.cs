using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage.Oracle.Migrations;

/// <summary>Per-auditor UI preferences (webcam + keyboard shortcuts) stored as JSON in Oracle.</summary>
internal sealed class Migration_056_UserAuditorUiPreferences : IOracleMigration
{
    public int Version => 56;

    public string Name => "User auditor UI preferences table";

    public void Apply(OracleConnection connection, OracleTransaction transaction, ILogger log)
    {
        using (var cmd = OracleSql.CreateCommand(connection,
                   """
                   CREATE TABLE user_auditor_ui_preferences (
                     user_uuid VARCHAR2(64) NOT NULL,
                     role VARCHAR2(64) NOT NULL,
                     preferences_json CLOB NOT NULL,
                     updated_at TIMESTAMP(6) NOT NULL,
                     CONSTRAINT pk_user_auditor_ui_prefs PRIMARY KEY (user_uuid, role),
                     CONSTRAINT fk_user_auditor_ui_prefs_user FOREIGN KEY (user_uuid) REFERENCES users (uuid)
                   )
                   """))
        {
            cmd.Transaction = transaction;
            try
            {
                cmd.ExecuteNonQuery();
                log.Information("Created user_auditor_ui_preferences");
            }
            catch (OracleException ex) when (ex.Number == 955)
            {
                log.Debug("user_auditor_ui_preferences already exists");
            }
        }
    }
}
