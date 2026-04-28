using FluentAssertions;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Reports;

public class ReportTemplateStoreTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;

    public ReportTemplateStoreTests(OracleTestFixture f) => _fx = f;

    private ReportTemplateStore _store => _fx.ReportTemplateStore;

    [SkippableFact]
    public async Task CreateTemplateAsync_ShouldCreateAndReturnId()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            Description = "Test Description",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = false,
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number, Order = 0 },
                new() { FieldName = "status", DisplayName = "Status", Type = FieldType.Text, Order = 1 }
            },
            Filters = new List<ReportFilter>
            {
                new() { FieldName = "status", Operator = FilterOperator.Equals, Value = "Draft" }
            },
            Sorting = new List<ReportSorting>
            {
                new() { FieldName = "id", Direction = SortDirection.Ascending }
            }
        };

        // Act
        var id = await _store.CreateTemplateAsync(template);

        // Assert
        id.Should().BeGreaterThan(0);
        template.Id.Should().Be(id);
    }

    [SkippableFact]
    public async Task GetTemplateAsync_ExistingTemplate_ShouldReturnTemplate()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number, Order = 0 }
            }
        };
        var id = await _store.CreateTemplateAsync(template);

        // Act
        var result = await _store.GetTemplateAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Test Report");
        result.ReportType.Should().Be("custom");
        result.CreatedBy.Should().Be(_fx.User1Id);
        result.Fields.Should().HaveCount(1);
        result.Fields[0].FieldName.Should().Be("id");
    }

    [SkippableFact]
    public async Task GetTemplateAsync_NonExistingTemplate_ShouldReturnNull()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Act
        var result = await _store.GetTemplateAsync(9999);

        // Assert
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task GetTemplatesByUserAsync_ShouldReturnUserTemplates()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var template1 = new CustomReportTemplate
        {
            Name = "User 1 Report 1",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var template2 = new CustomReportTemplate
        {
            Name = "User 1 Report 2",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var template3 = new CustomReportTemplate
        {
            Name = "User 2 Report",
            ReportType = "custom",
            CreatedBy = _fx.User2Id,
            CreatedAt = DateTime.UtcNow,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };

        await _store.CreateTemplateAsync(template1);
        await _store.CreateTemplateAsync(template2);
        await _store.CreateTemplateAsync(template3);

        // Act
        var result = await _store.GetTemplatesByUserAsync(_fx.User1Id);

        // Assert
        result.Should().HaveCount(2);
        result.All(t => t.CreatedBy == _fx.User1Id).Should().BeTrue();
        result[0].Name.Should().Be("User 1 Report 1"); // Most recent first
    }

    [SkippableFact]
    public async Task GetSharedTemplatesAsync_ShouldReturnOnlySharedTemplates()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var sharedTemplate = new CustomReportTemplate
        {
            Name = "Shared Report",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = true,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var privateTemplate = new CustomReportTemplate
        {
            Name = "Private Report",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = false,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };

        await _store.CreateTemplateAsync(sharedTemplate);
        await _store.CreateTemplateAsync(privateTemplate);

        // Act
        var result = await _store.GetSharedTemplatesAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].IsShared.Should().BeTrue();
        result[0].Name.Should().Be("Shared Report");
    }

    [SkippableFact]
    public async Task GetAllAccessibleTemplatesAsync_ShouldReturnUserAndSharedTemplates()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var userTemplate = new CustomReportTemplate
        {
            Name = "User Template",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = false,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var sharedTemplate = new CustomReportTemplate
        {
            Name = "Shared Template",
            ReportType = "custom",
            CreatedBy = _fx.User2Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = true,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var otherUserTemplate = new CustomReportTemplate
        {
            Name = "Other User Template",
            ReportType = "custom",
            CreatedBy = _fx.User2Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = false,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };

        await _store.CreateTemplateAsync(userTemplate);
        await _store.CreateTemplateAsync(sharedTemplate);
        await _store.CreateTemplateAsync(otherUserTemplate);

        // Act
        var result = await _store.GetAllAccessibleTemplatesAsync(_fx.User1Id);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "User Template");
        result.Should().Contain(t => t.Name == "Shared Template");
        result.Should().NotContain(t => t.Name == "Other User Template");
    }

    [SkippableFact]
    public async Task UpdateTemplateAsync_ShouldUpdateTemplate()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Original Name",
            Description = "Original Description",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            IsShared = false,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var id = await _store.CreateTemplateAsync(template);

        // Modify template
        template.Name = "Updated Name";
        template.Description = "Updated Description";
        template.IsShared = true;
        template.Fields.Add(new ReportField { FieldName = "status", DisplayName = "Status", Type = FieldType.Text });

        // Act
        await _store.UpdateTemplateAsync(template);

        // Assert
        var updated = await _store.GetTemplateAsync(id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Name");
        updated.Description.Should().Be("Updated Description");
        updated.IsShared.Should().BeTrue();
        updated.Fields.Should().HaveCount(2);
        updated.UpdatedAt.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task DeleteTemplateAsync_ShouldRemoveTemplate()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "To Delete",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            Fields = new List<ReportField> { new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number } }
        };
        var id = await _store.CreateTemplateAsync(template);

        // Act
        await _store.DeleteTemplateAsync(id);

        // Assert
        var result = await _store.GetTemplateAsync(id);
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task CreateTemplateAsync_WithComplexFiltersAndSorting_ShouldPreserveAll()
    {
        Skip.IfNot(_fx.IsAvailable);
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Complex Report",
            ReportType = "custom",
            CreatedBy = _fx.User1Id,
            CreatedAt = DateTime.UtcNow,
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number, Order = 0 },
                new() { FieldName = "status", DisplayName = "Status", Type = FieldType.Text, Order = 1 },
                new() { FieldName = "branch", DisplayName = "Branch", Type = FieldType.Text, Order = 2 }
            },
            Filters = new List<ReportFilter>
            {
                new() { FieldName = "status", Operator = FilterOperator.Equals, Value = "Draft", LogicalOp = LogicalOperator.And },
                new() { FieldName = "branch", Operator = FilterOperator.Contains, Value = "Main", LogicalOp = LogicalOperator.Or }
            },
            Sorting = new List<ReportSorting>
            {
                new() { FieldName = "branch", Direction = SortDirection.Ascending },
                new() { FieldName = "id", Direction = SortDirection.Descending }
            },
            Grouping = new ReportGrouping
            {
                FieldName = "branch",
                ShowTotals = true
            }
        };

        // Act
        var id = await _store.CreateTemplateAsync(template);
        var result = await _store.GetTemplateAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Fields.Should().HaveCount(3);
        result.Filters.Should().HaveCount(2);
        result.Filters[0].Operator.Should().Be(FilterOperator.Equals);
        result.Filters[1].LogicalOp.Should().Be(LogicalOperator.Or);
        result.Sorting.Should().HaveCount(2);
        result.Sorting[0].Direction.Should().Be(SortDirection.Ascending);
        result.Grouping.Should().NotBeNull();
        result.Grouping!.FieldName.Should().Be("branch");
        result.Grouping.ShowTotals.Should().BeTrue();
    }
}
