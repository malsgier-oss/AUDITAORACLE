using WorkAudit.Config;
using WorkAudit.Core.Services;
using Xunit;

namespace WorkAudit.Tests.Core.Services;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public void LooksLikePlaceholderConnectionString_DetectsPlaceholderValues()
    {
        var placeholder = "User Id=workaudit;Password=change-me;Data Source=//db-host:1521/WORKAUDIT";
        Assert.True(StartupCoordinator.LooksLikePlaceholderConnectionString(placeholder));
    }

    [Fact]
    public void IsValidOracleConnectionStringFormat_RejectsPlaceholderAndAcceptsRealShape()
    {
        var placeholder = "User Id=workaudit;Password=change-me;Data Source=//db-host:1521/WORKAUDIT";
        var validShape = "User Id=workaudit;Password=SuperSecret1!;Data Source=//db-host:1521/WORKAUDIT";

        Assert.False(StartupCoordinator.IsValidOracleConnectionStringFormat(placeholder));
        Assert.True(StartupCoordinator.IsValidOracleConnectionStringFormat(validShape));
    }

    [Fact]
    public void Initialize_WhenManagedEnvRequiredAndMissing_ReturnsEnvRequiredCode()
    {
        using var scope = new EnvScope()
            .Set("WORKAUDIT_REQUIRE_ORACLE_ENV", "true")
            .Set("WORKAUDIT_ORACLE_CONNECTION", null)
            .Set("WORKAUDIT_ORACLE_CONN", null)
            .Set("ORACLE_CONNECTION_STRING", null)
            .Set("WORKAUDIT_TEST_ORACLE", null);
        UserSettings.Set("oracle_connection_string", "");

        var coordinator = new StartupCoordinator();
        var result = coordinator.Initialize(
            promptForConnectionString: () => "",
            resolveOracleConnectionString: s => s,
            ensureArchiveSchema: _ => { });

        Assert.False(result.Success);
        Assert.Equal("BOOT_ORACLE_ENV_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void Initialize_WhenEnvironmentConnectionMalformed_ReturnsMalformedCode()
    {
        using var scope = new EnvScope()
            .Set("WORKAUDIT_REQUIRE_ORACLE_ENV", null)
            .Set("WORKAUDIT_ORACLE_CONNECTION", "not-a-connection");
        UserSettings.Set("oracle_connection_string", "");

        var coordinator = new StartupCoordinator();
        var result = coordinator.Initialize(
            promptForConnectionString: () => "",
            resolveOracleConnectionString: s => s,
            ensureArchiveSchema: _ => { });

        Assert.False(result.Success);
        Assert.Equal("BOOT_ORACLE_MALFORMED", result.ErrorCode);
    }

    [Fact]
    public void Initialize_WhenEncryptedLocalConnectionExists_PrefersLocalOverEnvironment()
    {
        using var scope = new EnvScope()
            .Set("WORKAUDIT_REQUIRE_ORACLE_ENV", null)
            .Set("WORKAUDIT_ORACLE_CONNECTION", "User Id=env;Password=EnvPass1!;Data Source=//env-host:1521/ENVSVC")
            .Set("WORKAUDIT_ORACLE_CONN", null)
            .Set("ORACLE_CONNECTION_STRING", null)
            .Set("WORKAUDIT_TEST_ORACLE", null);

        var local = "User Id=local;Password=LocalPass1!;Data Source=//local-host:1521/LOCALDB";
        UserSettings.SetSecure("oracle_connection_string", local);

        var captured = "";
        var coordinator = new StartupCoordinator();
        var result = coordinator.Initialize(
            promptForConnectionString: () => "",
            resolveOracleConnectionString: s =>
            {
                captured = s;
                return s;
            },
            ensureArchiveSchema: _ => { });

        Assert.False(result.Success);
        Assert.Equal("BOOT_ORACLE_UNREACHABLE", result.ErrorCode);
        Assert.Equal(local, captured);
    }

    [Fact]
    public void Initialize_WhenSecureValueMissing_UsesLegacyPlainConnectionText()
    {
        using var scope = new EnvScope()
            .Set("WORKAUDIT_REQUIRE_ORACLE_ENV", null)
            .Set("WORKAUDIT_ORACLE_CONNECTION", "User Id=env;Password=EnvPass1!;Data Source=//env-host:1521/ENVSVC")
            .Set("WORKAUDIT_ORACLE_CONN", null)
            .Set("ORACLE_CONNECTION_STRING", null)
            .Set("WORKAUDIT_TEST_ORACLE", null);

        var plainConnection = "User Id=plain;Password=PlainPass1!;Data Source=//plain-host:1521/PLAINSVC";
        UserSettings.Set("oracle_connection_string", plainConnection);

        var captured = "";
        var coordinator = new StartupCoordinator();
        var result = coordinator.Initialize(
            promptForConnectionString: () => "",
            resolveOracleConnectionString: s =>
            {
                captured = s;
                return s;
            },
            ensureArchiveSchema: _ => { });

        Assert.False(result.Success);
        Assert.Equal("BOOT_ORACLE_UNREACHABLE", result.ErrorCode);
        Assert.Equal(plainConnection, captured);
    }

    [Fact]
    public void Initialize_WhenNoEnvironmentConnection_UsesPromptAndValidatesValue()
    {
        using var scope = new EnvScope()
            .Set("WORKAUDIT_REQUIRE_ORACLE_ENV", null)
            .Set("WORKAUDIT_ORACLE_CONNECTION", null)
            .Set("WORKAUDIT_ORACLE_CONN", null)
            .Set("ORACLE_CONNECTION_STRING", null)
            .Set("WORKAUDIT_TEST_ORACLE", null);
        UserSettings.Set("oracle_connection_string", "");

        var coordinator = new StartupCoordinator();
        var result = coordinator.Initialize(
            promptForConnectionString: () => "User Id=workaudit;Password=change-me;Data Source=//db-host:1521/WORKAUDIT",
            resolveOracleConnectionString: s => s,
            ensureArchiveSchema: _ => { });

        Assert.False(result.Success);
        Assert.Equal("BOOT_ORACLE_MALFORMED", result.ErrorCode);
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new(StringComparer.OrdinalIgnoreCase);

        public EnvScope Set(string name, string? value)
        {
            if (!_original.ContainsKey(name))
                _original[name] = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            return this;
        }

        public void Dispose()
        {
            foreach (var pair in _original)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.Process);
        }
    }
}
