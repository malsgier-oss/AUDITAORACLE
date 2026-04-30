using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

public interface IReportBuilderService
{
    // Template Management
    Task<int> CreateTemplateAsync(CustomReportTemplate template);
    Task<CustomReportTemplate?> GetTemplateAsync(int id);
    Task<List<CustomReportTemplate>> GetUserTemplatesAsync(int userId);
    Task<List<CustomReportTemplate>> GetAllAccessibleTemplatesAsync(int userId);
    Task UpdateTemplateAsync(CustomReportTemplate template);
    Task DeleteTemplateAsync(int id);
    
    // Field Discovery
    List<ReportFieldDefinition> GetAvailableFields();
    List<FilterOperator> GetOperatorsForField(FieldType type);
    
    // Report Generation
    Task<ReportResult> GenerateReportAsync(CustomReportTemplate template, DateTime? startDate = null, DateTime? endDate = null);
    
    // Validation
    (bool IsValid, List<string> Errors) ValidateTemplate(CustomReportTemplate template);
}

public class ReportBuilderService : IReportBuilderService
{
    private readonly IReportTemplateStore _templateStore;
    private readonly IDocumentStore _documentStore;

    public ReportBuilderService(IReportTemplateStore templateStore, IDocumentStore documentStore)
    {
        _templateStore = templateStore;
        _documentStore = documentStore;
    }

    public Task<int> CreateTemplateAsync(CustomReportTemplate template)
    {
        template.CreatedAt = DateTime.UtcNow;
        return _templateStore.CreateTemplateAsync(template);
    }

    public Task<CustomReportTemplate?> GetTemplateAsync(int id)
    {
        return _templateStore.GetTemplateAsync(id);
    }

    public Task<List<CustomReportTemplate>> GetUserTemplatesAsync(int userId)
    {
        return _templateStore.GetTemplatesByUserAsync(userId);
    }

    public Task<List<CustomReportTemplate>> GetAllAccessibleTemplatesAsync(int userId)
    {
        return _templateStore.GetAllAccessibleTemplatesAsync(userId);
    }

    public Task UpdateTemplateAsync(CustomReportTemplate template)
    {
        template.UpdatedAt = DateTime.UtcNow;
        return _templateStore.UpdateTemplateAsync(template);
    }

    public Task DeleteTemplateAsync(int id)
    {
        return _templateStore.DeleteTemplateAsync(id);
    }

    public List<ReportFieldDefinition> GetAvailableFields()
    {
        return new List<ReportFieldDefinition>
        {
            // Core identifiers
            new() { FieldName = "id", DisplayName = "Document ID", Type = FieldType.Number, Category = "Core" },
            new() { FieldName = "uuid", DisplayName = "UUID", Type = FieldType.Text, Category = "Core" },
            
            // File information
            new() { FieldName = "file_path", DisplayName = "File Path", Type = FieldType.Text, Category = "File" },
            new() { FieldName = "file_hash", DisplayName = "File Hash", Type = FieldType.Text, Category = "File" },
            new() { FieldName = "file_size", DisplayName = "File Size (bytes)", Type = FieldType.Number, Category = "File" },
            new() { FieldName = "page_count", DisplayName = "Page Count", Type = FieldType.Number, Category = "File" },
            
            // Classification
            new() { FieldName = "document_type", DisplayName = "Document Type", Type = FieldType.Text, Category = "Classification" },
            new() { FieldName = "category", DisplayName = "Category", Type = FieldType.Text, Category = "Classification" },
            new() { FieldName = "classification_confidence", DisplayName = "Classification Confidence", Type = FieldType.Number, Category = "Classification" },
            
            // Organization
            new() { FieldName = "engagement", DisplayName = "Engagement", Type = FieldType.Text, Category = "Organization" },
            new() { FieldName = "section", DisplayName = "Section", Type = FieldType.Text, Category = "Organization" },
            new() { FieldName = "branch", DisplayName = "Branch", Type = FieldType.Text, Category = "Organization" },
            
            // Workflow
            new() { FieldName = "status", DisplayName = "Status", Type = FieldType.Text, Category = "Workflow" },
            new() { FieldName = "confidence", DisplayName = "Confidence", Type = FieldType.Number, Category = "Workflow" },
            
            // Timestamps
            new() { FieldName = "capture_time", DisplayName = "Capture Time", Type = FieldType.DateTime, Category = "Timestamps" },
            new() { FieldName = "reviewed_at", DisplayName = "Reviewed At", Type = FieldType.DateTime, Category = "Timestamps" },
            new() { FieldName = "updated_at", DisplayName = "Updated At", Type = FieldType.DateTime, Category = "Timestamps" },
            new() { FieldName = "archived_at", DisplayName = "Archived At", Type = FieldType.DateTime, Category = "Timestamps" },
            
            // Audit trail
            new() { FieldName = "created_by", DisplayName = "Created By", Type = FieldType.Text, Category = "Audit" },
            new() { FieldName = "reviewed_by", DisplayName = "Reviewed By", Type = FieldType.Text, Category = "Audit" },
            new() { FieldName = "archived_by", DisplayName = "Archived By", Type = FieldType.Number, Category = "Audit" },
            
            // Metadata
            new() { FieldName = "tags", DisplayName = "Tags", Type = FieldType.Text, Category = "Metadata" },
            new() { FieldName = "source", DisplayName = "Source", Type = FieldType.Text, Category = "Metadata" },
            
            // Archive/Compliance
            new() { FieldName = "legal_hold", DisplayName = "Legal Hold", Type = FieldType.Boolean, Category = "Compliance" },
            new() { FieldName = "retention_expiry_date", DisplayName = "Retention Expiry Date", Type = FieldType.Date, Category = "Compliance" },
            new() { FieldName = "is_immutable", DisplayName = "Is Immutable", Type = FieldType.Boolean, Category = "Compliance" },
        };
    }

    public List<FilterOperator> GetOperatorsForField(FieldType type)
    {
        return type switch
        {
            FieldType.Text => new List<FilterOperator>
            {
                FilterOperator.Equals,
                FilterOperator.NotEquals,
                FilterOperator.Contains,
                FilterOperator.StartsWith,
                FilterOperator.EndsWith,
                FilterOperator.IsNull,
                FilterOperator.IsNotNull
            },
            FieldType.Number => new List<FilterOperator>
            {
                FilterOperator.Equals,
                FilterOperator.NotEquals,
                FilterOperator.GreaterThan,
                FilterOperator.LessThan,
                FilterOperator.GreaterThanOrEqual,
                FilterOperator.LessThanOrEqual,
                FilterOperator.Between,
                FilterOperator.IsNull,
                FilterOperator.IsNotNull
            },
            FieldType.Date or FieldType.DateTime => new List<FilterOperator>
            {
                FilterOperator.Equals,
                FilterOperator.NotEquals,
                FilterOperator.GreaterThan,
                FilterOperator.LessThan,
                FilterOperator.Between,
                FilterOperator.IsNull,
                FilterOperator.IsNotNull
            },
            FieldType.Boolean => new List<FilterOperator>
            {
                FilterOperator.Equals,
                FilterOperator.NotEquals
            },
            _ => new List<FilterOperator>
            {
                FilterOperator.Equals,
                FilterOperator.NotEquals,
                FilterOperator.IsNull,
                FilterOperator.IsNotNull
            }
        };
    }

    public async Task<ReportResult> GenerateReportAsync(CustomReportTemplate template, DateTime? startDate = null, DateTime? endDate = null)
    {
        // For simplicity, use ListDocuments and filter in memory
        // In a production system, you would want to build a more sophisticated query
        var documents = _documentStore.ListDocuments(
            dateFrom: startDate?.ToString("O"),
            dateTo: endDate?.ToString("O"),
            limit: 10000, // High limit for custom reports
            newestFirst: true
        );
        
        // Apply template filters in memory
        var filteredDocs = ApplyFilters(documents, template.Filters);
        
        // Apply sorting
        var sortedDocs = ApplySorting(filteredDocs, template.Sorting);
        
        var result = new ReportResult
        {
            Template = template,
            GeneratedAt = DateTime.UtcNow,
            TotalCount = sortedDocs.Count
        };

        // Convert documents to dictionary format based on selected fields
        foreach (var doc in sortedDocs)
        {
            var row = new Dictionary<string, object?>();
            foreach (var field in template.Fields.Where(f => f.IsVisible).OrderBy(f => f.Order))
            {
                row[field.FieldName] = GetFieldValue(doc, field.FieldName);
            }
            result.Data.Add(row);
        }

        return Task.FromResult(result).Result;
    }

    public (bool IsValid, List<string> Errors) ValidateTemplate(CustomReportTemplate template)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            errors.Add("Template name is required");
        }

        if (!template.Fields.Any())
        {
            errors.Add("At least one field must be selected");
        }

        // Validate filters
        foreach (var filter in template.Filters)
        {
            if (string.IsNullOrWhiteSpace(filter.FieldName))
            {
                errors.Add("Filter field name is required");
            }

            if (filter.Operator != FilterOperator.IsNull && filter.Operator != FilterOperator.IsNotNull)
            {
                if (string.IsNullOrWhiteSpace(filter.Value))
                {
                    errors.Add($"Filter value is required for {filter.FieldName}");
                }
            }
        }

        return (errors.Count == 0, errors);
    }

    private List<Document> ApplyFilters(List<Document> documents, List<ReportFilter> filters)
    {
        if (!filters.Any()) return documents;

        return documents.Where(doc =>
        {
            foreach (var filter in filters)
            {
                var fieldValue = GetFieldValue(doc, filter.FieldName);
                var matchesFilter = EvaluateFilter(fieldValue, filter);

                if (filter.LogicalOp == LogicalOperator.And && !matchesFilter)
                {
                    return false;
                }
                else if (filter.LogicalOp == LogicalOperator.Or && matchesFilter)
                {
                    return true;
                }
            }
            return true;
        }).ToList();
    }

    private bool EvaluateFilter(object? fieldValue, ReportFilter filter)
    {
        var filterValue = filter.Value;

        return filter.Operator switch
        {
            FilterOperator.IsNull => fieldValue == null,
            FilterOperator.IsNotNull => fieldValue != null,
            FilterOperator.Equals => fieldValue?.ToString() == filterValue,
            FilterOperator.NotEquals => fieldValue?.ToString() != filterValue,
            FilterOperator.Contains => fieldValue?.ToString()?.Contains(filterValue ?? "", StringComparison.OrdinalIgnoreCase) == true,
            FilterOperator.StartsWith => fieldValue?.ToString()?.StartsWith(filterValue ?? "", StringComparison.OrdinalIgnoreCase) == true,
            FilterOperator.EndsWith => fieldValue?.ToString()?.EndsWith(filterValue ?? "", StringComparison.OrdinalIgnoreCase) == true,
            _ => true
        };
    }

    private List<Document> ApplySorting(List<Document> documents, List<ReportSorting> sorting)
    {
        if (!sorting.Any()) return documents;

        IOrderedEnumerable<Document>? ordered = null;

        foreach (var sort in sorting)
        {
            if (ordered == null)
            {
                ordered = sort.Direction == SortDirection.Ascending
                    ? documents.OrderBy(d => GetFieldValue(d, sort.FieldName))
                    : documents.OrderByDescending(d => GetFieldValue(d, sort.FieldName));
            }
            else
            {
                ordered = sort.Direction == SortDirection.Ascending
                    ? ordered.ThenBy(d => GetFieldValue(d, sort.FieldName))
                    : ordered.ThenByDescending(d => GetFieldValue(d, sort.FieldName));
            }
        }

        return ordered?.ToList() ?? documents;
    }

    private object? GetFieldValue(Document doc, string fieldName)
    {
        return fieldName switch
        {
            "id" => doc.Id,
            "uuid" => doc.Uuid,
            "file_path" => doc.FilePath,
            "file_hash" => doc.FileHash,
            "file_size" => doc.FileSize,
            "page_count" => doc.PageCount,
            "document_type" => doc.DocumentType,
            "category" => doc.Category,
            "classification_confidence" => doc.ClassificationConfidence,
            "engagement" => doc.Engagement,
            "section" => doc.Section,
            "branch" => doc.Branch,
            "status" => doc.Status,
            "confidence" => doc.Confidence,
            "capture_time" => doc.CaptureTime,
            "reviewed_at" => doc.ReviewedAt,
            "updated_at" => doc.UpdatedAt,
            "archived_at" => doc.ArchivedAt,
            "created_by" => doc.CreatedBy,
            "reviewed_by" => doc.ReviewedBy,
            "archived_by" => doc.ArchivedBy,
            "tags" => doc.Tags,
            "source" => doc.Source,
            "legal_hold" => doc.LegalHold,
            "retention_expiry_date" => doc.RetentionExpiryDate,
            "is_immutable" => doc.IsImmutable,
            _ => null
        };
    }
}
