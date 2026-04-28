using System.IO;
using System.Net;
using System.Net.Mail;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Sends report files via email. Used for scheduled report distribution.
/// </summary>
public interface IReportEmailService
{
    bool SendReport(string filePath, string reportType, string? subject = null);
}

public class ReportEmailService : IReportEmailService
{
    private readonly ILogger _log = LoggingService.ForContext<ReportEmailService>();
    private readonly IConfigStore _configStore;

    public ReportEmailService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public bool SendReport(string filePath, string reportType, string? subject = null)
    {
        if (!File.Exists(filePath))
        {
            _log.Warning("Report file not found for email: {Path}", filePath);
            return false;
        }

        var host = _configStore.GetSettingValue("smtp_host", "")?.Trim();
        var portStr = _configStore.GetSettingValue("smtp_port", "587") ?? "587";
        var user = _configStore.GetSettingValue("smtp_user", "")?.Trim();
        var password = _configStore.GetSecureSettingValue("smtp_password", "")?.Trim();
        var recipients = _configStore.GetSettingValue("scheduled_report_email_recipients", "")?.Trim();
        var from = _configStore.GetSettingValue("smtp_from", user ?? "workaudit@local")?.Trim();

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(recipients))
        {
            _log.Debug("Email not configured: SMTP host or recipients missing");
            return false;
        }

        if (!int.TryParse(portStr, out var port)) port = 587;

        try
        {
            using var client = new SmtpClient(host, port);
            client.EnableSsl = port == 465 || port == 587;
            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
            {
                client.Credentials = new NetworkCredential(user, password);
            }

            var msg = new MailMessage
            {
                From = new MailAddress(from ?? "workaudit@local", "WorkAudit"),
                Subject = subject ?? $"WorkAudit Report: {reportType} - {DateTime.Today:yyyy-MM-dd}",
                Body = $"Please find attached the {reportType} report generated on {DateTime.Now:yyyy-MM-dd HH:mm}."
            };

            foreach (var addr in recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = addr.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    msg.To.Add(trimmed);
            }

            if (msg.To.Count == 0)
            {
                _log.Warning("No valid email recipients configured");
                return false;
            }

            msg.Attachments.Add(new Attachment(filePath));

            client.Send(msg);
            _log.Information("Report emailed to {Count} recipient(s): {Type}", msg.To.Count, reportType);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to email report: {Path}", filePath);
            return false;
        }
    }
}
