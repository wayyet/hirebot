using HireBot.Abstraction.Models.SkillCatalog;

namespace HireBot.Abstraction.Services.SkillCatalog;

public interface ISkillCatalogService
{
    Task<ApiResponse<IReadOnlyList<SkillSummaryDto>>> GetSkillsAsync(string? q, string? level, string? status, CancellationToken cancellationToken = default);
    Task<ApiResponse<SkillDetailDto>> GetSkillAsync(string skillId, CancellationToken cancellationToken = default);
}
