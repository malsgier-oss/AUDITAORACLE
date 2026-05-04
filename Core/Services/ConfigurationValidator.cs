using System.IO;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Services;

public interface IConfigurationValidator
{
    List<ConfigValidationItem> ValidateAll(AppConfiguration config);
}

public sealed class ConfigurationValidator : IConfigurationValidator
{
    private readonly IConfigStore _configStore;

    public ConfigurationValidator(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public List<ConfigValidationItem> ValidateAll(AppConfiguration config)
    {
        var list = new List<ConfigValidationItem>();

        void Add(string key, string category, bool ok, string status, string sev, string? msg)
        {
            list.Add(new ConfigValidationItem
            {
                ConfigKey = key,
                Category = category,
                IsValid = ok,
                Status = status,
                Severity = sev,
                Message = msg
            });
        }

        var baseDir = config.BaseDirectory?.Trim() ?? "";
        var baseOk = !string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir);
        Add("base_directory", "Paths", baseOk, baseOk ? "Valid" : "Invalid", baseOk ? "Info" : "Error",
            baseOk ? "Base directory exists." : "Base directory missing or not set.");

        if (baseOk)
        {
            try
            {
                var testFile = Path.Combine(baseDir, ".workaudit_write_test");
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                Add("base_directory_writable", "Paths", true, "Valid", "Info", "Base directory is writable.");
            }
            catch (Exception ex)
            {
                Add("base_directory_writable", "Paths", false, "Invalid", "Error", ex.Message);
            }
        }

        var conn = config.OracleConnectionString?.Trim() ?? "";
        var connOk = !string.IsNullOrEmpty(conn) && conn.Contains("Data Source", StringComparison.OrdinalIgnoreCase);
        Add("oracle_connection_string", "Database", connOk, connOk ? "Valid" : "Invalid", connOk ? "Info" : "Error",
            connOk ? "Connection string is set." : "Oracle connection string missing or incomplete.");

        var backupOut = _configStore.GetSettingValue("scheduled_report_output_dir", "")?.Trim() ?? "";
        if (!string.IsNullOrEmpty(backupOut))
        {
            var ok = Directory.Exists(backupOut);
            Add("scheduled_report_output_dir", "Paths", ok, ok ? "Valid" : "Unreachable", ok ? "Info" : "Warning",
                ok ? "Scheduled report output folder exists." : "Scheduled report folder does not exist.");
        }

        var tess = _configStore.GetSettingValue("tesseract_tessdata_path", "")?.Trim() ?? "";
        if (!string.IsNullOrEmpty(tess))
        {
            var ok = Directory.Exists(tess);
            Add("tesseract_tessdata_path", "OCR", ok, ok ? "Valid" : "Unreachable", ok ? "Info" : "Warning",
                ok ? "Tessdata folder exists." : "Configured tessdata folder not found.");
        }

        var smtp = _configStore.GetSettingValue("smtp_host", "")?.Trim() ?? "";
        var reportsOn = _configStore.GetSettingBool("scheduled_reports_enabled", false);
        if (reportsOn && string.IsNullOrEmpty(smtp))
            Add("smtp_host", "Email", false, "Missing", "Warning", "Scheduled reports enabled but SMTP host is empty.");

        var branches = _configStore.GetBranches(true).Count(b => b.IsActive);
        Add("branches", "Configuration", branches > 0, branches > 0 ? "Valid" : "Missing", branches > 0 ? "Info" : "Warning",
            branches > 0 ? $"{branches} active branch(es)." : "No active branches.");

        var docTypes = _configStore.GetDocumentTypes(true).Count(t => t.IsActive);
        Add("document_types", "Configuration", docTypes > 0, docTypes > 0 ? "Valid" : "Missing", docTypes > 0 ? "Info" : "Warning",
            docTypes > 0 ? $"{docTypes} active document type(s)." : "No active document types.");

        var datapump = _configStore.GetSettingBool("include_oracle_data", false);
        if (datapump)
        {
            var localFolder = _configStore.GetSettingValue("oracle_datapump_local_folder", "")?.Trim() ?? "";
            var folderOk = string.IsNullOrEmpty(localFolder) || Directory.Exists(localFolder);
            Add("oracle_datapump_local_folder", "Backup", folderOk, folderOk ? "Valid" : "Unreachable",
                folderOk ? "Info" : "Warning",
                folderOk ? "Datapump local folder OK or not set." : "Oracle datapump local folder not found.");
        }

        return list;
    }
}
