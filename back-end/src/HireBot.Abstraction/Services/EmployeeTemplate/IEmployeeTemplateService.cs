using HireBot.Abstraction.Models.EmployeeTemplate;

namespace HireBot.Abstraction.Services.EmployeeTemplate;


public interface IEmployeeTemplateService
{
    Task<ApiResponse<EmployeeTemplateDetailDto>> GetTemplateDetailAsync(string templateId, CancellationToken cancellationToken = default);
}
