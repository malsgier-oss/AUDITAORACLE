using FluentAssertions;
using Moq;
using WorkAudit.Core.Reports;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Reports;

public class ReportBuilderServiceTests
{
    private readonly Mock<IReportTemplateStore> _mockTemplateStore;
    private readonly Mock<IDocumentStore> _mockDocumentStore;
    private readonly ReportBuilderService _service;

    public ReportBuilderServiceTests()
    {
        _mockTemplateStore = new Mock<IReportTemplateStore>();
        _mockDocumentStore = new Mock<IDocumentStore>();
        _service = new ReportBuilderService(_mockTemplateStore.Object, _mockDocumentStore.Object);
    }

    [Fact]
    public async Task CreateTemplateAsync_ShouldSetCreatedAtAndCallStore()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            ReportType = "custom",
            CreatedBy = 1,
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number, Order = 0 }
            }
        };
        
        _mockTemplateStore.Setup(x => x.CreateTemplateAsync(It.IsAny<CustomReportTemplate>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.CreateTemplateAsync(template);

        // Assert
        result.Should().Be(1);
        template.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        _mockTemplateStore.Verify(x => x.CreateTemplateAsync(template), Times.Once);
    }

    [Fact]
    public async Task GetTemplateAsync_ShouldReturnTemplateFromStore()
    {
        // Arrange
        var expectedTemplate = new CustomReportTemplate
        {
            Id = 1,
            Name = "Test Report",
            CreatedBy = 1
        };
        
        _mockTemplateStore.Setup(x => x.GetTemplateAsync(1))
            .ReturnsAsync(expectedTemplate);

        // Act
        var result = await _service.GetTemplateAsync(1);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.Name.Should().Be("Test Report");
    }

    [Fact]
    public async Task UpdateTemplateAsync_ShouldSetUpdatedAtAndCallStore()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Id = 1,
            Name = "Updated Report",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        
        _mockTemplateStore.Setup(x => x.UpdateTemplateAsync(It.IsAny<CustomReportTemplate>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.UpdateTemplateAsync(template);

        // Assert
        template.UpdatedAt.Should().NotBeNull();
        template.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        _mockTemplateStore.Verify(x => x.UpdateTemplateAsync(template), Times.Once);
    }

    [Fact]
    public async Task DeleteTemplateAsync_ShouldCallStore()
    {
        // Arrange
        _mockTemplateStore.Setup(x => x.DeleteTemplateAsync(1))
            .Returns(Task.CompletedTask);

        // Act
        await _service.DeleteTemplateAsync(1);

        // Assert
        _mockTemplateStore.Verify(x => x.DeleteTemplateAsync(1), Times.Once);
    }

    [Fact]
    public void GetAvailableFields_ShouldReturnAllDocumentFields()
    {
        // Act
        var fields = _service.GetAvailableFields();

        // Assert
        fields.Should().NotBeEmpty();
        fields.Should().Contain(f => f.FieldName == "id" && f.Type == FieldType.Number);
        fields.Should().Contain(f => f.FieldName == "document_type" && f.Type == FieldType.Text);
        fields.Should().Contain(f => f.FieldName == "status" && f.Type == FieldType.Text);
        fields.Should().Contain(f => f.FieldName == "branch" && f.Type == FieldType.Text);
        fields.Should().Contain(f => f.Category == "Core");
        fields.Should().Contain(f => f.Category == "Classification");
        fields.Should().Contain(f => f.Category == "Workflow");
        
        // Ensure we have a good variety of field types
        fields.Should().Contain(f => f.Type == FieldType.Text);
        fields.Should().Contain(f => f.Type == FieldType.Number);
        fields.Should().Contain(f => f.Type == FieldType.Date);
        fields.Should().Contain(f => f.Type == FieldType.Boolean);
    }

    [Fact]
    public void GetOperatorsForField_Text_ShouldReturnTextOperators()
    {
        // Act
        var operators = _service.GetOperatorsForField(FieldType.Text);

        // Assert
        operators.Should().Contain(FilterOperator.Equals);
        operators.Should().Contain(FilterOperator.NotEquals);
        operators.Should().Contain(FilterOperator.Contains);
        operators.Should().Contain(FilterOperator.StartsWith);
        operators.Should().Contain(FilterOperator.EndsWith);
        operators.Should().Contain(FilterOperator.IsNull);
        operators.Should().Contain(FilterOperator.IsNotNull);
        operators.Should().NotContain(FilterOperator.GreaterThan);
        operators.Should().NotContain(FilterOperator.Between);
    }

    [Fact]
    public void GetOperatorsForField_Number_ShouldReturnNumericOperators()
    {
        // Act
        var operators = _service.GetOperatorsForField(FieldType.Number);

        // Assert
        operators.Should().Contain(FilterOperator.Equals);
        operators.Should().Contain(FilterOperator.NotEquals);
        operators.Should().Contain(FilterOperator.GreaterThan);
        operators.Should().Contain(FilterOperator.GreaterThanOrEqual);
        operators.Should().Contain(FilterOperator.LessThan);
        operators.Should().Contain(FilterOperator.LessThanOrEqual);
        operators.Should().Contain(FilterOperator.Between);
        operators.Should().Contain(FilterOperator.IsNull);
        operators.Should().Contain(FilterOperator.IsNotNull);
        operators.Should().NotContain(FilterOperator.Contains);
    }

    [Fact]
    public void GetOperatorsForField_Boolean_ShouldReturnBooleanOperators()
    {
        // Act
        var operators = _service.GetOperatorsForField(FieldType.Boolean);

        // Assert
        operators.Should().Contain(FilterOperator.Equals);
        operators.Should().Contain(FilterOperator.NotEquals);
        operators.Should().HaveCount(2);
    }

    [Fact]
    public void GetOperatorsForField_Date_ShouldReturnDateOperators()
    {
        // Act
        var operators = _service.GetOperatorsForField(FieldType.Date);

        // Assert
        operators.Should().Contain(FilterOperator.Equals);
        operators.Should().Contain(FilterOperator.GreaterThan);
        operators.Should().Contain(FilterOperator.LessThan);
        operators.Should().Contain(FilterOperator.Between);
        operators.Should().Contain(FilterOperator.IsNull);
        operators.Should().NotContain(FilterOperator.Contains);
    }

    [Fact]
    public void ValidateTemplate_ValidTemplate_ShouldReturnValid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number }
            }
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplate_EmptyName_ShouldReturnInvalid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "",
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number }
            }
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain("Template name is required");
    }

    [Fact]
    public void ValidateTemplate_NoFields_ShouldReturnInvalid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            Fields = new List<ReportField>()
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain("At least one field must be selected");
    }

    [Fact]
    public void ValidateTemplate_FilterWithoutValue_ShouldReturnInvalid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number }
            },
            Filters = new List<ReportFilter>
            {
                new() { FieldName = "status", Operator = FilterOperator.Equals, Value = null }
            }
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Filter value is required"));
    }

    [Fact]
    public void ValidateTemplate_IsNullFilter_ShouldBeValid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number }
            },
            Filters = new List<ReportFilter>
            {
                new() { FieldName = "notes", Operator = FilterOperator.IsNull, Value = null }
            }
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplate_IsNotNullFilter_ShouldBeValid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Test Report",
            Fields = new List<ReportField>
            {
                new() { FieldName = "id", DisplayName = "ID", Type = FieldType.Number }
            },
            Filters = new List<ReportFilter>
            {
                new() { FieldName = "notes", Operator = FilterOperator.IsNotNull, Value = null }
            }
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTemplate_ComplexValidTemplate_ShouldReturnValid()
    {
        // Arrange
        var template = new CustomReportTemplate
        {
            Name = "Complex Report",
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
            }
        };

        // Act
        var (isValid, errors) = _service.ValidateTemplate(template);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }
}
