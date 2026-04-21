using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

// TODO: 后续由构建端服务拉取模板数据，替换当前 Mock 数据提供器。
public sealed class BuildServiceTemplateDataProvider : ITemplateDataProvider
{
    public Task<IReadOnlyList<EmployeeTemplateDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("BuildServiceTemplateDataProvider 尚未实现。");
    }

    public Task<EmployeeTemplateDefinition?> GetByIdAsync(string templateId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("BuildServiceTemplateDataProvider 尚未实现。");
    }
}
