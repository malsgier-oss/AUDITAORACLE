namespace WorkAudit.Domain;

/// <summary>
/// Configurable document type stored in database.
/// </summary>
public class ConfigDocumentType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Keywords { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? UpdatedAt { get; set; }
    /// <summary>Branch name (optional, for organization by Branch/Section/Type).</summary>
    public string? Branch { get; set; }
    /// <summary>Section name (optional, for organization by Branch/Section/Type).</summary>
    public string? Section { get; set; }
}

/// <summary>
/// Configurable branch stored in database.
/// </summary>
public class ConfigBranch
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Configurable category stored in database.
/// </summary>
public class ConfigCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Application setting stored in database.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public string Category { get; set; } = "";
    public string? Description { get; set; }
    public string ValueType { get; set; } = "string";
    public string? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Get value as integer.
    /// </summary>
    public int GetInt(int defaultValue = 0) =>
        int.TryParse(Value, out var result) ? result : defaultValue;

    /// <summary>
    /// Get value as float.
    /// </summary>
    public float GetFloat(float defaultValue = 0f) =>
        float.TryParse(Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    /// <summary>
    /// Get value as boolean.
    /// </summary>
    public bool GetBool(bool defaultValue = false) =>
        bool.TryParse(Value, out var result) ? result : defaultValue;
}

/// <summary>
/// Setting categories for grouping in UI.
/// </summary>
public static class SettingCategories
{
    public const string Security = "security";
    public const string Ocr = "ocr";
    public const string Backup = "backup";
    public const string Workflow = "workflow";

    public static readonly string[] All = { Security, Ocr, Backup, Workflow };

    public static string GetDisplayName(string category) => category switch
    {
        Security => "Security",
        Ocr => "OCR Settings",
        Backup => "Backup",
        Workflow => "Workflow",
        _ => category
    };
}
