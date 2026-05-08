using HireBot.Abstraction.Models.EmployeeTemplate;

namespace HireBot.Abstraction.Services.EmployeeTemplate;

public interface IEmployeeTemplateService
{
    Task<ApiResponse<EmployeeTemplateListDto>> GetTemplatesAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponse<EmployeeTemplateDetailDto>> GetTemplateDetailAsync(string templateId, CancellationToken cancellationToken = default);
}
