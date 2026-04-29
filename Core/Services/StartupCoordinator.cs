using System;
using System.IO;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Security;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public sealed class StartupBootResult
{
    public bool Success { get; init; }
    public string ErrorCode { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
    public string BaseDirectory { get; init; } = "";
    public string OracleConnectionString { get; init; } = "";
}

/// <summary>
/// Coordinates deterministic startup bootstrapping before UI login flow.
/// Keeps configuration, migration, and service initialization out of App startup method.
/// </summary>
public sealed class StartupCoordinator
{
    private readonly ILogger _log = LoggingService.ForContext<StartupCoordinator>();

    public StartupBootResult Initialize(
        Func<string> promptForConnectionString,
        Func<string, string> resolveOracleConnectionString,
        Action<string> ensureArchiveSchema)
    {
        try
        {
            var baseDir = ResolveAndEnsureBaseDirectory();
            var oracleConnectionString = ResolveOracleConnectionString(promptForConnectionString);
            var effectiveOracleConnectionString = resolveOracleConnectionString(oracleConnectionString);

            ServiceContainer.Initialize(effectiveOracleConnectionString, baseDir);
            _log.Information("Service container initialized (Base: {BaseDir})", baseDir);

            var migrationService = ServiceContainer.GetService<IMigrationService>();
            migrationService.Migrate();
            _log.Information("Database migrations applied, version {Version}", migrationService.GetCurrentVersion());

            ensureArchiveSchema(effectiveOracleConnectionString);
            _ = ServiceContainer.GetService<IEnvironmentService>().GetEnvironmentInfo();

            return new StartupBootResult
            {
                Success = true,
                BaseDirectory = baseDir,
                OracleConnectionString = effectiveOracleConnectionString
            };
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Oracle startup bootstrap failed");
            return new StartupBootResult
            {
                Success = false,
                ErrorCode = "BOOT_ORACLE",
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Startup bootstrap failed");
            return new StartupBootResult
            {
                Success = false,
                ErrorCode = "BOOT_GENERAL",
                ErrorMessage = ex.Message
            };
        }
    }

    private static string ResolveAndEnsureBaseDirectory()
    {
        var envBaseDir = Environment.GetEnvironmentVariable("WORKAUDIT_BASE_DIR");
        var userBaseDir = UserSettings.Get<string>("base_directory");
        var preferredBase = !string.IsNullOrWhiteSpace(envBaseDir) ? envBaseDir.Trim() : userBaseDir;
        var baseDir = Defaults.ResolveBaseDirectory(preferredBase);

        if (string.IsNullOrWhiteSpace(envBaseDir) &&
            !string.IsNullOrWhiteSpace(userBaseDir) &&
            !Defaults.IsBaseDirectoryAccessible(userBaseDir))
        {
            UserSettings.Set("base_directory", baseDir);
        }

        try
        {
            Directory.CreateDirectory(baseDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Log.Warning(ex, "Could not create base directory {Path}; falling back to default documents folder", baseDir);
            baseDir = Defaults.GetDefaultBaseDir();
            Directory.CreateDirectory(baseDir);
            if (string.IsNullOrWhiteSpace(envBaseDir))
                UserSettings.Set("base_directory", baseDir);
        }

        return baseDir;
    }

    private static string ResolveOracleConnectionString(Func<string> promptForConnectionString)
    {
        var envOracle = Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION")
            ?? Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONN")
            ?? Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("WORKAUDIT_TEST_ORACLE");
        var userOracle = UserSettings.Get<string>("oracle_connection_string");
        var oracleConnectionString = !string.IsNullOrWhiteSpace(envOracle)
            ? envOracle.Trim()
            : (userOracle ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(oracleConnectionString))
            return oracleConnectionString;

        oracleConnectionString = promptForConnectionString();
        if (string.IsNullOrWhiteSpace(oracleConnectionString))
            throw new InvalidOperationException("Oracle connection string is not configured.");

        UserSettings.Set("oracle_connection_string", oracleConnectionString);
        UserSettings.Set("first_run_completed", true);
        return oracleConnectionString;
    }
}
