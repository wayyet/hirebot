using HireBot.Abstraction;
using HireBot.Abstraction.Models.SkillCatalog;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.SkillCatalog;

namespace HireBot.Core.Services.SkillCatalog;

public sealed class MockSkillCatalogService(ISkillCatalogProvider skillCatalogProvider) : ISkillCatalogService
{
    public async Task<ApiResponse<IReadOnlyList<SkillSummaryDto>>> GetSkillsAsync(
        string? q,
        string? level,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var skills = await skillCatalogProvider.GetSkillsAsync(q, level, status, cancellationToken);
        return ApiResponse<IReadOnlyList<SkillSummaryDto>>.SuccessResponse(skills);
    }

    public async Task<ApiResponse<SkillDetailDto>> GetSkillAsync(string skillId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return ApiResponse<SkillDetailDto>.ErrorResponse(400, "skillId 不能为空");
        }

        var skill = await skillCatalogProvider.GetSkillAsync(skillId.Trim(), cancellationToken);
        if (skill is null)
        {
            return ApiResponse<SkillDetailDto>.ErrorResponse(404, "技能不存在");
        }

        return ApiResponse<SkillDetailDto>.SuccessResponse(skill);
    }
}
