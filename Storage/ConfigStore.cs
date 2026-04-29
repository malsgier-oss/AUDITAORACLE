using Oracle.ManagedDataAccess.Client;
using Oracle.ManagedDataAccess.Types;
using Serilog;
using System.Data;
using System.Globalization;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

/// <summary>
/// Storage service for configuration entities (document types, branches, settings).
/// Supports encryption for sensitive configuration values.
/// </summary>
public interface IConfigStore
{
    // Document Types
    List<ConfigDocumentType> GetDocumentTypes(bool includeInactive = false);
    ConfigDocumentType? GetDocumentType(int id);
    int InsertDocumentType(ConfigDocumentType docType);
    bool UpdateDocumentType(ConfigDocumentType docType);
    bool DeleteDocumentType(int id);

    // Branches
    List<ConfigBranch> GetBranches(bool includeInactive = false);
    ConfigBranch? GetBranch(int id);
    int InsertBranch(ConfigBranch branch);
    bool UpdateBranch(ConfigBranch branch);
    bool DeleteBranch(int id);

    // Categories
    List<ConfigCategory> GetCategories(bool includeInactive = false);
    ConfigCategory? GetCategory(int id);
    int InsertCategory(ConfigCategory category);
    bool UpdateCategory(ConfigCategory category);
    bool DeleteCategory(int id);

    // App Settings
    List<AppSetting> GetSettings(string? category = null);
    AppSetting? GetSetting(string key);
    string? GetSettingValue(string key, string? defaultValue = null);
    int GetSettingInt(string key, int defaultValue = 0);
    bool GetSettingBool(string key, bool defaultValue = false);
    float GetSettingFloat(string key, float defaultValue = 0f);
    bool SetSetting(string key, string? value, string? updatedBy = null);
    bool SetSettingInt(string key, int value, string? updatedBy = null);
    bool SetSettingBool(string key, bool value, string? updatedBy = null);
    bool DeleteSetting(string key);
    
    /// <summary>Gets a secure setting value and decrypts it if encrypted.</summary>
    string? GetSecureSettingValue(string key, string? defaultValue = null);
    
    /// <summary>Sets a secure setting value with automatic encryption.</summary>
    bool SetSecureSetting(string key, string? value, string? updatedBy = null);
}

public class ConfigStore : IConfigStore
{
    private readonly ILogger _log = LoggingService.ForContext<ConfigStore>();
    private static readonly ILogger s_log = LoggingService.ForContext(typeof(ConfigStore));
    private readonly string _connectionString;
    private readonly ISecureConfigService? _secureConfig;

    public ConfigStore(string dbPath, ISecureConfigService? secureConfig = null)
    {
        _connectionString = dbPath;
        _secureConfig = secureConfig;
    }

    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    #region Document Types

    public List<ConfigDocumentType> GetDocumentTypes(bool includeInactive = false)
    {
        var types = new List<ConfigDocumentType>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeInactive
            ? "SELECT * FROM config_document_types ORDER BY COALESCE(branch,''), COALESCE(section,''), display_order, name"
            : "SELECT * FROM config_document_types WHERE is_active = 1 ORDER BY COALESCE(branch,''), COALESCE(section,''), display_order, name";

        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            types.Add(ReadDocumentType(reader));
        }
        return types;
    }

    public ConfigDocumentType? GetDocumentType(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_document_types WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadDocumentType(reader) : null;
    }

    public int InsertDocumentType(ConfigDocumentType docType)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO config_document_types (name, category, keywords, is_active, display_order, created_at, branch, section)
                            VALUES (@name, @category, @keywords, @active, @displayOrder, @created, @branch, @section)
                            RETURNING id INTO @rid";
        var normalizedName = RequiredOrFallback(docType.Name, "Unspecified");
        var normalizedCategory = RequiredOrFallback(docType.Category, "General");
        cmd.Parameters.AddWithValue("@name", normalizedName);
        cmd.Parameters.AddWithValue("@category", normalizedCategory);
        cmd.Parameters.AddWithValue("@keywords", (object?)docType.Keywords ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", docType.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@displayOrder", docType.DisplayOrder);
        cmd.Parameters.Add(new OracleParameter("created", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        cmd.Parameters.AddWithValue("@branch", (object?)docType.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@section", (object?)docType.Section ?? DBNull.Value);
        var idParam = new OracleParameter("rid", OracleDbType.Int32, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        Prep(cmd);
        cmd.ExecuteNonQuery();
        return ToInt32(idParam.Value);
    }

    public bool UpdateDocumentType(ConfigDocumentType docType)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE config_document_types
                            SET name = @name, category = @category, keywords = @keywords,
                                is_active = @active, display_order = @displayOrder, updated_at = @updated,
                                branch = @branch, section = @section
                            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", docType.Id);
        var normalizedName = RequiredOrFallback(docType.Name, "Unspecified");
        var normalizedCategory = RequiredOrFallback(docType.Category, "General");
        cmd.Parameters.AddWithValue("@name", normalizedName);
        cmd.Parameters.AddWithValue("@category", normalizedCategory);
        cmd.Parameters.AddWithValue("@keywords", (object?)docType.Keywords ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", docType.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@displayOrder", docType.DisplayOrder);
        cmd.Parameters.Add(new OracleParameter("updated", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        cmd.Parameters.AddWithValue("@branch", (object?)docType.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@section", (object?)docType.Section ?? DBNull.Value);

        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteDocumentType(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM config_document_types WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static ConfigDocumentType ReadDocumentType(OracleDataReader reader)
    {
        var doc = new ConfigDocumentType
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Category = reader.GetString(reader.GetOrdinal("category")),
            Keywords = reader.IsDBNull(reader.GetOrdinal("keywords")) ? null : reader.GetString(reader.GetOrdinal("keywords")),
            IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1,
            DisplayOrder = reader.GetInt32(reader.GetOrdinal("display_order")),
            CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
            UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetString(reader.GetOrdinal("updated_at"))
        };
        try
        {
            var branchOrd = reader.GetOrdinal("branch");
            doc.Branch = reader.IsDBNull(branchOrd) ? null : reader.GetString(branchOrd);
        }
        catch (Exception ex) { s_log.Warning(ex, "Failed to read branch field from config: {Message}", ex.Message); }
        try
        {
            var sectionOrd = reader.GetOrdinal("section");
            doc.Section = reader.IsDBNull(sectionOrd) ? null : reader.GetString(sectionOrd);
        }
        catch (Exception ex) { s_log.Warning(ex, "Failed to read section field from config: {Message}", ex.Message); }
        return doc;
    }

    private static string RequiredOrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static int ToInt32(object? value)
    {
        if (value is null || value == DBNull.Value)
            return 0;
        if (value is OracleDecimal oracleDecimal)
            return oracleDecimal.ToInt32();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Branches

    public List<ConfigBranch> GetBranches(bool includeInactive = false)
    {
        var branches = new List<ConfigBranch>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeInactive
            ? "SELECT * FROM config_branches ORDER BY display_order, name"
            : "SELECT * FROM config_branches WHERE is_active = 1 ORDER BY display_order, name";

        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            branches.Add(ReadBranch(reader));
        }
        return branches;
    }

    public ConfigBranch? GetBranch(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_branches WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadBranch(reader) : null;
    }

    public int InsertBranch(ConfigBranch branch)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO config_branches (name, code, is_active, display_order, created_at)
                            VALUES (@name, @code, @active, @displayOrder, @created)
                            RETURNING id INTO @rid";
        cmd.Parameters.AddWithValue("@name", branch.Name);
        cmd.Parameters.AddWithValue("@code", (object?)branch.Code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", branch.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@displayOrder", branch.DisplayOrder);
        cmd.Parameters.Add(new OracleParameter("created", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        var idParam = new OracleParameter("rid", OracleDbType.Int32, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        Prep(cmd);
        cmd.ExecuteNonQuery();
        return ToInt32(idParam.Value);
    }

    public bool UpdateBranch(ConfigBranch branch)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE config_branches
                            SET name = @name, code = @code, is_active = @active,
                                display_order = @displayOrder, updated_at = @updated
                            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", branch.Id);
        cmd.Parameters.AddWithValue("@name", branch.Name);
        cmd.Parameters.AddWithValue("@code", (object?)branch.Code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", branch.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@displayOrder", branch.DisplayOrder);
        cmd.Parameters.Add(new OracleParameter("updated", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });

        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteBranch(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM config_branches WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static ConfigBranch ReadBranch(OracleDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Code = reader.IsDBNull(reader.GetOrdinal("code")) ? null : reader.GetString(reader.GetOrdinal("code")),
        IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1,
        DisplayOrder = reader.GetInt32(reader.GetOrdinal("display_order")),
        CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetString(reader.GetOrdinal("updated_at"))
    };

    #endregion

    #region Categories

    public List<ConfigCategory> GetCategories(bool includeInactive = false)
    {
        var categories = new List<ConfigCategory>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeInactive
            ? "SELECT * FROM config_categories ORDER BY display_order, name"
            : "SELECT * FROM config_categories WHERE is_active = 1 ORDER BY display_order, name";

        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            categories.Add(ReadCategory(reader));
        }
        return categories;
    }

    public ConfigCategory? GetCategory(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM config_categories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCategory(reader) : null;
    }

    public int InsertCategory(ConfigCategory category)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO config_categories (name, description, is_active, display_order, created_at)
                            VALUES (@name, @description, @active, @displayOrder, @created)
                            RETURNING id INTO @rid";
        cmd.Parameters.AddWithValue("@name", category.Name);
        cmd.Parameters.AddWithValue("@description", (object?)category.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", category.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@displayOrder", category.DisplayOrder);
        cmd.Parameters.Add(new OracleParameter("created", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        var idParam = new OracleParameter("rid", OracleDbType.Int32, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        Prep(cmd);
        cmd.ExecuteNonQuery();
        return ToInt32(idParam.Value);
    }

    public bool UpdateCategory(ConfigCategory category)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE config_categories
                            SET name = @name, description = @description, is_active = @active,
                                display_order = @displayOrder, updated_at = @updated
                            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", category.Id);
        cmd.Parameters.AddWithValue("@name", category.Name);
        cmd.Parameters.AddWithValue("@description", (object?)category.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@active", category.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@displayOrder", category.DisplayOrder);
        cmd.Parameters.Add(new OracleParameter("updated", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });

        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteCategory(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM config_categories WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static ConfigCategory ReadCategory(OracleDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1,
        DisplayOrder = reader.GetInt32(reader.GetOrdinal("display_order")),
        CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetString(reader.GetOrdinal("updated_at"))
    };

    #endregion

    #region App Settings

    public List<AppSetting> GetSettings(string? category = null)
    {
        var settings = new List<AppSetting>();
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = category == null
            ? "SELECT * FROM app_settings ORDER BY category, key"
            : "SELECT * FROM app_settings WHERE category = @p_category ORDER BY key";
        if (category != null)
            cmd.Parameters.AddWithValue("p_category", category);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            settings.Add(ReadSetting(reader));
        }
        return settings;
    }

    public AppSetting? GetSetting(string key)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM app_settings WHERE key = @p_key";
        cmd.Parameters.AddWithValue("p_key", key);

        Prep(cmd); using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSetting(reader) : null;
    }

    public string? GetSettingValue(string key, string? defaultValue = null)
    {
        var setting = GetSetting(key);
        return setting?.Value ?? defaultValue;
    }

    public int GetSettingInt(string key, int defaultValue = 0)
    {
        var setting = GetSetting(key);
        return setting?.GetInt(defaultValue) ?? defaultValue;
    }

    public bool GetSettingBool(string key, bool defaultValue = false)
    {
        var setting = GetSetting(key);
        return setting?.GetBool(defaultValue) ?? defaultValue;
    }

    public float GetSettingFloat(string key, float defaultValue = 0f)
    {
        var setting = GetSetting(key);
        return setting?.GetFloat(defaultValue) ?? defaultValue;
    }

    public bool SetSetting(string key, string? value, string? updatedBy = null)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();

        // Oracle upsert (MERGE) for settings
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            MERGE INTO app_settings s
            USING (SELECT @p_key AS key_name, @p_value AS value_txt, @p_updated AS updated_at_txt, @p_updated_by AS updated_by_txt FROM dual) v
            ON (s.key = v.key_name)
            WHEN MATCHED THEN
              UPDATE SET
                s.value = v.value_txt,
                s.updated_at = v.updated_at_txt,
                s.updated_by = v.updated_by_txt
            WHEN NOT MATCHED THEN
              INSERT (key, value, category, description, value_type, updated_at, updated_by)
              VALUES (v.key_name, v.value_txt, 'general', NULL, 'string', v.updated_at_txt, v.updated_by_txt)";
        cmd.Parameters.AddWithValue("p_key", key);
        cmd.Parameters.AddWithValue("p_value", (object?)value ?? DBNull.Value);
        cmd.Parameters.Add(new OracleParameter("p_updated", OracleDbType.TimeStamp) { Value = DateTime.UtcNow });
        cmd.Parameters.AddWithValue("p_updated_by", (object?)updatedBy ?? DBNull.Value);
        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool SetSettingInt(string key, int value, string? updatedBy = null) =>
        SetSetting(key, value.ToString(CultureInfo.InvariantCulture), updatedBy);

    public bool SetSettingBool(string key, bool value, string? updatedBy = null) =>
        SetSetting(key, value.ToString().ToLowerInvariant(), updatedBy);

    public bool DeleteSetting(string key)
    {
        using var conn = new OracleConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM app_settings WHERE key = @p_key";
        cmd.Parameters.AddWithValue("p_key", key);
        Prep(cmd);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static AppSetting ReadSetting(OracleDataReader reader) => new()
    {
        Key = reader.GetString(reader.GetOrdinal("key")),
        Value = reader.IsDBNull(reader.GetOrdinal("value")) ? null : reader.GetString(reader.GetOrdinal("value")),
        Category = reader.GetString(reader.GetOrdinal("category")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        ValueType = reader.GetString(reader.GetOrdinal("value_type")),
        UpdatedAt = reader.IsDBNull(reader.GetOrdinal("updated_at")) ? null : reader.GetString(reader.GetOrdinal("updated_at")),
        UpdatedBy = reader.IsDBNull(reader.GetOrdinal("updated_by")) ? null : reader.GetString(reader.GetOrdinal("updated_by"))
    };

    public string? GetSecureSettingValue(string key, string? defaultValue = null)
    {
        var encryptedValue = GetSettingValue(key, defaultValue);
        if (encryptedValue == null)
            return defaultValue;

        if (_secureConfig == null)
        {
            _log.Warning("SecureConfigService not available, returning encrypted value as-is for key: {Key}", key);
            return encryptedValue;
        }

        return _secureConfig.IsEncrypted(encryptedValue) 
            ? _secureConfig.Decrypt(encryptedValue) ?? defaultValue 
            : encryptedValue;
    }

    public bool SetSecureSetting(string key, string? value, string? updatedBy = null)
    {
        if (string.IsNullOrEmpty(value))
            return SetSetting(key, value, updatedBy);

        if (_secureConfig == null)
        {
            _log.Error("SecureConfigService not available, cannot encrypt setting: {Key}", key);
            return false;
        }

        var encryptedValue = _secureConfig.Encrypt(value);
        return SetSetting(key, encryptedValue, updatedBy);
    }

    #endregion
}
