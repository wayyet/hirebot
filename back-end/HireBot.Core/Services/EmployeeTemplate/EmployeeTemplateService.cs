using HireBot.Abstraction;
using HireBot.Abstraction.Models.EmployeeTemplate;
using HireBot.Abstraction.Providers;
using HireBot.Abstraction.Services.EmployeeTemplate;
using HireBot.Core.Services.Hiring.TemplatePackages;
using Microsoft.Extensions.Logging;

namespace HireBot.Core.Services.EmployeeTemplate;

internal sealed class EmployeeTemplateService(
    ITemplateDataProvider templateDataProvider,
    ITemplatePackageProvider templatePackageProvider,
    ILogger<EmployeeTemplateService> logger) : IEmployeeTemplateService
{
    public async Task<ApiResponse<EmployeeTemplateDetailDto>> GetTemplateDetailAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(400, "templateId 不能为空");
        }

        var normalizedTemplateId = templateId.Trim();

        EmployeeTemplateDefinition? template;
        try
        {
            template = await templateDataProvider.GetByIdAsync(normalizedTemplateId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Template detail unavailable from upstream data source. TemplateId={TemplateId}", normalizedTemplateId);
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(502, ex.Message);
        }

        if (template is null || !template.IsAvailable)
        {
            return ApiResponse<EmployeeTemplateDetailDto>.ErrorResponse(404, "模板不存在或已下架");
        }

        IReadOnlyList<EmployeeTemplatePackageSkillDto> packageSkills = [];
        try
        {
            var package = await templatePackageProvider.LoadAsync(normalizedTemplateId, cancellationToken);
            packageSkills = package.Skills
                .Select(skill => new EmployeeTemplatePackageSkillDto(
                    skill.Name,
                    skill.RelativePath,
                    skill.Required))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 模板详情主体可以继续展示，技能卡片区域降级为空即可。
            logger.LogWarning(ex, "Template package skills unavailable. TemplateId={TemplateId}", normalizedTemplateId);
        }

        return ApiResponse<EmployeeTemplateDetailDto>.SuccessResponse(MapDetail(template, packageSkills));
    }

    private static EmployeeTemplateDetailDto MapDetail(
        EmployeeTemplateDefinition template,
        IReadOnlyList<EmployeeTemplatePackageSkillDto> packageSkills)
    {
        return new EmployeeTemplateDetailDto(
            template.TemplateId,
            template.IconUrl,
            template.Name,
            template.Tagline,
            template.Description,
            template.DetailDoc,
            template.CoreAbilities,
            new TemplateResponsibilityBoundaryDto(template.InScope, template.OutOfScope),
            template.Prerequisites,
            template.SuccessCases,
            packageSkills,
            new TemplateCtaDto("开始雇佣", "/hire"));
    }
}
