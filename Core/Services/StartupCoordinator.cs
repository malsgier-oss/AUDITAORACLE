using System;
using System.IO;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using WorkAudit.Config;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Security;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

internal sealed class StartupConfigurationException : Exception
{
    public string ErrorCode { get; }

    public StartupConfigurationException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

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
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKAUDIT_BASE_DIR")))
            {
                _log.Warning(
                    "WORKAUDIT_BASE_DIR is not set: attachments use per-machine data under {BaseDir}. " +
                    "For multiple PCs sharing one Oracle schema, set WORKAUDIT_BASE_DIR to a UNC or shared drive so file_path resolves on every client.",
                    baseDir);
            }

        var oracleConnectionString = ResolveOracleConnectionString(promptForConnectionString);
            var effectiveOracleConnectionString = resolveOracleConnectionString(oracleConnectionString);
            EnsureOracleReachable(effectiveOracleConnectionString);

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
        catch (StartupConfigurationException ex)
        {
            _log.Error(ex, "Startup configuration failed ({ErrorCode})", ex.ErrorCode);
            return new StartupBootResult
            {
                Success = false,
                ErrorCode = ex.ErrorCode,
                ErrorMessage = ex.Message
            };
        }
        catch (OracleException ex)
        {
            _log.Error(ex, "Oracle startup bootstrap failed");
            return new StartupBootResult
            {
                Success = false,
                ErrorCode = "BOOT_ORACLE_UNREACHABLE",
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

    private static bool IsManagedOracleEnvRequired()
    {
        var flag = Environment.GetEnvironmentVariable("WORKAUDIT_REQUIRE_ORACLE_ENV");
        return flag != null &&
               (flag.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                flag.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                flag.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool LooksLikePlaceholderConnectionString(string connectionString)
    {
        return connectionString.Contains("change-me", StringComparison.OrdinalIgnoreCase) ||
               connectionString.Contains("yourpassword", StringComparison.OrdinalIgnoreCase) ||
               connectionString.Contains("password=***", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsValidOracleConnectionStringFormat(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        if (LooksLikePlaceholderConnectionString(connectionString))
            return false;

        try
        {
            var builder = new OracleConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.UserID) &&
                   !string.IsNullOrWhiteSpace(builder.DataSource);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureOracleConnectionStringFormat(string connectionString, string source)
    {
        if (IsValidOracleConnectionStringFormat(connectionString))
            return;

        throw new StartupConfigurationException(
            "BOOT_ORACLE_MALFORMED",
            $"Oracle connection string from {source} is invalid. Expected ODP.NET format with User Id and Data Source, and no placeholder values.");
    }

    private static void EnsureOracleReachable(string connectionString)
    {
        try
        {
            using var conn = new OracleConnection(connectionString);
            conn.Open();
        }
        catch (OracleException ex)
        {
            throw new StartupConfigurationException(
                "BOOT_ORACLE_UNREACHABLE",
                "Oracle endpoint is unreachable with the configured connection string. Verify listener/service reachability and credentials.",
                ex);
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
        var managedEnvRequired = IsManagedOracleEnvRequired();
        var envOracle = GetEnvironmentOracleConnectionString();
        var userOracle = UserSettings.GetSecure("oracle_connection_string");

        if (managedEnvRequired)
        {
            if (!string.IsNullOrWhiteSpace(envOracle))
            {
                EnsureOracleConnectionStringFormat(envOracle, "environment variable");
                return envOracle;
            }

            throw new StartupConfigurationException(
                "BOOT_ORACLE_ENV_REQUIRED",
                "Managed deployment requires WORKAUDIT_ORACLE_CONNECTION (or equivalent Oracle env variable) to be configured at machine scope.");
        }

        if (!string.IsNullOrWhiteSpace(userOracle))
        {
            EnsureOracleConnectionStringFormat(userOracle, "user settings");
            return userOracle!;
        }

        if (!string.IsNullOrWhiteSpace(envOracle))
        {
            EnsureOracleConnectionStringFormat(envOracle, "environment variable");
            return envOracle;
        }

        var oracleConnectionString = promptForConnectionString();
        if (string.IsNullOrWhiteSpace(oracleConnectionString))
        {
            throw new StartupConfigurationException(
                "BOOT_ORACLE_MISSING",
                "Oracle connection string is not configured.");
        }

        oracleConnectionString = oracleConnectionString.Trim();
        EnsureOracleConnectionStringFormat(oracleConnectionString, "setup prompt");

        UserSettings.SetSecure("oracle_connection_string", oracleConnectionString);
        UserSettings.Set("first_run_completed", true);
        return oracleConnectionString;
    }

    private static string? GetEnvironmentOracleConnectionString()
    {
        var envOracle = Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONNECTION")
                          ?? Environment.GetEnvironmentVariable("WORKAUDIT_ORACLE_CONN")
                          ?? Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
                          ?? Environment.GetEnvironmentVariable("WORKAUDIT_TEST_ORACLE");

        return string.IsNullOrWhiteSpace(envOracle) ? null : envOracle.Trim();
    }
}
