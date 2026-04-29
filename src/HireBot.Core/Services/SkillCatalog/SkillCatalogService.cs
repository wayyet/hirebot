using HireBot.Abstraction;
using HireBot.Abstraction.Models.SkillCatalog;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.SkillCatalog;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.SkillCatalog;

public sealed class SkillCatalogService(
    ISkillCatalogProvider skillCatalogProvider,
    ILogger<SkillCatalogService> logger) : ISkillCatalogService
{
    public async Task<ApiResponse<IReadOnlyList<SkillSummaryDto>>> GetSkillsAsync(
        string? q,
        string? level,
        string? status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skills = await skillCatalogProvider.GetSkillsAsync(q, level, status, cancellationToken);
            return ApiResponse<IReadOnlyList<SkillSummaryDto>>.SuccessResponse(skills);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Skill catalog unavailable from upstream provider.");
            return ApiResponse<IReadOnlyList<SkillSummaryDto>>.ErrorResponse(501, ex.Message);
        }
    }

    public async Task<ApiResponse<SkillDetailDto>> GetSkillAsync(string skillId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return ApiResponse<SkillDetailDto>.ErrorResponse(400, "skillId 不能为空");
        }

        try
        {
            var skill = await skillCatalogProvider.GetSkillAsync(skillId.Trim(), cancellationToken);
            if (skill is null)
            {
                return ApiResponse<SkillDetailDto>.ErrorResponse(404, "技能不存在");
            }

            return ApiResponse<SkillDetailDto>.SuccessResponse(skill);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Skill detail unavailable from upstream provider. SkillId={SkillId}", skillId);
            return ApiResponse<SkillDetailDto>.ErrorResponse(501, ex.Message);
        }
    }
}
