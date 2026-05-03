using WorkAudit.Domain;

namespace WorkAudit.Storage;

public interface IReportTemplateStore
{
    Task<int> CreateTemplateAsync(CustomReportTemplate reportTemplate);
    Task<CustomReportTemplate?> GetTemplateAsync(int id);
    Task<List<CustomReportTemplate>> GetTemplatesByUserAsync(int userId);
    Task<List<CustomReportTemplate>> GetSharedTemplatesAsync();
    Task<List<CustomReportTemplate>> GetAllAccessibleTemplatesAsync(int userId);
    Task UpdateTemplateAsync(CustomReportTemplate reportTemplate);
    Task DeleteTemplateAsync(int id);
}
