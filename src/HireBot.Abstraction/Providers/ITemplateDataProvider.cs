using HireBot.Abstraction.Models.EmployeeTemplate;

namespace HireBot.Abstraction.Providers;

public interface ITemplateDataProvider
{
    Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default);
}
