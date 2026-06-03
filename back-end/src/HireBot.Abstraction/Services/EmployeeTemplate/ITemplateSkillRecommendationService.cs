using HireBot.Abstraction.Models.EmployeeTemplate;

namespace HireBot.Abstraction.Services.EmployeeTemplate;

public interface ITemplateSkillRecommendationService
{
    Task<ApiResponse<IReadOnlyList<RecommendedSkillDto>>> GetRecommendedSkillsAsync(
        string templateId,
        int limit = 5,
        CancellationToken cancellationToken = default);
}
