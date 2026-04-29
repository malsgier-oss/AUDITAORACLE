using Oracle.ManagedDataAccess.Client;

namespace WorkAudit.Core.Backup;

/// <summary>
/// Runs Oracle Data Pump (expdp/impdp) as external processes. Injectable for tests.
/// </summary>
public interface IOracleBackupGateway
{
    Task<OraclePumpOperationResult> ExportSchemaAsync(OraclePumpExportRequest request, CancellationToken cancellationToken = default);

    Task<OraclePumpOperationResult> ImportSchemaAsync(OraclePumpImportRequest request, CancellationToken cancellationToken = default);
}

public sealed class OraclePumpExportRequest
{
    public required string ConnectionString { get; init; }

    /// <summary>Oracle schema / user name (same as expdp SCHEMAS=).</summary>
    public required string SchemaName { get; init; }

    /// <summary>Oracle DIRECTORY object name (server-side).</summary>
    public required string OracleDirectoryName { get; init; }

    public required string DumpFileName { get; init; }

    public required string LogFileName { get; init; }

    /// <summary>Full path to expdp.exe, or null to resolve from PATH.</summary>
    public string? ExpdpExecutablePath { get; init; }

    /// <summary>Working directory for the process (logs on client if expdp writes there — usually server only).</summary>
    public string? WorkingDirectory { get; init; }
}

public sealed class OraclePumpImportRequest
{
    public required string ConnectionString { get; init; }

    public required string SchemaName { get; init; }

    public required string OracleDirectoryName { get; init; }

    public required string DumpFileName { get; init; }

    public required string LogFileName { get; init; }

    public string? ImpdpExecutablePath { get; init; }

    public string? WorkingDirectory { get; init; }

    /// <summary>When true, adds TABLE_EXISTS_ACTION=REPLACE for schema re-import.</summary>
    public bool ReplaceExistingObjects { get; init; } = true;
}

public sealed class OraclePumpOperationResult
{
    public bool Success { get; init; }

    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = "";

    public string StandardError { get; init; } = "";

    public string? ErrorMessage { get; init; }

    public static OraclePumpOperationResult Ok(int exitCode, string stdout, string stderr) =>
        new() { Success = true, ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };

    public static OraclePumpOperationResult Fail(string message, int exitCode = -1, string stdout = "", string stderr = "") =>
        new() { Success = false, ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr, ErrorMessage = message };
}

/// <summary>
/// Parses ODP.NET connection strings for Data Pump userid= arguments.
/// </summary>
public static class OracleBackupConnectionParser
{
    public static bool TryParse(string connectionString, out string userId, out string? password, out string dataSource)
    {
        userId = "";
        password = null;
        dataSource = "";
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var b = new OracleConnectionStringBuilder(connectionString);
            userId = (b.UserID ?? "").Trim();
            password = string.IsNullOrEmpty(b.Password) ? null : b.Password;
            dataSource = (b.DataSource ?? "").Trim();
            return !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(dataSource);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds expdp/impdp USERID= value. Password special characters are escaped per Oracle CLI rules.</summary>
    public static string BuildUserIdArgument(string userId, string? password, string dataSource)
    {
        var safeUser = EscapeForOracleCli(userId);
        var safeDs = dataSource.Contains(' ', StringComparison.Ordinal) ? $"\"{dataSource.Replace("\"", "\"\"")}\"" : dataSource;
        if (string.IsNullOrEmpty(password))
            return $"{safeUser}@{safeDs}";

        return $"{safeUser}/{EscapePasswordForUserid(password)}@{safeDs}";
    }

    private static string EscapeForOracleCli(string value) =>
        value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string EscapePasswordForUserid(string password)
    {
        // Wrap password in double quotes if it has special chars; double internal quotes.
        if (password.IndexOfAny(['/', '@', ' ', '"', ';']) >= 0)
            return $"\"{password.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        return password;
    }
}
