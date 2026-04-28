namespace WorkAudit.Domain;

public class CustomReportTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ReportType { get; set; } = "custom";
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsShared { get; set; }
    
    public List<ReportField> Fields { get; set; } = new();
    public List<ReportFilter> Filters { get; set; } = new();
    public List<ReportSorting> Sorting { get; set; } = new();
    public ReportGrouping? Grouping { get; set; }
}

public class ReportField
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public FieldType Type { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
}

public class ReportFilter
{
    public string FieldName { get; set; } = string.Empty;
    public FilterOperator Operator { get; set; }
    public string? Value { get; set; }
    public LogicalOperator LogicalOp { get; set; } = LogicalOperator.And;
}

public class ReportSorting
{
    public string FieldName { get; set; } = string.Empty;
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}

public class ReportGrouping
{
    public string FieldName { get; set; } = string.Empty;
    public bool ShowTotals { get; set; }
}

public class ReportFieldDefinition
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public FieldType Type { get; set; }
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
}

public enum FieldType
{
    Text,
    Number,
    Date,
    DateTime,
    Boolean,
    Enum
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
    Between,
    IsNull,
    IsNotNull,
    In
}

public enum LogicalOperator
{
    And,
    Or
}

public enum SortDirection
{
    Ascending,
    Descending
}

public class ReportResult
{
    public List<Dictionary<string, object?>> Data { get; set; } = new();
    public int TotalCount { get; set; }
    public CustomReportTemplate Template { get; set; } = null!;
    public DateTime GeneratedAt { get; set; }
}
