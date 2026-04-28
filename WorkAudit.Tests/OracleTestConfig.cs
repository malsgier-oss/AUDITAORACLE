namespace WorkAudit.Tests;

/// <summary>Connect integration tests to an Oracle test schema (set in CI or local dev).</summary>
public static class OracleTestConfig
{
    public const string EnvKey = "WORKAUDIT_TEST_ORACLE";

    /// <summary>ODP connection string, or null if tests should be skipped.</summary>
    public static string? GetConnectionString() => Environment.GetEnvironmentVariable(EnvKey);
}
