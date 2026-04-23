using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeTemplate;

namespace HireBot.Core.Services.EmployeeTemplate;

public sealed class EmployeeTemplateService(ITemplateDataProvider templateDataProvider) : IEmployeeTemplateService
{
    public async Task<ApiResponse<EmployeeTemplateListDto>> GetTemplatesAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page <= 0 ? 1 : page;
        var normalizedPageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 50);

        var templates = await templateDataProvider.GetAllAsync(cancellationToken);

        var filteredTemplates = templates
            .Where(template => template.IsAvailable)
            .Where(template => IsMatched(template, query))
            .OrderByDescending(template => template.HiredCount)
            .ThenBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totalCount = filteredTemplates.Count;
        var pagedItems = filteredTemplates
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(MapCard)
            .ToArray();

        var result = new EmployeeTemplateListDto(
            normalizedPage,
            normalizedPageSize,
            totalCount,
            pagedItems);

        return ApiResponse<EmployeeTemplateListDto>.SuccessResponse(result);
    }

    public async Task<ApiResponse<EmployeeTemplateDetailDto>> GetTemplateDetailAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(400, "templateId 不能为空");
        }

        var template = await templateDataProvider.GetByIdAsync(templateId.Trim(), cancellationToken);
        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        return ApiResponse<EmployeeTemplateDetailDto>.SuccessResponse(MapDetail(template));
    }

    private static bool IsMatched(EmployeeTemplateDefinition template, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var keyword = query.Trim();

        if (template.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            template.Tagline.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            template.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return template.CoreAbilityTags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               template.CoreAbilities.Any(ability => ability.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               template.InScope.Any(item => item.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               template.OutOfScope.Any(item => item.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               template.Prerequisites.Any(item =>
                   item.SystemName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   item.PermissionName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                   item.Purpose.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               template.SuccessCases.Any(item => item.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static EmployeeTemplateCardDto MapCard(EmployeeTemplateDefinition template)
    {
        return new EmployeeTemplateCardDto(
            template.TemplateId,
            template.IconUrl,
            template.Name,
            template.Tagline,
            template.CoreAbilityTags,
            new TemplateTrustProofDto(
                template.HiredCount,
                template.SuccessRate,
                template.AvgRating),
            template.IsAvailable);
    }

    private static EmployeeTemplateDetailDto MapDetail(EmployeeTemplateDefinition template)
    {
        return new EmployeeTemplateDetailDto(
            template.TemplateId,
            template.IconUrl,
            template.Name,
            template.Tagline,
            template.Description,
            template.CoreAbilities,
            new TemplateResponsibilityBoundaryDto(template.InScope, template.OutOfScope),
            template.Prerequisites,
            template.SuccessCases,
            new TemplateCtaDto("开始雇佣", "/hire"));
    }
}
