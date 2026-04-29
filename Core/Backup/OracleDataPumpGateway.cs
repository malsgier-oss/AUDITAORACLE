using System.Diagnostics;
using System.IO;
using System.Text;
using Serilog;
using WorkAudit.Core.Services;

namespace WorkAudit.Core.Backup;

/// <summary>
/// Invokes Oracle <c>expdp</c> / <c>impdp</c> client tools. Requires Oracle client or Instant Client on PATH
/// or explicit paths in <see cref="OraclePumpExportRequest.ExpdpExecutablePath"/>.
/// </summary>
public sealed class OracleDataPumpGateway : IOracleBackupGateway
{
    private readonly ILogger _log = LoggingService.ForContext<OracleDataPumpGateway>();

    public async Task<OraclePumpOperationResult> ExportSchemaAsync(OraclePumpExportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!OracleBackupConnectionParser.TryParse(request.ConnectionString, out var user, out var pwd, out var ds))
            return OraclePumpOperationResult.Fail("Invalid Oracle connection string (User Id / Data Source required).");

        var expdp = ResolveExecutable(request.ExpdpExecutablePath, "expdp.exe");
        if (expdp == null)
            return OraclePumpOperationResult.Fail(
                "expdp.exe not found. Install Oracle Instant Client with Data Pump, set PATH, or set app setting oracle_backup_dump_tool_path.");

        var userid = OracleBackupConnectionParser.BuildUserIdArgument(user, pwd, ds);
        var args = new StringBuilder();
        args.Append("userid=").Append(userid).Append(' ');
        args.Append("directory=").Append(request.OracleDirectoryName).Append(' ');
        args.Append("dumpfile=").Append(request.DumpFileName).Append(' ');
        args.Append("logfile=").Append(request.LogFileName).Append(' ');
        args.Append("schemas=").Append(request.SchemaName).Append(' ');
        args.Append("reuse_dumpfiles=Y");

        return await RunPumpAsync(expdp, args.ToString(), request.WorkingDirectory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OraclePumpOperationResult> ImportSchemaAsync(OraclePumpImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!OracleBackupConnectionParser.TryParse(request.ConnectionString, out var user, out var pwd, out var ds))
            return OraclePumpOperationResult.Fail("Invalid Oracle connection string (User Id / Data Source required).");

        var impdp = ResolveExecutable(request.ImpdpExecutablePath, "impdp.exe");
        if (impdp == null)
            return OraclePumpOperationResult.Fail(
                "impdp.exe not found. Install Oracle Instant Client with Data Pump, set PATH, or set app setting oracle_backup_dump_tool_path.");

        var userid = OracleBackupConnectionParser.BuildUserIdArgument(user, pwd, ds);
        var args = new StringBuilder();
        args.Append("userid=").Append(userid).Append(' ');
        args.Append("directory=").Append(request.OracleDirectoryName).Append(' ');
        args.Append("dumpfile=").Append(request.DumpFileName).Append(' ');
        args.Append("logfile=").Append(request.LogFileName).Append(' ');
        args.Append("schemas=").Append(request.SchemaName).Append(' ');
        if (request.ReplaceExistingObjects)
            args.Append("table_exists_action=REPLACE ");

        return await RunPumpAsync(impdp, args.ToString(), request.WorkingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveExecutable(string? configuredPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed))
                return trimmed;
            var combined = Path.Combine(trimmed, fileName);
            if (File.Exists(combined))
                return combined;
            if (File.Exists(trimmed))
                return trimmed;
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var candidate = Path.Combine(folder.Trim(), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async Task<OraclePumpOperationResult> RunPumpAsync(string executable, string arguments, string? workingDirectory,
        CancellationToken cancellationToken)
    {
        _log.Information("Starting Data Pump: {Exe} {Args}", executable, RedactUserid(arguments));

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory
        };

        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            if (!proc.Start())
                return OraclePumpOperationResult.Fail("Failed to start Data Pump process.");

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var outText = stdout.ToString();
            var errText = stderr.ToString();
            if (proc.ExitCode == 0)
            {
                _log.Information("Data Pump completed successfully (exit {Code})", proc.ExitCode);
                return OraclePumpOperationResult.Ok(proc.ExitCode, outText, errText);
            }

            var msg = $"Data Pump exited with code {proc.ExitCode}. See application log for stderr.";
            _log.Warning("Data Pump failed: {Message}\nSTDOUT:\n{Out}\nSTDERR:\n{Err}", msg, outText, errText);
            return new OraclePumpOperationResult
            {
                Success = false,
                ExitCode = proc.ExitCode,
                StandardOutput = outText,
                StandardError = errText,
                ErrorMessage = msg
            };
        }
        catch (OperationCanceledException)
        {
            return OraclePumpOperationResult.Fail("Data Pump was cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Data Pump process error");
            return OraclePumpOperationResult.Fail(ex.Message);
        }
    }

    private static string RedactUserid(string arguments)
    {
        const string token = "userid=";
        var i = arguments.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return arguments;
        var start = i + token.Length;
        var end = start;
        while (end < arguments.Length && arguments[end] != ' ')
            end++;
        return arguments[..start] + "***" + arguments[end..];
    }
}
