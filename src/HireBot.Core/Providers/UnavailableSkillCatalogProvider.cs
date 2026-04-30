using HireBot.Abstraction.Models.SkillCatalog;
using HireBot.Abstraction.Providers;

namespace HireBot.Core.Providers;

public sealed class UnavailableSkillCatalogProvider : ISkillCatalogProvider
{
    private const string Message = "技能目录未接入真实数据源，Mock 数据已移除。";

    public Task<IReadOnlyList<SkillSummaryDto>> GetSkillsAsync(
        string? q,
        string? level,
        string? status,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }

    public Task<SkillDetailDto?> GetSkillAsync(string skillId, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(Message);
    }
}
