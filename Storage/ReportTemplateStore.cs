using System.Text.Json;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using WorkAudit.Domain;
using WorkAudit.Storage.Oracle;

namespace WorkAudit.Storage;

public class ReportTemplateStore : IReportTemplateStore
{
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions;
    private static void Prep(OracleCommand cmd)
    {
        cmd.BindByName = true;
        cmd.CommandText = OracleSql.ToOracleBindSyntax(cmd.CommandText);
    }

    public ReportTemplateStore(string dbPath)
    {
        _connectionString = dbPath;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    public async Task<int> CreateTemplateAsync(CustomReportTemplate template)
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO report_templates (
                name, description, report_type, created_by, created_at, 
                is_shared, fields_json, filters_json, sorting_json, grouping_json
            ) VALUES (
                @name, @description, @reportType, @createdBy, @createdAt, 
                @isShared, @fieldsJson, @filtersJson, @sortingJson, @groupingJson
            )
            RETURNING id INTO @rid
        ";

        cmd.Parameters.AddWithValue("@name", template.Name);
        cmd.Parameters.AddWithValue("@description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reportType", template.ReportType);
        cmd.Parameters.AddWithValue("@createdBy", template.CreatedBy);
        cmd.Parameters.AddWithValue("@createdAt", template.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@isShared", template.IsShared ? 1 : 0);
        cmd.Parameters.AddWithValue("@fieldsJson", JsonSerializer.Serialize(template.Fields, _jsonOptions));
        cmd.Parameters.AddWithValue("@filtersJson", JsonSerializer.Serialize(template.Filters, _jsonOptions));
        cmd.Parameters.AddWithValue("@sortingJson", JsonSerializer.Serialize(template.Sorting, _jsonOptions));
        cmd.Parameters.AddWithValue("@groupingJson", template.Grouping != null ? JsonSerializer.Serialize(template.Grouping, _jsonOptions) : DBNull.Value);
        var idParam = new OracleParameter("rid", OracleDbType.Int32, ParameterDirection.Output);
        cmd.Parameters.Add(idParam);
        Prep(cmd);

        await cmd.ExecuteNonQueryAsync();
        var id = Convert.ToInt32(idParam.Value);
        template.Id = id;
        return id;
    }

    public async Task<CustomReportTemplate?> GetTemplateAsync(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, report_type, created_by, created_at, updated_at, 
                   is_shared, fields_json, filters_json, sorting_json, grouping_json
            FROM report_templates
            WHERE id = @id
        ";
        cmd.Parameters.AddWithValue("@id", id);
        Prep(cmd);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapReaderToTemplate(reader);
        }

        return null;
    }

    public async Task<List<CustomReportTemplate>> GetTemplatesByUserAsync(int userId)
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, report_type, created_by, created_at, updated_at, 
                   is_shared, fields_json, filters_json, sorting_json, grouping_json
            FROM report_templates
            WHERE created_by = @userId
            ORDER BY created_at DESC
        ";
        cmd.Parameters.AddWithValue("@userId", userId);
        Prep(cmd);

        var templates = new List<CustomReportTemplate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(MapReaderToTemplate(reader));
        }

        return templates;
    }

    public async Task<List<CustomReportTemplate>> GetSharedTemplatesAsync()
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, report_type, created_by, created_at, updated_at, 
                   is_shared, fields_json, filters_json, sorting_json, grouping_json
            FROM report_templates
            WHERE is_shared = 1
            ORDER BY created_at DESC
        ";
        Prep(cmd);

        var templates = new List<CustomReportTemplate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(MapReaderToTemplate(reader));
        }

        return templates;
    }

    public async Task<List<CustomReportTemplate>> GetAllAccessibleTemplatesAsync(int userId)
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name, description, report_type, created_by, created_at, updated_at, 
                   is_shared, fields_json, filters_json, sorting_json, grouping_json
            FROM report_templates
            WHERE created_by = @userId OR is_shared = 1
            ORDER BY created_at DESC
        ";
        cmd.Parameters.AddWithValue("@userId", userId);
        Prep(cmd);

        var templates = new List<CustomReportTemplate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(MapReaderToTemplate(reader));
        }

        return templates;
    }

    public async Task UpdateTemplateAsync(CustomReportTemplate template)
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE report_templates
            SET name = @name,
                description = @description,
                report_type = @reportType,
                updated_at = @updatedAt,
                is_shared = @isShared,
                fields_json = @fieldsJson,
                filters_json = @filtersJson,
                sorting_json = @sortingJson,
                grouping_json = @groupingJson
            WHERE id = @id
        ";

        cmd.Parameters.AddWithValue("@id", template.Id);
        cmd.Parameters.AddWithValue("@name", template.Name);
        cmd.Parameters.AddWithValue("@description", (object?)template.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reportType", template.ReportType);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@isShared", template.IsShared ? 1 : 0);
        cmd.Parameters.AddWithValue("@fieldsJson", JsonSerializer.Serialize(template.Fields, _jsonOptions));
        cmd.Parameters.AddWithValue("@filtersJson", JsonSerializer.Serialize(template.Filters, _jsonOptions));
        cmd.Parameters.AddWithValue("@sortingJson", JsonSerializer.Serialize(template.Sorting, _jsonOptions));
        cmd.Parameters.AddWithValue("@groupingJson", template.Grouping != null ? JsonSerializer.Serialize(template.Grouping, _jsonOptions) : DBNull.Value);
        Prep(cmd);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteTemplateAsync(int id)
    {
        using var conn = new OracleConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM report_templates WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        Prep(cmd);

        await cmd.ExecuteNonQueryAsync();
    }

    private CustomReportTemplate MapReaderToTemplate(OracleDataReader reader)
    {
        var template = new CustomReportTemplate
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            ReportType = reader.GetString(3),
            CreatedBy = reader.GetInt32(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            UpdatedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
            IsShared = reader.GetInt32(7) == 1
        };

        // Deserialize JSON fields
        var fieldsJson = reader.GetString(8);
        template.Fields = JsonSerializer.Deserialize<List<ReportField>>(fieldsJson, _jsonOptions) ?? new();

        var filtersJson = reader.IsDBNull(9) ? "[]" : reader.GetString(9);
        template.Filters = JsonSerializer.Deserialize<List<ReportFilter>>(filtersJson, _jsonOptions) ?? new();

        var sortingJson = reader.IsDBNull(10) ? "[]" : reader.GetString(10);
        template.Sorting = JsonSerializer.Deserialize<List<ReportSorting>>(sortingJson, _jsonOptions) ?? new();

        if (!reader.IsDBNull(11))
        {
            var groupingJson = reader.GetString(11);
            template.Grouping = JsonSerializer.Deserialize<ReportGrouping>(groupingJson, _jsonOptions);
        }

        return template;
    }
}
