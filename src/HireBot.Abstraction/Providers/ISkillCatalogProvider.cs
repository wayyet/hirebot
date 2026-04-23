using HireBot.Abstraction.Models.SkillCatalog;

namespace HireBot.Abstraction.Providers;

public interface ISkillCatalogProvider
{
    Task<IReadOnlyList<SkillSummaryDto>> GetSkillsAsync(string? q, string? level, string? status, CancellationToken cancellationToken = default);
    Task<SkillDetailDto?> GetSkillAsync(string skillId, CancellationToken cancellationToken = default);
}
