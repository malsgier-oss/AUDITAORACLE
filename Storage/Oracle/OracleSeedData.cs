using Oracle.ManagedDataAccess.Client;

namespace WorkAudit.Storage.Oracle;

/// <summary>Default catalog rows aligned with historical baseline versions (10/43/44/48).</summary>
internal static class OracleSeedData
{
    public static void Apply(OracleConnection conn)
    {
        SeedDocumentTypes(conn);
        SeedBranches(conn);
        SeedCategories(conn);
        SeedCoreSettings(conn);
    }

    private static void ExecInsertIgnore(OracleConnection conn, string sql, Action<OracleCommand> bind)
    {
        using var cmd = OracleSql.CreateCommand(conn, sql);
        bind(cmd);
        cmd.ExecuteNonQuery();
    }

    private static void SeedDocumentTypes(OracleConnection conn)
    {
        var types = new[]
        {
            ("Cash Deposit Slip", "CASH", "cash,deposit,slip,إيداع", 0),
            ("Withdrawal Slip", "CASH", "withdrawal,slip,سحب", 1),
            ("Cheque Book Request Form", "CHEQUE", "cheque,book,request,دفتر شيكات", 2),
            ("Stop Payment Request", "CHEQUE", "stop,payment,وقف,صرف", 3),
            ("Account Statement Request", "ACCOUNT", "account,statement,كشف,حساب", 4),
            ("Internal Transfer Form", "ACCOUNT", "internal,transfer,تحويل,داخلي", 5),
            ("Account Opening Form", "ACCOUNT", "account,opening,فتح,حساب", 6),
            ("Card Receipt & Authorization", "CARD", "card,receipt,authorization,بطاقة", 7),
            ("3D Secure Subscription / Cancellation", "CARD", "3d,secure,subscription,cancellation", 8),
            ("Cards Dispute Form", "CARD", "card,dispute,نزاع,بطاقة", 9),
            ("Foreign Exchange & Transfer Request (MoneyGram)", "TRANSFER", "foreign,exchange,moneygram,حوالة", 10),
            ("Change Beneficiary / Country (MoneyGram)", "TRANSFER", "beneficiary,country,moneygram", 11),
            ("MoneyGram", "TRANSFER", "moneygram", 12),
            ("Know Your Customer (KYC) Form", "KYC", "kyc,know,customer,اعرف,عميلك", 13),
            ("Power of Attorney", "OTHER", "power,attorney,توكيل", 14),
            ("Signature Card", "OTHER", "signature,card,بطاقة,توقيع", 15)
        };

        var now = DateTime.UtcNow;
        foreach (var (name, category, keywords, order) in types)
        {
            var sql = """
                INSERT INTO config_document_types (name, category, keywords, is_active, display_order, created_at)
                SELECT :name, :category, :keywords, 1, :ord, :created FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM config_document_types t WHERE t.name = :name)
                """;
            ExecInsertIgnore(conn, sql, cmd =>
            {
                OracleSql.AddParameter(cmd, "name", name);
                OracleSql.AddParameter(cmd, "category", category);
                OracleSql.AddParameter(cmd, "keywords", keywords);
                OracleSql.AddParameter(cmd, "ord", order);
                OracleSql.AddParameter(cmd, "created", now);
            });
        }
    }

    private static void SeedBranches(OracleConnection conn)
    {
        var branches = new[]
        {
            ("Main Branch", "MAIN", 0),
            ("Tripoli Tower Branch", "TTB", 1),
            ("Siahya Branch", "SB", 2),
            ("Zawiat Dahmani", "ZD", 3),
            ("Almadar Branch", "AMB", 4),
            ("Almashtel Branch", "ALMB", 5),
            ("Misrata Branch", "MB", 6)
        };
        var now = DateTime.UtcNow;
        foreach (var (name, code, order) in branches)
        {
            var sql = """
                INSERT INTO config_branches (name, code, is_active, display_order, created_at)
                SELECT :name, :code, 1, :ord, :created FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM config_branches b WHERE b.name = :name)
                """;
            ExecInsertIgnore(conn, sql, cmd =>
            {
                OracleSql.AddParameter(cmd, "name", name);
                OracleSql.AddParameter(cmd, "code", code);
                OracleSql.AddParameter(cmd, "ord", order);
                OracleSql.AddParameter(cmd, "created", now);
            });
        }
    }

    private static void SeedCategories(OracleConnection conn)
    {
        var categories = new[]
        {
            ("CASH", "Cash transactions", 0),
            ("CHEQUE", "Cheque operations", 1),
            ("ACCOUNT", "Account services", 2),
            ("CARD", "Card services", 3),
            ("TRANSFER", "Money transfers", 4),
            ("KYC", "Know Your Customer", 5),
            ("OTHER", "Other documents", 6)
        };
        var now = DateTime.UtcNow;
        foreach (var (name, description, order) in categories)
        {
            var sql = """
                INSERT INTO config_categories (name, description, is_active, display_order, created_at)
                SELECT :name, :description, 1, :ord, :created FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM config_categories c WHERE c.name = :name)
                """;
            ExecInsertIgnore(conn, sql, cmd =>
            {
                OracleSql.AddParameter(cmd, "name", name);
                OracleSql.AddParameter(cmd, "description", description);
                OracleSql.AddParameter(cmd, "ord", order);
                OracleSql.AddParameter(cmd, "created", now);
            });
        }
    }

    private static void SeedCoreSettings(OracleConnection conn)
    {
        var settings = new (string key, string value, string category, string description, string valueType)[]
        {
            ("session_timeout_minutes", "30", "security", "Session inactivity timeout in minutes", "int"),
            ("max_login_attempts", "5", "security", "Maximum failed login attempts before lockout", "int"),
            ("lockout_duration_minutes", "30", "security", "Account lockout duration in minutes", "int"),
            ("session_expiry_hours", "8", "security", "Session expiry time in hours", "int"),
            ("password_min_length", "8", "security", "Minimum password length", "int"),
            ("password_require_uppercase", "true", "security", "Require uppercase letters in password", "bool"),
            ("password_require_lowercase", "true", "security", "Require lowercase letters in password", "bool"),
            ("password_require_digit", "true", "security", "Require digits in password", "bool"),
            ("password_require_special", "true", "security", "Require special characters in password", "bool"),
            ("default_ocr_language", "eng", "ocr", "Default OCR language", "string"),
            ("backup_enabled", "true", "backup", "Enable automatic backups", "bool"),
            ("backup_interval_hours", "24", "backup", "Backup interval in hours", "int"),
            ("backup_retention_count", "10", "backup", "Number of backups to retain", "int"),
            ("backup_include_documents", "true", "backup", "Include document files in backup", "bool"),
            ("include_oracle_data", "false", "backup", "Include Oracle schema in WorkAudit backups (requires expdp/impdp)", "bool"),
            ("oracle_datapump_directory", "DATA_PUMP_DIR", "backup", "Oracle DIRECTORY object for Data Pump", "string"),
            ("oracle_datapump_local_folder", "", "backup", "Local/UNC folder matching DIRECTORY path for .dmp access", "string"),
            ("oracle_backup_dump_tool_path", "", "backup", "Optional path to expdp/impdp or their folder", "string"),
            ("oracle_backup_retention_days", "0", "backup", "Reserved retention hint for dump files (0=unused)", "int"),
            ("require_review_before_audit", "true", "workflow", "Require review status before Ready for Audit", "bool"),
            ("auto_archive_days", "0", "workflow", "Auto-archive cleared documents after N days (0=disabled)", "int"),
            ("archive_retention_years", "7", "archive", "Default retention period in years for archived documents", "int"),
            ("archive_cost_per_gb", "0.10", "archive", "Storage cost per GB (USD) for archive analytics", "decimal"),
            ("text_extraction_method", "llava_only", "ocr", "Primary text extraction method", "string"),
            ("ocr_enabled", "true", "ocr", "Enable Tesseract OCR for text extraction", "bool"),
            ("ocr_engine", "tesseract", "ocr", "OCR engine setting", "string")
        };

        var now = DateTime.UtcNow;
        foreach (var (key, value, category, description, valueType) in settings)
        {
            var sql = """
                INSERT INTO app_settings (key, value, category, description, value_type, updated_at)
                SELECT :key, :value, :category, :description, :valueType, :updated FROM DUAL
                WHERE NOT EXISTS (SELECT 1 FROM app_settings s WHERE s.key = :key)
                """;
            ExecInsertIgnore(conn, sql, cmd =>
            {
                OracleSql.AddParameter(cmd, "key", key);
                OracleSql.AddParameter(cmd, "value", value);
                OracleSql.AddParameter(cmd, "category", category);
                OracleSql.AddParameter(cmd, "description", description);
                OracleSql.AddParameter(cmd, "valueType", valueType);
                OracleSql.AddParameter(cmd, "updated", now);
            });
        }
    }
}
